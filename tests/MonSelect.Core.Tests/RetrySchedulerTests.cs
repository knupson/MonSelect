using MonSelect.Core.Engine;
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class RetrySchedulerTests
{
    private const nint Hwnd = 1234;
    private static readonly Rect Wanted = Rect.FromLtrb(3000, 0, 4920, 1048);
    private static readonly Rect Elsewhere = Rect.FromLtrb(100, 100, 900, 700);
    private static readonly int[] Schedule = { 0, 150, 400, 800 };

    /// <summary>Reloj de mentira: no espera, sólo anota cuánto le pidieron esperar.</summary>
    private sealed class FakeDelay : IDelay
    {
        public List<int> Waits { get; } = new();

        public Task WaitAsync(int milliseconds, CancellationToken ct)
        {
            Waits.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Stops_after_one_attempt_when_the_app_cooperates()
    {
        var system = new FakeWindowSystem();
        system.Add(Hwnd, Elsewhere, 0);
        var delay = new FakeDelay();
        var scheduler = new RetryScheduler(system, delay);

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, Wanted),
            CancellationToken.None);

        Assert.True(result.Settled);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Retries_until_a_stubborn_app_gives_up()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 1; // resiste el primer intento

        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, Wanted),
            CancellationToken.None);

        // Lecturas: [Elsewhere, Wanted, Wanted]. Recién en la tercera hay dos
        // lecturas consecutivas iguales y en el objetivo, que es la condición
        // de corte. Con un solo forcejeo hacen falta tres intentos, no dos.
        Assert.True(result.Settled);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task Gives_up_after_the_budget_and_reports_what_it_saw()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99; // nunca cede

        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, Wanted),
            CancellationToken.None);

        Assert.False(result.Settled);
        Assert.Equal(Schedule.Length, result.Attempts);
        Assert.Equal(Schedule.Length, result.Observed.Count);
        Assert.All(result.Observed, r => Assert.Equal(Elsewhere, r));
    }

    [Fact]
    public async Task Honours_the_configured_schedule()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99;
        var delay = new FakeDelay();

        await new RetryScheduler(system, delay).RunAsync(
            Hwnd, new[] { 0, 250 }, Wanted, attempt: () => { }, CancellationToken.None);

        Assert.Equal(new[] { 0, 250 }, delay.Waits);
    }

    [Fact]
    public async Task An_empty_expected_rect_means_a_single_attempt()
    {
        // Es el caso de Minimized: no hay bounds observables que comparar.
        var system = new FakeWindowSystem();
        system.Add(Hwnd, Elsewhere, 0);
        var delay = new FakeDelay();

        var result = await new RetryScheduler(system, delay).RunAsync(
            Hwnd, Schedule, Rect.FromLtrb(0, 0, 0, 0), attempt: () => { }, CancellationToken.None);

        Assert.True(result.Settled);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Stops_immediately_when_the_window_never_existed()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99;

        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            9999, Schedule, Wanted, attempt: () => { }, CancellationToken.None);

        Assert.False(result.Settled);
        Assert.Equal(0, result.Attempts);
    }

    /// <summary>Saca la ventana del fake justo antes de la N-ésima espera, simulando que se cerró entre reintentos.</summary>
    private sealed class RemovingDelay(FakeWindowSystem system, nint handle, int removeBeforeCallNumber) : IDelay
    {
        private int _calls;

        public Task WaitAsync(int milliseconds, CancellationToken ct)
        {
            _calls++;
            if (_calls == removeBeforeCallNumber)
                system.Remove(handle);

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Stops_when_the_window_disappears_after_an_attempt_has_run()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99; // nunca cede, así el loop llega a una segunda vuelta

        // La ventana desaparece durante la espera de la segunda vuelta, es
        // decir después de que la primera vuelta ya corrió attempt() con éxito.
        var delay = new RemovingDelay(system, Hwnd, removeBeforeCallNumber: 2);
        var scheduler = new RetryScheduler(system, delay);

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, Wanted),
            CancellationToken.None);

        // Una sola vuelta llegó a ejecutar attempt() antes de que la ventana
        // desapareciera en la espera de la segunda.
        Assert.False(result.Settled);
        Assert.Equal(1, result.Attempts);
    }

    /// <summary>Reloj de mentira que se cancela mientras espera, como Task.Delay(ms, ct) de verdad.</summary>
    private sealed class ThrowingDelay : IDelay
    {
        public Task WaitAsync(int milliseconds, CancellationToken ct)
            => throw new TaskCanceledException();
    }

    [Fact]
    public async Task Cancellation_during_the_wait_produces_an_outcome_not_an_exception()
    {
        var system = new FakeWindowSystem();
        system.Add(Hwnd, Elsewhere, 0);

        var scheduler = new RetryScheduler(system, new ThrowingDelay());

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted, attempt: () => { }, CancellationToken.None);

        Assert.False(result.Settled);
        Assert.Equal(0, result.Attempts);
        Assert.Empty(result.Observed);
    }

    [Fact]
    public async Task Cancellation_stops_the_retry_loop()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99;
        using var cts = new CancellationTokenSource();

        var result = await new RetryScheduler(system, new FakeDelay()).RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => cts.Cancel(),
            cts.Token);

        Assert.False(result.Settled);
        Assert.Equal(1, result.Attempts);
    }

    // --- Defecto 2: Maximized siempre se logueaba Resisted por el borde
    // invisible que DWM agrega en Windows 11 (~8px) alrededor del rect
    // maximizado. GetWindowRect nunca coincide exactamente con el work area
    // aunque la ventana esté perfectamente maximizada, así que para Maximized
    // "asentado" es show state + monitor, no igualdad de rect. Ver
    // RetryScheduler.IsMaximizedSettled.

    private static readonly Rect RightMonitorBounds = Rect.FromLtrb(3000, 0, 4920, 1080);
    private static readonly Rect RightMonitorWorkArea = Rect.FromLtrb(3000, 0, 4920, 1048);

    // El "borde invisible" real medido en Windows 11 (docs/superpowers/findings/f1-acceptance.md):
    // (2992,-8)-(4928,1056) en vez del work area exacto (3000,0)-(4920,1048).
    private static readonly Rect ObservedWithDwmBorder = Rect.FromLtrb(2992, -8, 4928, 1056);

    [Fact]
    public async Task Maximized_settles_on_the_first_attempt_despite_the_DWM_invisible_resize_border()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        var delay = new FakeDelay();
        var scheduler = new RetryScheduler(system, delay);

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, RightMonitorWorkArea,
            attempt: () =>
            {
                window.Style = (uint)WindowStyles.Maximize;
                system.SetObservedBounds(Hwnd, ObservedWithDwmBorder);
            },
            CancellationToken.None,
            state: WindowState.Maximized,
            monitorBounds: RightMonitorBounds);

        Assert.True(result.Settled);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Maximized_keeps_retrying_if_the_show_state_never_actually_becomes_maximized()
    {
        // El bounds observado cae justo donde se lo pidió, pero WS_MAXIMIZE
        // nunca se prendió (la app se resistió a maximizar de verdad): no
        // alcanza con el rect, tiene que ser realmente Maximized.
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, RightMonitorWorkArea,
            attempt: () => system.SetObservedBounds(Hwnd, RightMonitorWorkArea),
            CancellationToken.None,
            state: WindowState.Maximized,
            monitorBounds: RightMonitorBounds);

        Assert.False(result.Settled);
        Assert.Equal(Schedule.Length, result.Attempts);
    }

    [Fact]
    public async Task Maximized_keeps_retrying_if_it_lands_maximized_on_the_wrong_monitor()
    {
        // Maximiza de verdad (WS_MAXIMIZE prendido) pero en OTRO monitor: el
        // show state solo no alcanza, tiene que ser en el monitor pedido.
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, RightMonitorWorkArea,
            attempt: () =>
            {
                window.Style = (uint)WindowStyles.Maximize;
                system.SetObservedBounds(Hwnd, Elsewhere); // otro monitor, lejos de RightMonitorBounds
            },
            CancellationToken.None,
            state: WindowState.Maximized,
            monitorBounds: RightMonitorBounds);

        Assert.False(result.Settled);
        Assert.Equal(Schedule.Length, result.Attempts);
    }

    [Fact]
    public async Task Normal_still_requires_the_exact_rect_and_ignores_a_near_miss()
    {
        // La distinción es sólo para Maximized/Minimized: Normal (y Borderless,
        // mismo camino) siguen exigiendo el rect exacto. Una ventana a un par de
        // píxeles del target sigue contando como resistida, no "suficientemente
        // cerca" — un blanket tolerance acá se filtraría a Borderless, donde un
        // margen de 8px se ve como una franja de escritorio.
        var system = new FakeWindowSystem();
        system.Add(Hwnd, Elsewhere, 0);
        var scheduler = new RetryScheduler(system, new FakeDelay());

        var almostWanted = Rect.FromLtrb(Wanted.Left + 2, Wanted.Top, Wanted.Right + 2, Wanted.Bottom);

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, almostWanted),
            CancellationToken.None,
            state: WindowState.Normal,
            monitorBounds: RightMonitorBounds);

        Assert.False(result.Settled);
        Assert.Equal(Schedule.Length, result.Attempts);
    }
}
