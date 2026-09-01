using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MonSelect.Core.Engine;

namespace MonSelect.App;

public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Bootstrap _bootstrap;

    public TrayHost(Bootstrap bootstrap)
    {
        _bootstrap = bootstrap;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Aplicar reglas ahora", null, (_, _) => ApplyAll());
        menu.Items.Add("Abrir rules.yaml", null, (_, _) => OpenConfig());
        menu.Items.Add("Recargar config", null, (_, _) => _bootstrap.ReloadConfig());
        menu.Items.Add("Revertir bordes quitados (borderless)", null, (_, _) => RevertAllBorderless());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };

        _bootstrap.ConfigChanged += UpdateTooltip;
        UpdateTooltip();
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
        _icon.Text = error is null
            ? "MonSelect"
            : "MonSelect — error de config";

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
    }
}
