namespace MonSelect.Core.Rules;

/// <summary>
/// Criterios de identificación de una ventana. Todos son opcionales; los que
/// están presentes se combinan con AND. Un <see cref="MatchCriteria"/> sin
/// ningún campo matchea cualquier ventana.
/// </summary>
public sealed record MatchCriteria(
    string? Exe = null,
    string? CommandLine = null,
    string? ClassName = null,
    string? Title = null,
    string? Aumid = null)
{
    public static readonly MatchCriteria Any = new();

    public bool IsEmpty =>
        Exe is null && CommandLine is null && ClassName is null
        && Title is null && Aumid is null;
}
