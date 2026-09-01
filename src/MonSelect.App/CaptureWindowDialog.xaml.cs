using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;
using MessageBox = System.Windows.MessageBox;

namespace MonSelect.App;

/// <summary>
/// F3: captura guiada. En vez de leer la posición de la ventana en el
/// instante del click ("Crear regla desde esta ventana" original), el usuario
/// la acomoda a mano mientras este diálogo muestra su monitor/estado/rect en
/// vivo y la resalta en el mapa; recién al confirmar se lee la posición real.
/// No mueve ni toca la ventana capturada en ningún momento — sólo la lee, con
/// un timer, igual que el resto de la GUI.
/// </summary>
internal partial class CaptureWindowDialog : Window
{
    private readonly Bootstrap _bootstrap;
    private readonly nint _handle;
    private readonly DispatcherTimer _timer;

    private List<OpenWindowRow> _rows = new();

    /// <summary>La ventana tal como quedó al confirmar, o null si se canceló o la ventana desapareció.</summary>
    public OpenWindowRow? Captured { get; private set; }

    public CaptureWindowDialog(Bootstrap bootstrap, nint handle, string title)
    {
        _bootstrap = bootstrap;
        _handle = handle;

        InitializeComponent();
        InstructionText.Text =
            $"Acomodá \"{title}\" donde la querés dejar — movela, cambiale el tamaño o maximizala — " +
            "y hacé clic en \"Confirmar posición\" cuando esté lista. MonSelect no la va a tocar mientras tanto.";

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _timer.Tick += (_, _) => Refresh();

        Loaded += (_, _) => { Refresh(); _timer.Start(); };
    }

    private void Refresh()
    {
        if (!_bootstrap.WindowSystem.IsWindow(_handle))
        {
            _timer.Stop();
            LiveStatusText.Text = "La ventana se cerró. Cancelá y elegí otra.";
            ConfirmButton.IsEnabled = false;
            return;
        }

        _rows = OpenWindowRowBuilder.Build(_bootstrap);
        var row = _rows.FirstOrDefault(r => r.Handle == _handle);

        if (row is null)
        {
            // Perdió el título o quedó oculta a mitad de la captura.
            LiveStatusText.Text = "No se puede leer esta ventana ahora mismo (¿se ocultó o minimizó a la bandeja?).";
            ConfirmButton.IsEnabled = false;
        }
        else
        {
            LiveStatusText.Text =
                $"Monitor: {row.MonitorLabel}   ·   Estado: {row.StateLabel}   ·   " +
                $"Rect visible: {row.VisibleBounds}";
            ConfirmButton.IsEnabled = true;
        }

        DesktopMap.Render(MapCanvas, _bootstrap.Monitors.Monitors, _rows, _bootstrap.CurrentRuleSet, _handle, _ => { });
    }

    private void MapCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => Refresh();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var row = _rows.FirstOrDefault(r => r.Handle == _handle);
        if (row is null)
        {
            MessageBox.Show(this, "No se pudo leer la ventana; probá de nuevo.", "MonSelect",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Captured = row;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_Closing(object? sender, CancelEventArgs e) => _timer.Stop();
}
