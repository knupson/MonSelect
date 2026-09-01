using System.Windows;
using System.Windows.Controls;
using MonSelect.Core.Rules;
using MessageBox = System.Windows.MessageBox;

namespace MonSelect.App;

/// <summary>
/// Vista previa editable de la regla que "Crear regla desde esta ventana" está
/// por escribir. WindowToRule (Core, puro) arma la regla; acá sólo se muestra
/// el YAML resultante y se deja cambiar el nombre antes de confirmar.
/// </summary>
internal partial class CreateRuleDialog : Window
{
    private readonly OpenWindowRow _row;
    private readonly string _monitorAlias;
    private readonly bool _includeCommandLine;
    private readonly bool _includeTitle;

    /// <summary>La regla final, lista para agregar a rules.yaml, si el usuario guardó.</summary>
    public Rule? Result { get; private set; }

    public CreateRuleDialog(OpenWindowRow row, string monitorAlias, bool includeCommandLine, bool includeTitle)
    {
        _row = row;
        _monitorAlias = monitorAlias;
        _includeCommandLine = includeCommandLine;
        _includeTitle = includeTitle;

        InitializeComponent();
        NameBox.Text = DefaultName(row);
        NameBox.SelectAll();
        UpdatePreview();
    }

    private static string DefaultName(OpenWindowRow row)
        => string.IsNullOrWhiteSpace(row.Process) ? row.Title : row.Process;

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text) ? "(sin nombre)" : NameBox.Text.Trim();

        try
        {
            var rule = WindowToRule.Convert(
                _row.Info, _row.VisibleBounds, _monitorAlias, name, _includeCommandLine, _includeTitle);
            Result = rule;
            YamlPreview.Text = YamlStore.RenderRule(rule);
        }
        catch (Exception ex)
        {
            Result = null;
            YamlPreview.Text = $"# no se puede armar la regla: {ex.Message}";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            MessageBox.Show(this, "La regla necesita un nombre.", "MonSelect",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdatePreview();
        if (Result is null)
            return;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
