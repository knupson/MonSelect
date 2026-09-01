using System.Text.RegularExpressions;

namespace MonSelect.Core.Rules;

/// <summary>
/// Valida una regla en edición contra un <see cref="RuleSet"/> ya cargado —
/// más de lo que <see cref="YamlStore.Parse"/> puede saber por sí solo, porque
/// eso sólo ve el documento, no qué alias están declarados ni si un regex de
/// título compila. Lo que sí puede validar el formato (rotate con menos de
/// dos monitores, rect invertido, estado desconocido) se reusa tal cual —
/// mismo mensaje — rendereando la regla y volviéndola a parsear, en vez de
/// duplicar esas reglas acá con otro texto.
/// </summary>
public static class RuleValidation
{
    /// <summary>Errores encontrados, listos para mostrar en la GUI. Vacío si la regla es válida.</summary>
    public static IReadOnlyList<string> Validate(Rule rule, RuleSet set)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(rule.Name))
            errors.Add("La regla necesita un nombre.");

        if (rule.Place.MonitorAliases.Count == 0)
            errors.Add($"La regla '{rule.Name}' no dice a qué monitor va.");

        foreach (var alias in rule.Place.MonitorAliases)
        {
            if (!set.Monitors.ContainsKey(alias))
                errors.Add($"el alias '{alias}' no está declarado en el bloque monitors");
        }

        if (rule.Match.Title is { } title)
        {
            try
            {
                _ = new Regex(title);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"el regex de título no es válido: {ex.Message}");
            }
        }

        // Reusa YamlStore para lo que ya sabe validar sobre el formato de una
        // regla (rotate con <2 monitores, rect invertido, estado desconocido):
        // renderiza esta única regla dentro del RuleSet actual y la vuelve a
        // parsear, capturando el mismo RuleSetFormatException que vería el
        // usuario si lo hubiera escrito a mano en rules.yaml.
        try
        {
            var probe = set with { Rules = new[] { rule } };
            YamlStore.Parse(YamlStore.Render(probe));
        }
        catch (RuleSetFormatException ex)
        {
            errors.Add(ex.Message);
        }

        return errors;
    }
}
