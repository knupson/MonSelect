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

    private void UpdateTooltip()
    {
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

    private static void OpenConfig()
        => Process.Start(new ProcessStartInfo(ConfigPaths.Rules) { UseShellExecute = true });

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
