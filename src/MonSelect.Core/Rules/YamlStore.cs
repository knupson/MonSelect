using System.Text;
using MonSelect.Core.Monitors;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;
using YamlDotNet.RepresentationModel;

namespace MonSelect.Core.Rules;

/// <summary>
/// Carga y guarda rules.yaml. Se parsea con el modelo de representación en vez
/// de con el deserializador por objetos porque el campo monitor acepta tanto un
/// escalar como una secuencia, y porque los errores tienen que nombrar el valor
/// exacto que el usuario escribió mal.
/// </summary>
public static class YamlStore
{
    public static RuleSet Load(string path)
    {
        if (!File.Exists(path))
            return RuleSet.Empty;

        return Parse(File.ReadAllText(path));
    }

    public static RuleSet Parse(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (Exception ex)
        {
            throw new RuleSetFormatException($"rules.yaml no es YAML válido: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
            return RuleSet.Empty;

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new RuleSetFormatException("rules.yaml tiene que empezar con un mapa de claves.");

        var version = (int)ReadScalarLong(root, "version", 1);
        var monitors = ReadMonitors(root);
        var (defaultIfMissing, defaultRetry) = ReadDefaults(root);
        var rules = ReadRules(root, defaultIfMissing, defaultRetry);

        return new RuleSet(version, monitors, rules);
    }

    public static void Save(string path, RuleSet set)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var writer = new StringWriter();
        writer.WriteLine($"version: {set.Version}");

        if (set.Monitors.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("monitors:");
            foreach (var (alias, monitor) in set.Monitors)
            {
                writer.WriteLine($"  {alias}:");
                writer.WriteLine($"    path: '{monitor.Path}'");
                writer.WriteLine($"    label: {Quote(monitor.Label)}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("defaults:");
        writer.WriteLine("  if_missing: skip");
        writer.WriteLine($"  retry_ms: [{string.Join(", ", Rule.DefaultRetryMs)}]");

        writer.WriteLine();
        writer.WriteLine("rules:");
        foreach (var rule in set.Rules)
            WriteRule(writer, rule);

        File.WriteAllText(path, writer.ToString());
    }

    /// <summary>
    /// Renderiza una sola regla exactamente como <see cref="Save"/> la escribiría.
    /// Lo usa la GUI para mostrar la vista previa de "Crear regla desde esta
    /// ventana" antes de tocar el archivo.
    /// </summary>
    public static string RenderRule(Rule rule)
    {
        var writer = new StringWriter();
        WriteRule(writer, rule);
        return writer.ToString();
    }

    private static void WriteRule(TextWriter writer, Rule rule)
    {
        writer.WriteLine($"  - name: {Quote(rule.Name)}");
        if (!rule.Enabled)
            writer.WriteLine("    enabled: false");

        writer.WriteLine("    match:");
        WriteOptional(writer, "exe", rule.Match.Exe);
        WriteOptional(writer, "cmdline", rule.Match.CommandLine);
        WriteOptional(writer, "class", rule.Match.ClassName);
        WriteOptional(writer, "title", rule.Match.Title);
        WriteOptional(writer, "aumid", rule.Match.Aumid);

        writer.WriteLine("    place:");
        writer.WriteLine(rule.Place.MonitorAliases.Count == 1
            ? $"      monitor: {rule.Place.MonitorAliases[0]}"
            : $"      monitor: [{string.Join(", ", rule.Place.MonitorAliases)}]");
        writer.WriteLine($"      state: {rule.Place.State.ToString().ToLowerInvariant()}");
        if (rule.Place.Rect is { } r)
            writer.WriteLine($"      rect: [{r.Left}, {r.Top}, {r.Right}, {r.Bottom}]");

        if (rule.Apply != ApplyMode.All)
            writer.WriteLine($"    apply: {rule.Apply.ToString().ToLowerInvariant()}");
        if (rule.IfMissing != IfMissing.Skip)
            writer.WriteLine($"    if_missing: {rule.IfMissing.ToString().ToLowerInvariant()}");
        if (rule.RetryMs is { } retry)
            writer.WriteLine($"    retry_ms: [{string.Join(", ", retry)}]");
    }

    private static void WriteOptional(TextWriter writer, string key, string? value)
    {
        if (value is not null)
            writer.WriteLine($"      {key}: {Quote(value)}");
    }

    /// <summary>
    /// Produce un escalar YAML entre comillas dobles correctamente escapado.
    /// El orden importa: la barra invertida se escapa primero, para que las
    /// barras invertidas que introducen los demás casos no se vuelvan a
    /// escapar.
    /// </summary>
    private static string Quote(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    result.Append("\\\\");
                    break;
                case '"':
                    result.Append("\\\"");
                    break;
                case '\n':
                    result.Append("\\n");
                    break;
                case '\r':
                    result.Append("\\r");
                    break;
                case '\t':
                    result.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                        result.Append("\\x").Append(((int)c).ToString("x2"));
                    else
                        result.Append(c);
                    break;
            }
        }

        result.Append('"');
        return result.ToString();
    }

    private static IReadOnlyDictionary<string, MonitorAlias> ReadMonitors(YamlMappingNode root)
    {
        var result = new Dictionary<string, MonitorAlias>(StringComparer.OrdinalIgnoreCase);
        if (!TryGet(root, "monitors", out var node) || node is not YamlMappingNode monitors)
            return result;

        foreach (var (key, value) in monitors.Children)
        {
            var alias = ((YamlScalarNode)key).Value ?? string.Empty;
            if (value is not YamlMappingNode entry)
                throw new RuleSetFormatException($"El monitor '{alias}' tiene que ser un mapa con path y label.");

            var path = ReadScalarString(entry, "path")
                ?? throw new RuleSetFormatException($"El monitor '{alias}' no tiene path.");
            var label = ReadScalarString(entry, "label") ?? alias;

            result[alias] = new MonitorAlias(path, label);
        }

        return result;
    }

    private static (IfMissing, IReadOnlyList<int>) ReadDefaults(YamlMappingNode root)
    {
        var ifMissing = IfMissing.Skip;
        var retry = Rule.DefaultRetryMs;

        if (!TryGet(root, "defaults", out var node) || node is not YamlMappingNode defaults)
            return (ifMissing, retry);

        if (ReadScalarString(defaults, "if_missing") is { } raw)
            ifMissing = ParseEnum<IfMissing>(raw, "if_missing");

        if (ReadIntList(defaults, "retry_ms") is { } list)
            retry = list;

        return (ifMissing, retry);
    }

    private static IReadOnlyList<Rule> ReadRules(
        YamlMappingNode root, IfMissing defaultIfMissing, IReadOnlyList<int> defaultRetry)
    {
        if (!TryGet(root, "rules", out var node))
            return Array.Empty<Rule>();

        if (node is not YamlSequenceNode sequence)
            throw new RuleSetFormatException("rules tiene que ser una lista.");

        var rules = new List<Rule>();
        foreach (var item in sequence)
        {
            if (item is not YamlMappingNode entry)
                throw new RuleSetFormatException("Cada regla tiene que ser un mapa.");

            var name = ReadScalarString(entry, "name") ?? $"regla {rules.Count + 1}";
            var enabled = ReadScalarString(entry, "enabled") is not "false";

            var match = TryGet(entry, "match", out var m) && m is YamlMappingNode matchNode
                ? new MatchCriteria(
                    ReadScalarString(matchNode, "exe"),
                    ReadScalarString(matchNode, "cmdline"),
                    ReadScalarString(matchNode, "class"),
                    ReadScalarString(matchNode, "title"),
                    ReadScalarString(matchNode, "aumid"))
                : MatchCriteria.Any;

            if (!TryGet(entry, "place", out var p) || p is not YamlMappingNode placeNode)
                throw new RuleSetFormatException($"La regla '{name}' no tiene bloque place.");

            var place = new RulePlacement(
                ReadMonitorAliases(placeNode, name),
                ParseEnum<WindowState>(
                    ReadScalarString(placeNode, "state") ?? "normal", "state"),
                ReadRect(placeNode));

            var apply = ParseEnum<ApplyMode>(ReadScalarString(entry, "apply") ?? "all", "apply");
            var ifMissing = ReadScalarString(entry, "if_missing") is { } rawIfMissing
                ? ParseEnum<IfMissing>(rawIfMissing, "if_missing")
                : defaultIfMissing;
            var retry = ReadIntList(entry, "retry_ms") ?? defaultRetry;

            if (apply == ApplyMode.Rotate && place.MonitorAliases.Count < 2)
                throw new RuleSetFormatException(
                    $"La regla '{name}' usa apply: rotate pero place.monitor no es una lista de dos o más alias.");

            rules.Add(new Rule(name, match, place, enabled, apply, ifMissing, retry));
        }

        return rules;
    }

    private static IReadOnlyList<string> ReadMonitorAliases(YamlMappingNode place, string ruleName)
    {
        if (!TryGet(place, "monitor", out var node))
            throw new RuleSetFormatException($"La regla '{ruleName}' no dice a qué monitor va.");

        return node switch
        {
            YamlScalarNode scalar when scalar.Value is { Length: > 0 } v => new[] { v },
            YamlSequenceNode seq => seq
                .OfType<YamlScalarNode>()
                .Select(s => s.Value ?? string.Empty)
                .Where(s => s.Length > 0)
                .ToArray(),
            _ => throw new RuleSetFormatException(
                $"La regla '{ruleName}' tiene un monitor que no es ni un alias ni una lista de alias."),
        };
    }

    private static Rect? ReadRect(YamlMappingNode place)
    {
        if (!TryGet(place, "rect", out var node) || node is not YamlSequenceNode seq)
            return null;

        var values = seq.OfType<YamlScalarNode>()
            .Select(s => int.TryParse(s.Value, out var v)
                ? v
                : throw new RuleSetFormatException($"rect tiene un valor no numérico: '{s.Value}'."))
            .ToArray();

        if (values.Length != 4)
            throw new RuleSetFormatException("rect tiene que tener exactamente cuatro enteros: [left, top, right, bottom].");

        return Rect.FromLtrb(values[0], values[1], values[2], values[3]);
    }

    private static IReadOnlyList<int>? ReadIntList(YamlMappingNode node, string key)
    {
        if (!TryGet(node, key, out var value) || value is not YamlSequenceNode seq)
            return null;

        return seq.OfType<YamlScalarNode>()
            .Select(s => int.TryParse(s.Value, out var v)
                ? v
                : throw new RuleSetFormatException($"{key} tiene un valor no numérico: '{s.Value}'."))
            .ToArray();
    }

    private static bool TryGet(YamlMappingNode node, string key, out YamlNode value)
    {
        foreach (var (k, v) in node.Children)
        {
            if (k is YamlScalarNode scalar
                && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static string? ReadScalarString(YamlMappingNode node, string key)
        => TryGet(node, key, out var value) && value is YamlScalarNode scalar
            && scalar.Value is { Length: > 0 } text
            && !string.Equals(text, "null", StringComparison.OrdinalIgnoreCase)
                ? text
                : null;

    private static long ReadScalarLong(YamlMappingNode node, string key, long fallback)
        => ReadScalarString(node, key) is { } text && long.TryParse(text, out var value)
            ? value
            : fallback;

    private static T ParseEnum<T>(string raw, string field) where T : struct, Enum
        => Enum.TryParse<T>(raw, ignoreCase: true, out var value)
            ? value
            : throw new RuleSetFormatException(
                $"'{raw}' no es un valor válido para {field}. Opciones: {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");
}
