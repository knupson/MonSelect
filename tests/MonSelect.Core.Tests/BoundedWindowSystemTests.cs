using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

/// <summary>
/// Cubre el defecto 1 (v2) del F1 acceptance: el fix anterior movió la
/// llamada bloqueante de hilo (hook -> colocación) pero no la acotó, así que
/// una sola ventana ajena colgada seguía parando TODO el motor detrás de
/// ella, sin límite y sin señal. Ver BoundedWindowSystem y
/// .superpowers/sdd/2026-09-01-monselect-f1/hang-fix-2-report.md.
/// </summary>
public class BoundedWindowSystemTests
{
    private static readonly TimeSpan ShortBudget = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Ventana de mentira controlable a mano: SetPlacement puede quedarse
    /// esperando en un <see cref="ManualResetEventSlim"/> hasta que el test
    /// lo libere, simulando una app ajena colgada adentro de
    /// WM_WINDOWPOSCHANGING sin depender de Thread.Sleep con duraciones fijas.
    /// </summary>
    private sealed class ControllableWindowSystem : IWindowSystem
    {
        public int SetPlacementCalls;
        public ManualResetEventSlim? Gate;
        public Exception? ThrowOnSetPlacement;

        public bool IsWindow(nint handle) => true;
        public bool IsVisible(nint handle) => true;
        public Rect GetBounds(nint handle) => default;

        public Rect GetVisibleBounds(nint handle) => GetBounds(handle);
        public void SetSquareCorners(nint handle) { }
        public uint GetStyle(nint handle) => 0;
        public void SetStyle(nint handle, uint style) { }
        public void ApplyFrameChange(nint handle) { }

        public void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition)
        {
            Interlocked.Increment(ref SetPlacementCalls);
            Gate?.Wait();
            if (ThrowOnSetPlacement is { } ex)
                throw ex;
        }

        public void Show(nint handle, ShowCommand showCmd) { }
    }

    [Fact]
    public void Abandons_a_call_that_never_returns_within_budget()
    {
        var inner = new ControllableWindowSystem();
        using var gate = new ManualResetEventSlim(false);
        inner.Gate = gate;
        var system = new BoundedWindowSystem(inner, ShortBudget);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<WindowUnresponsiveException>(
            () => system.SetPlacement(1, ShowCommand.Normal, default));
        sw.Stop();

        // Bien por debajo de "para siempre": el llamador recupera el control
        // cerca del budget, no cuando (si alguna vez) la llamada real vuelve.
        Assert.True(sw.ElapsedMilliseconds < 2000,
            $"tardó {sw.ElapsedMilliseconds}ms en volver; debería acotarse al budget");

        gate.Set(); // libera el hilo de fondo para no dejarlo colgado tras el test
    }

    [Fact]
    public void Rejects_a_second_call_against_the_same_handle_while_the_first_is_stuck()
    {
        var inner = new ControllableWindowSystem();
        using var gate = new ManualResetEventSlim(false);
        inner.Gate = gate;
        var system = new BoundedWindowSystem(inner, ShortBudget);

        Assert.Throws<WindowUnresponsiveException>(
            () => system.SetPlacement(1, ShowCommand.Normal, default));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = Assert.Throws<WindowUnresponsiveException>(() => system.SetStyle(1, 0));
        sw.Stop();

        // Rechazo inmediato, no otra espera de budget completo: la ventana ya
        // tiene una llamada real en curso, encolar una segunda la
        // interleavearía contra la misma ventana.
        Assert.True(sw.ElapsedMilliseconds < ShortBudget.TotalMilliseconds,
            $"el segundo intento tardó {sw.ElapsedMilliseconds}ms; debería fallar casi instantáneo");
        Assert.Contains("otra llamada", ex.Message);

        gate.Set();
    }

    [Fact]
    public void A_stuck_window_does_not_block_calls_against_a_different_window()
    {
        var inner = new ControllableWindowSystem();
        using var gate = new ManualResetEventSlim(false);
        inner.Gate = gate;
        var system = new BoundedWindowSystem(inner, ShortBudget);

        Assert.Throws<WindowUnresponsiveException>(
            () => system.SetPlacement(1, ShowCommand.Normal, default));

        // El handle 1 sigue "en curso" (el hilo de fondo todavía está
        // esperando el gate); el handle 2 es una ventana distinta y no
        // comparte el gate desde este punto, así que debe pasar derecho.
        inner.Gate = null;
        system.SetPlacement(2, ShowCommand.Normal, default);

        gate.Set();
    }

    [Fact]
    public void Recovers_once_the_abandoned_call_actually_returns()
    {
        var inner = new ControllableWindowSystem();
        using var gate = new ManualResetEventSlim(false);
        inner.Gate = gate;
        var system = new BoundedWindowSystem(inner, ShortBudget);

        Assert.Throws<WindowUnresponsiveException>(
            () => system.SetPlacement(1, ShowCommand.Normal, default));

        gate.Set(); // la ventana ajena "se destraba sola"

        // El hilo de fondo termina de forma asincrónica; se sondea en vez de
        // asumir un tiempo fijo, para no depender de la velocidad de la
        // máquina que corre el test.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        Exception? last = null;
        do
        {
            try
            {
                system.SetStyle(1, 0);
                last = null;
                break;
            }
            catch (WindowUnresponsiveException ex)
            {
                last = ex;
                Thread.Sleep(10);
            }
        } while (DateTime.UtcNow < deadline);

        Assert.Null(last);
    }

    [Fact]
    public void Propagates_the_inner_exception_type_when_the_call_fails_fast()
    {
        var inner = new ControllableWindowSystem { ThrowOnSetPlacement = new InvalidOperationException("boom") };
        var system = new BoundedWindowSystem(inner, TimeSpan.FromSeconds(1));

        var ex = Assert.Throws<InvalidOperationException>(
            () => system.SetPlacement(1, ShowCommand.Normal, default));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void Forwards_reads_and_fast_writes_to_the_inner_system_unchanged()
    {
        var inner = new FakeWindowSystem();
        inner.Add(1, Rect.FromLtrb(0, 0, 100, 100), 0);
        var system = new BoundedWindowSystem(inner, TimeSpan.FromSeconds(1));

        Assert.True(system.IsWindow(1));
        Assert.True(system.IsVisible(1));
        Assert.Equal(inner.GetBounds(1), system.GetBounds(1));

        var target = Rect.FromLtrb(10, 10, 50, 50);
        system.SetPlacement(1, ShowCommand.Normal, target);

        Assert.Contains(inner.Calls, c => c.StartsWith("SetPlacement"));
        Assert.Equal(target, inner[1].NormalPosition);
    }
}
