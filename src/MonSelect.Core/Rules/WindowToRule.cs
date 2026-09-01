using System.Text.RegularExpressions;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Rules;

/// <summary>
/// Construye una <see cref="Rule"/> a partir de una ventana ya colocada a mano
/// por el usuario. Pura: no toca Win32 ni el disco. La GUI reúne los datos
/// (WindowInfo, el rect visible, el alias del monitor actual) y esto sólo arma
/// la regla; guardar el archivo es responsabilidad de quien la llama.
/// </summary>
public static class WindowToRule
{
    /// <param name="window">Snapshot de la ventana capturada.</param>
    /// <param name="visibleBounds">
    /// GetVisibleBounds de la ventana, no GetBounds: un rect en este producto es
    /// el rectángulo visible (spec §5, §7 — DWM agrega un marco invisible
    /// alrededor de GetBounds). Sólo se usa cuando el estado es Normal.
    /// </param>
    /// <param name="monitorAlias">Alias ya resuelto contra el bloque monitors:.</param>
    /// <param name="ruleName">Nombre editable por el usuario antes de guardar.</param>
    /// <param name="includeCommandLine">Si el matcher exige también el command line.</param>
    /// <param name="includeTitle">Si el matcher exige también el título, como regex.</param>
    /// <param name="titleRegex">
    /// Regex a usar cuando <paramref name="includeTitle"/> es true. Si es null se
    /// deriva del título literal de la ventana, anclado para no matchear de más.
    /// </param>
    public static Rule Convert(
        WindowInfo window,
        Rect visibleBounds,
        string monitorAlias,
        string ruleName,
        bool includeCommandLine,
        bool includeTitle,
        string? titleRegex = null)
    {
        if (string.IsNullOrWhiteSpace(monitorAlias))
            throw new ArgumentException(
                "La ventana no tiene un alias de monitor resuelto contra el bloque monitors:.",
                nameof(monitorAlias));

        var match = new MatchCriteria(
            Exe: window.ExePath,
            CommandLine: includeCommandLine ? window.CommandLine : null,
            ClassName: window.ClassName,
            Title: includeTitle ? (titleRegex ?? DefaultTitleRegex(window.Title)) : null,
            Aumid: null);

        Rect? rect = window.CurrentState == WindowState.Normal ? visibleBounds : null;
        var place = new RulePlacement(new[] { monitorAlias }, window.CurrentState, rect);

        return new Rule(ruleName, match, place);
    }

    /// <summary>Título literal escapado y anclado, para que no matchee de más por accidente.</summary>
    public static string DefaultTitleRegex(string title)
        => "^" + Regex.Escape(title) + "$";
}
