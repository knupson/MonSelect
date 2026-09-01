using MonSelect.Core.Monitors;

namespace MonSelect.Core.Rules;

public sealed record Rule(
    string Name,
    MatchCriteria Match,
    RulePlacement Place,
    bool Enabled = true,
    ApplyMode Apply = ApplyMode.All,
    IfMissing IfMissing = IfMissing.Skip,
    IReadOnlyList<int>? RetryMs = null)
{
    public static readonly IReadOnlyList<int> DefaultRetryMs = new[] { 0, 150, 400, 800 };

    public IReadOnlyList<int> EffectiveRetryMs => RetryMs ?? DefaultRetryMs;
}
