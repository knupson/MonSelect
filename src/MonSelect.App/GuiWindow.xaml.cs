using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MonSelect.Core.Rules;
using MonSelect.Core.Windows;
using MessageBox = System.Windows.MessageBox;

namespace MonSelect.App;

/// <summary>
/// La GUI de F2: dos pestañas, "Ventanas abiertas" y "Reglas". Vive enteramente
/// en MonSelect.App — Core no sabe que WPF existe. Cualquier acción que mueva
/// una ventana real (aplicar una regla) se despacha por <see cref="Bootstrap.Post"/>;
/// todo lo demás acá (Describe, GetVisibleBounds, GetMonitorForRect) es lectura
/// y es seguro llamarlo desde el hilo de la GUI.
/// </summary>
internal partial class GuiWindow : Window
{
    private readonly Bootstrap _bootstrap;
    private List<OpenWindowRow> _openWindowRows = new();
    private List<RuleRow> _ruleRows = new();
    private nint? _selectedHandle;

    public GuiWindow(Bootstrap bootstrap)
    {
        _bootstrap = bootstrap;
        InitializeComponent();
        RefreshAll();
        // ActualWidth/Height del canvas son 0 hasta el primer layout pass; sin
        // esto el mapa se dibuja una vez con el tamaño de fallback y se queda
        // así hasta el próximo resize manual del usuario.
        Loaded += (_, _) => RenderMap();
    }

