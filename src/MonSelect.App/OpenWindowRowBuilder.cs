using System.Diagnostics;
using System.IO;
using MonSelect.Core.Rules;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>
/// Arma la lista de <see cref="OpenWindowRow"/> a partir de las ventanas
/// abiertas ahora. Lo usan tanto la pestaña "Ventanas abiertas" como el
/// diálogo de captura guiada (F3) — los dos necesitan la misma foto del
/// escritorio, sólo que el segundo la refresca varias veces por segundo.
/// </summary>
internal static class OpenWindowRowBuilder
{
    public static List<OpenWindowRow> Build(Bootstrap bootstrap)
    {
        var set = bootstrap.CurrentRuleSet;
        var rows = new List<OpenWindowRow>();

        foreach (var handle in TopLevelWindows.Enumerate())
        {
            var info = bootstrap.Probe.Describe(handle);
            if (info is null || string.IsNullOrEmpty(info.Title))
                continue;

            var visible = bootstrap.WindowSystem.GetVisibleBounds(handle);
            var monitor = bootstrap.MonitorSystem.GetMonitorForRect(info.Bounds);
            var monitorLabel = monitor is null ? "?" : set.AliasFor(monitor.Id) ?? monitor.GdiName;
            var matched = RuleMatcher.FirstMatch(set.Rules, info)?.Name ?? string.Empty;

            rows.Add(new OpenWindowRow
            {
                Handle = handle,
                Title = info.Title,
                Process = ProcessName(info),
                ExePath = info.ExePath ?? "(sin acceso)",
                ClassName = info.ClassName,
                CommandLine = info.CommandLine ?? "(sin acceso)",
                MonitorLabel = monitorLabel,
                StateLabel = info.CurrentState.ToString(),
                MatchedRule = matched,
                Info = info,
                VisibleBounds = visible,
            });
        }

        return rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>WindowInfo no trae el nombre de proceso "lindo"; Process.GetProcessById puede fallar (proceso elevado, ya murió).</summary>
    public static string ProcessName(WindowInfo info)
    {
        try
        {
            return Process.GetProcessById((int)info.ProcessId).ProcessName;
        }
        catch
        {
            return info.ExePath is { } exe ? Path.GetFileNameWithoutExtension(exe) : string.Empty;
        }
    }
}
