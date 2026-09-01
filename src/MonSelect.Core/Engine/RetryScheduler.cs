using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Engine;

/// <summary>Espera inyectable, para que los tests no tarden segundos reales.</summary>
public interface IDelay
{
    Task WaitAsync(int milliseconds, CancellationToken ct);
}

public sealed class RealDelay : IDelay
{
    public Task WaitAsync(int milliseconds, CancellationToken ct)
        => milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, ct);
}

/// <param name="Settled">True si la ventana terminó donde se quería.</param>
/// <param name="Observed">Bounds leídos después de cada intento, para el log.</param>
public sealed record RetryOutcome(bool Settled, int Attempts, IReadOnlyList<Rect> Observed);

/// <summary>
/// Reaplica una colocación hasta que la ventana se queda quieta donde
/// corresponde. Existe porque muchas apps (Electron y Qt sobre todo) se
/// reposicionan solas después de mostrarse: un único intento falla en silencio.
/// </summary>
public sealed class RetryScheduler(IWindowSystem system, IDelay delay)
{
    public async Task<RetryOutcome> RunAsync(
        nint handle,
        IReadOnlyList<int> scheduleMs,
        Rect expectedBounds,
        Action attempt,
        CancellationToken ct)
    {
        var observed = new List<Rect>();

        if (!system.IsWindow(handle))
            return new RetryOutcome(false, 0, observed);

        // Minimizada no tiene bounds observables: se aplica una vez y listo.
        var comparable = !expectedBounds.IsEmpty;

        for (var i = 0; i < scheduleMs.Count; i++)
        {
            await delay.WaitAsync(scheduleMs[i], ct).ConfigureAwait(false);

            if (!system.IsWindow(handle))
                return new RetryOutcome(false, i, observed);

            attempt();

            if (!comparable)
                return new RetryOutcome(true, i + 1, observed);

            var actual = system.GetBounds(handle);
            observed.Add(actual);

            if (ct.IsCancellationRequested)
                return new RetryOutcome(false, i + 1, observed);

            // Se corta cuando el resultado es el buscado y además es estable:
            // dos lecturas seguidas iguales significan que la app dejó de pelear.
            var onTarget = actual == expectedBounds;
            var stable = observed.Count >= 2 && observed[^1] == observed[^2];

            if (onTarget && (stable || observed.Count == 1))
                return new RetryOutcome(true, i + 1, observed);
        }

        return new RetryOutcome(false, scheduleMs.Count, observed);
    }
}