    /// <summary>
    /// SelectionChanged es el MISMO routed event en TabControl y en DataGrid
    /// (los dos heredan de Selector) y burbujea: seleccionar una fila en
    /// cualquiera de las dos grillas dispara TAMBIÉN este handler. Sin el
    /// filtro por e.Source, RefreshAll() reasigna SelectedItem en la grilla,
    /// eso vuelve a burbujear hasta acá, y se entra en una recursión infinita
    /// que termina en StackOverflowException — sin excepción capturable, tira
    /// abajo el proceso entero. Hallado seleccionando una fila a mano.
    /// </summary>
    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, Tabs))
            return;

        if (!IsLoaded)
            return;

        RefreshAll();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        ShowConfigError();
        RefreshOpenWindows();
        RefreshRules();
    }

    private void ShowConfigError()
    {
        var error = _bootstrap.LastConfigError;
        ErrorBanner.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = error ?? string.Empty;
    }

    // ---------------------------------------------------------------
    // Ventanas abiertas
    // ---------------------------------------------------------------

    private void RefreshOpenWindows()
    {
        var previouslySelected = _selectedHandle;
        _openWindowRows = BuildOpenWindowRows();
        OpenWindowsGrid.ItemsSource = _openWindowRows;

        var row = previouslySelected is { } h ? _openWindowRows.FirstOrDefault(r => r.Handle == h) : null;
        OpenWindowsGrid.SelectedItem = row;
        _selectedHandle = row?.Handle;

        OpenWindowsStatus.Text = $"{_openWindowRows.Count} ventana(s) visible(s).";
        UpdateCreateRuleAvailability();
        RenderMap();
    }

    private List<OpenWindowRow> BuildOpenWindowRows() => OpenWindowRowBuilder.Build(_bootstrap);

    private void OpenWindowsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedHandle = (OpenWindowsGrid.SelectedItem as OpenWindowRow)?.Handle;
        UpdateCreateRuleAvailability();
        RenderMap();
    }

    private void OpenWindowsMap_SizeChanged(object sender, SizeChangedEventArgs e) => RenderMap();

    /// <summary>
    /// El mapa a escala es la superficie principal de esta pestaña: dibuja todos
    /// los monitores en su posición real (orígenes negativos incluidos) y cada
    /// ventana abierta encima, en el color de acento si tiene regla. Seleccionar
    /// una fila resalta su rectángulo y viceversa — dos vistas de la misma verdad.
    /// </summary>
    private void RenderMap()
    {
        DesktopMap.Render(
            OpenWindowsMap, _bootstrap.Monitors.Monitors, _openWindowRows, _bootstrap.CurrentRuleSet,
            _selectedHandle, OnMapWindowClicked);
    }

    private void OnMapWindowClicked(nint handle)
    {
        _selectedHandle = handle;
        OpenWindowsGrid.SelectedItem = _openWindowRows.FirstOrDefault(r => r.Handle == handle);
        UpdateCreateRuleAvailability();
        RenderMap();
    }

    private void UpdateCreateRuleAvailability()
    {
        var row = SelectedOpenWindow();
        CreateRuleButton.IsEnabled = row is not null;
        OpenWindowsHint.Text = row switch
        {
            null => "Seleccioná una ventana, arriba en la tabla o en el mapa.",
            { Info.ExePath: null } => "Esta ventana no tiene exe accesible (proceso elevado o de otro usuario); la regla no podrá matchear por exe.",
            _ => "Lista para capturar.",
        };

        // El mapa no siempre tiene lugar para el título completo adentro del
        // rectángulo; esta línea sí, y siempre muestra la ventana seleccionada.
        SelectedWindowCaption.Text = row is null
            ? string.Empty
            : $"{row.Title}   ·   {row.Process}   ·   {row.MonitorLabel}   ·   {row.StateLabel}"
              + (row.MatchedRule.Length > 0 ? $"   ·   regla: {row.MatchedRule}" : string.Empty);
    }

    private OpenWindowRow? SelectedOpenWindow() => OpenWindowsGrid.SelectedItem as OpenWindowRow;

    /// <summary>
    /// F3, captura guiada: "colocá la ventana donde la querés y dale aplicar".
    /// Primero se le pide al usuario que acomode la ventana a mano
    /// (<see cref="CaptureWindowDialog"/>, con feedback en vivo); recién al
    /// confirmar se lee dónde quedó y se arma la regla con WindowToRule (Core,
    /// puro) sobre esa posición fresca — no la que tenía al abrir la pestaña.
    /// El nombre y la vista previa de YAML se piden después, reusando
    /// CreateRuleDialog. En ningún paso se mueve ni se toca la ventana real.
    /// </summary>
    private void CreateRule_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedOpenWindow();
        if (selected is null)
            return;

        var capture = new CaptureWindowDialog(_bootstrap, selected.Handle, selected.Title) { Owner = this };
        if (capture.ShowDialog() != true || capture.Captured is not { } row)
            return;

        var set = _bootstrap.CurrentRuleSet;
        var monitor = _bootstrap.MonitorSystem.GetMonitorForRect(row.Info.Bounds);
        var alias = monitor is null ? null : set.AliasFor(monitor.Id);

        if (alias is null)
        {
            MessageBox.Show(this,
                "El monitor donde quedó esta ventana no tiene alias en el bloque monitors: de rules.yaml. " +
                "Reiniciá MonSelect para que lo genere, o agregalo a mano.",
                "No se puede crear la regla", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // F2: cuánto se come de su propio rect visible la app en sí (borde
        // dibujado adentro). Se mide contra la posición recién confirmada, no
        // la de apertura de la pestaña, y se graba explícito en la regla para
        // que aplicarla reproduzca los mismos píxeles sin depender de una
        // remedición futura.
        var bleed = _bootstrap.WindowSystem.MeasureContentInset(row.Handle);

        var dialog = new CreateRuleDialog(
            row, alias, IncludeCmdlineCheck.IsChecked == true, IncludeTitleCheck.IsChecked == true, bleed)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true || dialog.Result is not { } rule)
            return;

        try
        {
            var current = YamlStore.Load(ConfigPaths.Rules);
            var updated = current with { Rules = current.Rules.Append(rule).ToList() };
            YamlStore.Save(ConfigPaths.Rules, updated);
            _bootstrap.ReloadConfig();
            RefreshAll();

            MessageBox.Show(this, $"Regla '{rule.Name}' agregada a rules.yaml.", "MonSelect",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo guardar rules.yaml: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---------------------------------------------------------------
    // Reglas
    // ---------------------------------------------------------------

    private void RefreshRules()
    {
        var set = _bootstrap.CurrentRuleSet;
        var openWindows = DescribeOpenWindows();
        _ruleRows = set.Rules.Select(r => RuleRow.From(r, openWindows)).ToList();
        RulesGrid.ItemsSource = _ruleRows;
        RulesEmptyHint.Visibility = _ruleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RulesStatus.Text = set.Rules.Count == 0
            ? string.Empty
            : $"{set.Rules.Count} regla(s), {set.Rules.Count(r => r.Enabled)} habilitada(s). El orden de arriba hacia abajo es la prioridad.";
    }

    /// <summary>
    /// Mismo criterio que RuleEditorDialog.RenderMatches (título no vacío):
    /// una ventana sin título no le importa al usuario ni puede matchear un
    /// title: en una regla. Sólo lectura, segura desde el hilo de la GUI.
    /// </summary>
    private List<WindowInfo> DescribeOpenWindows()
        => TopLevelWindows.Enumerate()
            .Select(_bootstrap.Probe.Describe)
            .Where(info => info is not null && info.Title.Length > 0)
            .Select(info => info!)
            .ToList();

    private void RulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Sin selección propia por ahora: cada acción (arriba/abajo/borrar/probar)
        // ya lleva su propia fila por DataContext del botón que se apretó.
    }

    /// <summary>F1: doble click abre el editor completo, igual que el botón "Editar".</summary>
    private void RulesGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RulesGrid.SelectedItem is RuleRow row)
            OpenRuleEditor(row.Rule);
    }

    /// <summary>
    /// F1: "Editar" abre un editor con todos los campos de la regla, valida
    /// antes de guardar (RuleValidation, que reusa los mensajes de YamlStore) y
    /// muestra en vivo qué ventanas abiertas matchean. No muta ninguna ventana.
    /// </summary>
    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;

        OpenRuleEditor(row.Rule);
    }

    private void OpenRuleEditor(Rule rule)
    {
        var dialog = new RuleEditorDialog(_bootstrap, rule) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is not { } edited)
            return;

        try
        {
            var set = YamlStore.Load(ConfigPaths.Rules);
            var rules = set.Rules.ToList();
            var index = rules.FindIndex(r => r.Name == dialog.OriginalName);
            if (index < 0)
            {
                MessageBox.Show(this,
                    "La regla ya no está en rules.yaml (¿la borró o renombró otra ventana?); no se guardó el cambio.",
                    "MonSelect", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            rules[index] = edited;
            SaveRules(set with { Rules = rules });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo guardar rules.yaml: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MoveRuleUp_Click(object sender, RoutedEventArgs e) => MoveRule(sender, -1);

    private void MoveRuleDown_Click(object sender, RoutedEventArgs e) => MoveRule(sender, 1);

    private void MoveRule(object sender, int delta)
    {
        if (RowOf(sender) is not { } row)
            return;

        var set = YamlStore.Load(ConfigPaths.Rules);
        var rules = set.Rules.ToList();
        var index = rules.FindIndex(r => r.Name == row.Rule.Name);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= rules.Count)
            return;

        (rules[index], rules[target]) = (rules[target], rules[index]);
        SaveRules(set with { Rules = rules });
    }

    private void ToggleRuleEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;

        var set = YamlStore.Load(ConfigPaths.Rules);
        var rules = set.Rules.ToList();
        var index = rules.FindIndex(r => r.Name == row.Rule.Name);
        if (index < 0)
            return;

        rules[index] = rules[index] with { Enabled = !rules[index].Enabled };
        SaveRules(set with { Rules = rules });
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;

        if (MessageBox.Show(this, $"¿Eliminar la regla '{row.Rule.Name}'? Esto no se puede deshacer.", "Confirmar",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var set = YamlStore.Load(ConfigPaths.Rules);
        var rules = set.Rules.Where(r => r.Name != row.Rule.Name).ToList();
        SaveRules(set with { Rules = rules });
    }

    /// <summary>
    /// "Probar esta regla": aplica ESA regla puntual a las ventanas abiertas que
    /// matcheen, ignorando el orden de prioridad del resto del archivo. Muta
    /// ventanas reales, así que va por Bootstrap.Post — nunca directo desde acá.
    /// </summary>
    private void ApplyRule_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;

        var rule = row.Rule;

        _bootstrap.Post(() =>
        {
            var handles = TopLevelWindows.Enumerate().ToList();
            var applied = _bootstrap.Engine.ApplyRuleAsync(rule, handles, CancellationToken.None)
                .GetAwaiter().GetResult();

            Dispatcher.BeginInvoke(() =>
                MessageBox.Show(this, $"Regla '{rule.Name}' aplicada a {applied} ventana(s).", "MonSelect",
                    MessageBoxButton.OK, MessageBoxImage.Information));
        });
    }

    private static RuleRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as RuleRow;

    private void SaveRules(RuleSet set)
    {
        try
        {
            YamlStore.Save(ConfigPaths.Rules, set);
            _bootstrap.ReloadConfig();
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"No se pudo guardar rules.yaml: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
