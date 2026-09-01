using MonSelect.Core.Monitors;

namespace MonSelect.Core.Rules;

/// <param name="Bleed">
/// Compensación del borde que la propia app dibuja adentro de su rect visible
/// (spec F2). <c>null</c> significa "auto": medir con
/// <see cref="Windows.IWindowSystem.MeasureContentInset"/> en el momento de
/// aplicar. Un valor explícito — incluido 0, "nunca compensar" — pisa la
/// medición. Se expande el rect pedido por este tanto en las cuatro puntas
/// antes de convertir a rect externo (<see cref="Windows.WindowPlacer"/>).
/// </param>
public sealed record Rule(
    string Name,
    MatchCriteria Match,
    RulePlacement Place,
    bool Enabled = true,
    ApplyMode Apply = ApplyMode.All,
    IfMissing IfMissing = IfMissing.Skip,
    IReadOnlyList<int>? RetryMs = null,
    int? Bleed = null)
{
    public static readonly IReadOnlyList<int> DefaultRetryMs = new[] { 0, 150, 400, 800 };

    public IReadOnlyList<int> EffectiveRetryMs => RetryMs ?? DefaultRetryMs;
}
