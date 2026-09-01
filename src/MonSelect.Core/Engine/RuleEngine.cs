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

    /// <summary>
    /// Ventanas ya colocadas. Una regla se aplica cuando la ventana aparece, no
    /// cada vez que el usuario la toca: EVENT_SYSTEM_FOREGROUND se dispara con
    /// cada cambio de foco, y sin esto MonSelect devuelve la ventana a su sitio
    /// apenas la movés, con lo que no se puede reacomodar nada a mano.
    /// </summary>
    private readonly HashSet<nint> _placed = new();

    public void UpdateRules(RuleSet set)
    {
        lock (_gate)
        {
            _set = set;
            _rotation.Clear();
            // Config nueva: las ventanas ya colocadas pueden querer otro destino.
            _placed.Clear();
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

            await HandleAsync(handle, ct, force: true).ConfigureAwait(false);
        }
    }

    /// <param name="force">
    /// Reaplica aunque la ventana ya haya sido colocada. Lo usa "aplicar reglas
    /// ahora"; el camino automático nunca fuerza.
    /// </param>
    public async Task<ApplyResult> HandleAsync(nint handle, CancellationToken ct, bool force = false)
    {
        lock (_gate)
        {
            if (!_inFlight.Add(handle))
                return ApplyResult.Ignored;

            if (!force && _placed.Contains(handle))
            {
                _inFlight.Remove(handle);
                return ApplyResult.Ignored;
            }
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

            return await ApplyRuleToWindowAsync(info, rule, set, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Aplica una regla concreta a las ventanas abiertas que matcheen, sin pasar
    /// por la prioridad del resto del ruleset ni por el gate de "ya colocada".
    /// Lo usa el botón "Probar esta regla" de la GUI: el usuario quiere ver el
    /// efecto de ESA regla puntual, no el resultado que ganaría bajo first-match
    /// si hay otra regla antes en el archivo. Debe correr en el hilo dueño de la
    /// mutación de ventanas — ver <c>Bootstrap.Post</c>.
    /// </summary>
    /// <returns>Cuántas ventanas quedaron aplicadas (Settled).</returns>
    public async Task<int> ApplyRuleAsync(Rule rule, IEnumerable<nint> handles, CancellationToken ct)
    {
        RuleSet set;
        lock (_gate)
            set = _set;

        var applied = 0;

        foreach (var handle in handles)
        {
            if (ct.IsCancellationRequested)
                break;

            bool acquired;
            lock (_gate)
                acquired = _inFlight.Add(handle);

            if (!acquired)
                continue;

            try
            {
                var info = probe.Describe(handle);
                if (info is null || !RuleMatcher.Matches(rule, info))
                    continue;

                var result = await ApplyRuleToWindowAsync(info, rule, set, ct).ConfigureAwait(false);
                if (result == ApplyResult.Applied)
                    applied++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Add(new ApplyEntry(
                    DateTimeOffset.Now, handle, string.Empty, rule.Name, ApplyResult.Resisted, 0,
                    $"error: {ex.GetType().Name}: {ex.Message}"));
            }
            finally
            {
                lock (_gate)
                    _inFlight.Remove(handle);
            }
        }

        return applied;
    }

    /// <summary>
    /// El tramo común entre el camino automático y "probar esta regla": resolver
    /// alias, monitor, calcular destino, aplicar con retry y loguear.
    /// </summary>
    private async Task<ApplyResult> ApplyRuleToWindowAsync(
        WindowInfo info, Rule rule, RuleSet set, CancellationToken ct)
    {
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

        // Bleed explícito (incluido 0, "nunca compensar") pisa la medición;
        // "auto" (null) mide contra la ventana real una sola vez, antes del
        // primer intento — la medición no cambia entre reintentos porque el
        // borde que dibuja la app es parte de su chrome, no de su contenido.
        var bleed = rule.Bleed ?? probe.MeasureContentInset(info.Handle);

        var outcome = await retries.RunAsync(
            info.Handle,
            rule.EffectiveRetryMs,
            target.ExpectedBounds,
            () => placer.Apply(info.Handle, info.ProcessId, startTicks, target, bleed),
            ct,
            rule.Place.State,
            monitor.Bounds).ConfigureAwait(false);

        var detail = $"{monitor.GdiName} {rule.Place.State}";
        if (!outcome.Settled && outcome.Observed.Count > 0)
            detail += $"; último rect observado {outcome.Observed[^1]}";

        // Colocada. No se vuelve a tocar hasta que la config cambie o el
        // usuario pida "aplicar reglas ahora".
        lock (_gate)
            _placed.Add(info.Handle);

        return Record(
            info, rule,
            outcome.Settled ? ApplyResult.Applied : ApplyResult.Resisted,
            outcome.Attempts, detail);
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
