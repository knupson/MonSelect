using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MonSelect.Core.Engine;

namespace MonSelect.App;

public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Bootstrap _bootstrap;
    private readonly Icon _trayIcon = TrayIcon.Create();
    private GuiWindow? _gui;

    public TrayHost(Bootstrap bootstrap)
    {
        _bootstrap = bootstrap;

        var menu = new ContextMenuStrip();
        var openItem = menu.Items.Add("Abrir MonSelect", null, (_, _) => OpenGui());
        openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Aplicar reglas ahora", null, (_, _) => ApplyAll());
        menu.Items.Add("Abrir rules.yaml", null, (_, _) => OpenConfig());
        menu.Items.Add("Recargar config", null, (_, _) => _bootstrap.ReloadConfig());
        menu.Items.Add("Revertir bordes quitados (borderless)", null, (_, _) => RevertAllBorderless());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _icon = new NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenGui();

        _bootstrap.ConfigChanged += UpdateTooltip;
        UpdateTooltip();
    }

    /// <summary>
    /// Abre la GUI o, si ya está abierta, la trae al frente en vez de apilar
    /// ventanas — es la única ventana que MonSelect crea, y con una alcanza.
    /// </summary>
    private void OpenGui()
    {
        if (_gui is null)
        {
            _gui = new GuiWindow(_bootstrap);
            _gui.Closed += (_, _) => _gui = null;
            _gui.Show();
            return;
        }

        if (_gui.WindowState == System.Windows.WindowState.Minimized)
            _gui.WindowState = System.Windows.WindowState.Normal;

        _gui.Activate();
    }

    /// <summary>
    /// ConfigChanged se dispara desde el debounce del FileSystemWatcher, en un
    /// hilo de threadpool. NotifyIcon suele tolerar mutaciones fuera del hilo de
    /// UI, pero el resto del código respeta esa frontera, así que acá también.
    /// </summary>
    private void UpdateTooltip()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(UpdateTooltip);
            return;
        }

        var error = _bootstrap.LastConfigError;
        var ruleCount = _bootstrap.CurrentRuleSet.Rules.Count;
        var summary = $"{ruleCount} regla{(ruleCount == 1 ? string.Empty : "s")} cargada{(ruleCount == 1 ? string.Empty : "s")}";

        // NotifyIcon.Text tiene un límite duro de 63 caracteres; se corta en
        // silencio si se pasa, así que hay que armarlo corto a propósito.
        var text = error is null ? $"MonSelect — {summary}" : "MonSelect — error de config";
        _icon.Text = text.Length > 63 ? text[..63] : text;

        if (error is not null)
        {
            _icon.BalloonTipTitle = "rules.yaml tiene un problema";
            // El globo se corta a 255 caracteres; el mensaje completo va al log.
            _icon.BalloonTipText = error.Length > 250 ? error[..250] : error;
            _icon.ShowBalloonTip(5000);
        }
    }

    private void ApplyAll()
        => _bootstrap.Post(() =>
        {
            var handles = TopLevelWindows.Enumerate().ToList();
            _ = _bootstrap.Engine.ApplyAllAsync(handles, CancellationToken.None);
        });

    /// <summary>
    /// Único camino del producto hacia WindowPlacer.Revert (defecto 3 del
    /// acceptance de F1): sin esto, quitarle el marco a una ventana era
    /// definitivo — el registro se guardaba bien en borderless.json pero nada
    /// lo invocaba nunca.
    /// </summary>
    private void RevertAllBorderless()
        => _bootstrap.Post(() =>
        {
            var restored = _bootstrap.RevertAllBorderless();
            ReportRevert(restored);
        });

    /// <summary>Se llama desde el hilo de colocación de WindowWatcher; NotifyIcon quiere el de UI.</summary>
    private void ReportRevert(int restored)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ReportRevert(restored));
            return;
        }

        _icon.BalloonTipTitle = "MonSelect";
        _icon.BalloonTipText = restored == 0
            ? "No había ninguna ventana borderless para revertir."
            : $"Se restauró la barra de título en {restored} ventana(s).";
        _icon.ShowBalloonTip(4000);
    }

    private static void OpenConfig()
        => Process.Start(new ProcessStartInfo(ConfigPaths.Rules) { UseShellExecute = true });

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _trayIcon.Dispose();
        _gui?.Close();
    }
}
