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

        // Declarado afuera del for para que siga visible en el catch: al
        // momento de la excepción vale la cantidad de vueltas ya completadas.
        var i = 0;
        try
        {
            for (; i < scheduleMs.Count; i++)
            {
                // Chequeo temprano, antes del delay y antes de attempt(): si ya
                // nos cancelaron entre vueltas no arrancamos otra — attempt()
                // mueve una ventana real del usuario. Cubre también el camino
                // !comparable, que si no tendría cero chequeos de cancelación.
                if (ct.IsCancellationRequested)
                    return new RetryOutcome(false, i, observed);

                await delay.WaitAsync(scheduleMs[i], ct).ConfigureAwait(false);

                if (!system.IsWindow(handle))
                    return new RetryOutcome(false, i, observed);

                attempt();

                // Nota: acá es i+1 porque el attempt() de esta vuelta sí corrió;
                // en los retornos de arriba es i porque todavía no había corrido.
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
        }
        catch (OperationCanceledException)
        {
            // Task.Delay(ms, ct) — lo que usa RealDelay — tira TaskCanceledException
            // si el token se cancela mientras esperamos. No es un error del sistema,
            // es sólo otra forma de decir "no sigas": se traduce a un resultado
            // limpio en vez de dejar que la excepción escape al llamador.
            return new RetryOutcome(false, i, observed);
        }

        return new RetryOutcome(false, scheduleMs.Count, observed);
    }
}
