using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Engine;

/// <summary>
/// Orquesta el camino completo: describir la ventana, elegir la regla,
/// resolver el monitor, calcular el destino, aplicarlo y reintentar.
/// </summary>
public sealed class RuleEngine(
    IWindowDescriber probe,
    MonitorRegistry monitors,
    WindowPlacer placer,
    RetryScheduler retries,
    ApplyLog log)
{
    private readonly Lock _gate = new();
    private RuleSet _set = RuleSet.Empty;

    /// <summary>Ventanas ya vistas por reglas con apply: first, por proceso.</summary>
    private readonly HashSet<(string RuleName, uint Pid)> _firstSeen = new();

    /// <summary>Próximo índice de monitor para cada regla con apply: rotate.</summary>
    private readonly Dictionary<string, int> _rotation = new();

    /// <summary>
    /// Ventanas que estamos tratando ahora mismo. Nuestro propio SetWindowPos
    /// dispara eventos que volverían a entrar acá.
    /// </summary>
    private readonly HashSet<nint> _inFlight = new();

    public void UpdateRules(RuleSet set)
    {
        lock (_gate)
        {
            _set = set;
            _rotation.Clear();
            // Un reload es una decisión nueva del usuario: una regla first no
            // puede seguir ignorando procesos vivos por lo que pasó antes de guardar.
            _firstSeen.Clear();
        }
    }

    public async Task ApplyAllAsync(IEnumerable<nint> handles, CancellationToken ct)
    {
        foreach (var handle in handles)
        {
            if (ct.IsCancellationRequested)
                return;

            await HandleAsync(handle, ct).ConfigureAwait(false);
        }
    }

    public async Task<ApplyResult> HandleAsync(nint handle, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_inFlight.Add(handle))
                return ApplyResult.Ignored;
        }

        try
        {
            return await HandleCoreAsync(handle, ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
                _inFlight.Remove(handle);
        }
    }

    private async Task<ApplyResult> HandleCoreAsync(nint handle, CancellationToken ct)
    {
        // info/rule quedan visibles en el catch para que la entrada de error
        // lleve el título y la regla, si ya se habían resuelto antes de fallar.
        WindowInfo? info = null;
        Rule? rule = null;

        try
        {
            info = probe.Describe(handle);
            if (info is null)
                return ApplyResult.NoMatch;

            RuleSet set;
            lock (_gate)
                set = _set;

            rule = RuleMatcher.FirstMatch(set.Rules, info);
            if (rule is null)
                return Record(info, null, ApplyResult.NoMatch, 0, null);

            if (rule.Apply == ApplyMode.First)
            {
                lock (_gate)
                {
                    if (!_firstSeen.Add((rule.Name, info.ProcessId)))
                        return Record(info, rule, ApplyResult.Ignored, 0, "ya se colocó la primera ventana");
                }
            }

            var alias = NextAlias(rule);
            if (!set.Monitors.TryGetValue(alias, out var declared))
                return Record(info, rule, ApplyResult.Skipped, 0,
                    $"el alias '{alias}' no está declarado en el bloque monitors");

            var monitor = monitors.Resolve(new MonitorId(declared.Path), rule.IfMissing, info.Bounds);
            if (monitor is null)
                return Record(info, rule, ApplyResult.Skipped, 0,
                    $"el monitor '{alias}' no está conectado y la política es {rule.IfMissing}");

            var target = PlacementCalculator.Compute(
                monitor, rule.Place.State, rule.Place.Rect, info.Bounds);

            var startTicks = probe.StartTicksOf(info.ProcessId);

            var outcome = await retries.RunAsync(
                handle,
                rule.EffectiveRetryMs,
                target.ExpectedBounds,
                () => placer.Apply(handle, info.ProcessId, startTicks, target),
                ct).ConfigureAwait(false);

            var detail = $"{monitor.GdiName} {rule.Place.State}";
            if (!outcome.Settled && outcome.Observed.Count > 0)
                detail += $"; último rect observado {outcome.Observed[^1]}";

            return Record(
                info, rule,
                outcome.Settled ? ApplyResult.Applied : ApplyResult.Resisted,
                outcome.Attempts, detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin esto, una excepción de cualquier paso (probe, matcher, resolve,
            // compute, placer o retries) se va como fire-and-forget desde el hook
            // thread y ApplyLog no muestra nada: el único lugar donde el usuario
            // puede ver por qué una ventana no se movió queda en blanco.
            // "error:" distingue esto de un Resisted normal por no asentarse.
            var title = info?.Title ?? string.Empty;
            log.Add(new ApplyEntry(
                DateTimeOffset.Now, handle, title, rule?.Name, ApplyResult.Resisted, 0,
                $"error: {ex.GetType().Name}: {ex.Message}"));
            return ApplyResult.Resisted;
        }
    }

    /// <summary>Para Rotate devuelve el siguiente monitor de la lista; si no, el primero.</summary>
    private string NextAlias(Rule rule)
    {
        var aliases = rule.Place.MonitorAliases;
        if (aliases.Count == 0)
            return string.Empty;

        if (rule.Apply != ApplyMode.Rotate)
            return aliases[0];

        lock (_gate)
        {
            var next = _rotation.TryGetValue(rule.Name, out var i) ? i : 0;
            _rotation[rule.Name] = (next + 1) % aliases.Count;
            return aliases[next];
        }
    }

    private ApplyResult Record(
        WindowInfo info, Rule? rule, ApplyResult result, int attempts, string? detail)
    {
        log.Add(new ApplyEntry(
            DateTimeOffset.Now, info.Handle, info.Title, rule?.Name, result, attempts, detail));
        return result;
    }

    /// <summary>Olvida el estado de apply: first cuando un proceso muere.</summary>
    public void ForgetProcess(uint pid)
    {
        lock (_gate)
            _firstSeen.RemoveWhere(key => key.Pid == pid);
    }
}
