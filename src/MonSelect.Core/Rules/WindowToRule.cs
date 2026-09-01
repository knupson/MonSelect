using System.Text;
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
    /// <param name="includeCommandLine">
    /// Si el matcher exige también el command line. Por defecto NO: exe + class
    /// ya identifican la aplicación, y el command line completo (el exe entre
    /// comillas, de nuevo) es redundante y sólo suma otra forma de romperse.
    /// Opt-in explícito, igual que <paramref name="includeTitle"/>.
    /// </param>
    /// <param name="includeTitle">
    /// Si el matcher exige también el título. Un título cambia todo el tiempo
    /// (contador de notificaciones, nombre de documento, banners transitorios
    /// como "¡Actualizaciones Disponibles!") — por eso NO es parte de la
    /// captura por defecto. Opt-in explícito.
    /// </param>
    /// <param name="titleRegex">
    /// Override a usar cuando <paramref name="includeTitle"/> es true — lo que
    /// haya quedado en el campo editable de la captura guiada. Si es null se
    /// deriva con <see cref="DefaultTitleRegex"/>: la parte estable del título,
    /// sin anclar, para que sobreviva a que el resto del título cambie.
    /// </param>
    /// <param name="commandLineArguments">
    /// Override a usar cuando <paramref name="includeCommandLine"/> es true. Si
    /// es null se deriva con <see cref="ArgumentsOnly"/>: sólo los argumentos,
    /// no el exe completo entre comillas (eso ya está en <c>match.exe</c>).
    /// </param>
    /// <param name="bleed">
    /// Borde propio de la app (F2), medido con <c>IWindowSystem.MeasureContentInset</c>
    /// contra la ventana capturada. <paramref name="visibleBounds"/> es lo que
    /// se ve AHORA, borde incluido; para que aplicar la regla reproduzca esos
    /// mismos píxeles, el rect que se guarda se encoge por este tanto (lo que
    /// <see cref="Windows.WindowPlacer"/> vuelve a expandir al aplicar) y el
    /// valor medido se graba explícito en la regla — no "auto" — para que una
    /// remedición futura contra otra instancia de la ventana no lo corra.
    /// </param>
    public static Rule Convert(
        WindowInfo window,
        Rect visibleBounds,
        string monitorAlias,
        string ruleName,
        bool includeCommandLine,
        bool includeTitle,
        string? titleRegex = null,
        string? commandLineArguments = null,
        int bleed = 0)
    {
        if (string.IsNullOrWhiteSpace(monitorAlias))
            throw new ArgumentException(
                "La ventana no tiene un alias de monitor resuelto contra el bloque monitors:.",
                nameof(monitorAlias));

        var match = new MatchCriteria(
            Exe: window.ExePath,
            CommandLine: includeCommandLine ? (commandLineArguments ?? ArgumentsOnly(window.CommandLine)) : null,
            ClassName: window.ClassName,
            Title: includeTitle ? (titleRegex ?? DefaultTitleRegex(window.Title)) : null,
            Aumid: null);

        Rect? rect = window.CurrentState == WindowState.Normal ? Shrink(visibleBounds, bleed) : null;
        var place = new RulePlacement(new[] { monitorAlias }, window.CurrentState, rect);

        return new Rule(ruleName, match, place, Bleed: bleed);
    }

    /// <summary>Inverso de <see cref="Windows.WindowPlacer.ExpandForBleed"/>.</summary>
    private static Rect Shrink(Rect rect, int bleed)
        => bleed == 0
            ? rect
            : Rect.FromLtrb(
                rect.Left + bleed, rect.Top + bleed, rect.Right - bleed, rect.Bottom - bleed);

    /// <summary>
    /// La parte "estable" de un título: la corrida inicial de letras, hasta el
    /// primer carácter que no sea una letra (dígito, espacio, guion, símbolo,
    /// signo de puntuación). Para "JDownloader 2 - ¡Actualizaciones
    /// Disponibles!" da "JDownloader" — el nombre de la aplicación, sin el
    /// número de versión ni el banner transitorio que le sigue. Devuelve ""
    /// si el título no empieza con una letra; el llamador decide qué hacer
    /// con eso (no ofrecer nada, en vez de escribir un patrón vacío que
    /// matchearía cualquier título).
    /// </summary>
    public static string SuggestedTitleSubstring(string title)
        => new(title.TakeWhile(char.IsLetter).ToArray());

    /// <summary>
    /// Regex por defecto para el título cuando el usuario no da un override
    /// propio: la parte estable (<see cref="SuggestedTitleSubstring"/>),
    /// escapada sólo en los metacaracteres de regex — NO en los espacios, que
    /// <see cref="System.Text.RegularExpressions.Regex.Escape"/> también
    /// escapa aunque no haga falta — y SIN anclar con ^...$, para que
    /// matchee como substring sin importar qué le agreguen antes o después
    /// (un contador de notificaciones, el nombre de un documento, etc.).
    /// </summary>
    public static string DefaultTitleRegex(string title)
        => EscapeRegexMetacharacters(SuggestedTitleSubstring(title));

    /// <summary>
    /// Escapa únicamente los caracteres que un motor de regex trata como
    /// especiales — no toca espacios ni ningún otro carácter "normal", a
    /// diferencia de <c>Regex.Escape</c>.
    /// </summary>
    private static string EscapeRegexMetacharacters(string text)
    {
        const string metacharacters = "\\^$.|?*+()[]{}";
        var result = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (metacharacters.IndexOf(c) >= 0)
                result.Append('\\');
            result.Append(c);
        }

        return result.ToString();
    }

    /// <summary>
    /// Sólo los argumentos de un command line crudo del PEB — la parte
    /// después del ejecutable — porque el ejecutable ya está en
    /// <c>match.exe</c> y repetirlo entre comillas (con su espacio final,
    /// cuando no hay argumentos) no aporta nada y es frágil. Sigue la misma
    /// regla que usa Windows para separar el primer token de un command
    /// line: si empieza con comilla, el primer token termina en la próxima
    /// comilla; si no, termina en el primer espacio. Devuelve null (nada que
    /// ofrecer) cuando no hay argumentos, en vez de una cadena vacía que
    /// terminaría escribiéndose como un patrón vacío-pero-no-null.
    /// </summary>
    public static string? ArgumentsOnly(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        int argsStart;
        if (commandLine[0] == '"')
        {
            var closing = commandLine.IndexOf('"', 1);
            argsStart = closing < 0 ? commandLine.Length : closing + 1;
        }
        else
        {
            var space = commandLine.IndexOf(' ');
            argsStart = space < 0 ? commandLine.Length : space;
        }

        var arguments = commandLine[argsStart..].TrimStart();
        return arguments.Length == 0 ? null : arguments;
    }
}
