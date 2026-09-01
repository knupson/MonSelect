using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Se tira cuando una llamada mutante contra una ventana ajena no vuelve
/// dentro del presupuesto de <see cref="BoundedWindowSystem"/>, o cuando ya
/// hay otra llamada pendiente contra la misma ventana. <c>RuleEngine</c> ya
/// sabe convertir cualquier excepción del placer en una entrada de
/// <c>ApplyLog</c> (ver el catch de <c>HandleCoreAsync</c>) — así que esto no
/// puede volver a producir el silencio del hang original: si se tira, el
/// usuario lo ve en el log.
/// </summary>
public sealed class WindowUnresponsiveException(string message) : Exception(message);

/// <summary>
/// Decora un <see cref="IWindowSystem"/> para que ninguna llamada mutante
/// pueda bloquear al hilo que la invoca más que <see cref="_budget"/>.
///
/// Por qué esto y no <c>SWP_ASYNCWINDOWPOS</c>: ese flag sólo aplica a
/// <c>SetWindowPos</c> (acá, <see cref="ApplyFrameChange"/>). El camino
/// borderless también pasa por <c>SetWindowLongPtr</c> (<see cref="SetStyle"/>,
/// que Windows resuelve enviando <c>WM_STYLECHANGING</c>/<c>WM_STYLECHANGED</c>
/// a la cola de la ventana ajena, igual de bloqueante) y por
/// <c>SetWindowPlacement</c> (<see cref="SetPlacement"/>), ninguna de las dos
/// con un flag asincrónico equivalente. Acotar con un hilo + timeout cubre
/// las cuatro operaciones mutantes por igual, sin depender de qué
/// combinación de flags soporta cada API — y además es la única de las tres
/// alternativas evaluadas que resuelve el caso reproducido: una ventana que
/// contesta bien a un probe (<c>SendMessageTimeout(WM_NULL)</c> volvería
/// enseguida) pero se cuelga específicamente adentro de
/// <c>WM_WINDOWPOSCHANGING</c>, que es el mensaje que estas cuatro llamadas
/// disparan. Un probe previo no la detecta; sólo acotar la llamada real lo
/// hace.
///
/// La llamada real corre en un hilo dedicado (no en el ThreadPool: si nunca
/// vuelve, no queremos agotar el pool con hilos atascados para siempre) y el
/// hilo llamante la espera con <c>Thread.Join(timeout)</c>. Si no contesta a
/// tiempo, este método igual vuelve — el hilo de fondo sigue vivo, bloqueado
/// adentro de Win32, hasta que la ventana ajena se destrabe sola (o nunca).
/// Es una fuga intencional y acotada: como mucho un hilo por ventana
/// atascada, nunca más de uno por handle a la vez (ver <see cref="_inFlight"/>),
/// y se limpia solo en cuanto la llamada real vuelve.
/// </summary>
public sealed class BoundedWindowSystem : IWindowSystem
{
    private readonly IWindowSystem _inner;
    private readonly TimeSpan _budget;

    /// <summary>
    /// Handle -> marca mientras hay una llamada mutante en curso (corriendo o
    /// ya abandonada por timeout) contra esa ventana. Garantiza que nunca hay
    /// dos llamadas reales a la vez contra el mismo hwnd: si Windows las
    /// intercalara, el resultado dependería del orden de llegada y un retry
    /// podría pisar a otro (spec: dos colocaciones contra la misma ventana no
    /// deben interleavearse). Sin esto, una llamada abandonada por timeout
    /// podría seguir corriendo en su hilo de fondo justo cuando un evento
    /// nuevo para la MISMA ventana dispara un HandleAsync fresco — el
    /// _inFlight de RuleEngine no cubre ese caso porque el HandleAsync viejo
    /// ya terminó (con excepción) antes de que la llamada real haya vuelto.
    /// </summary>
    private readonly ConcurrentDictionary<nint, byte> _inFlight = new();

    public BoundedWindowSystem(IWindowSystem inner, TimeSpan budget)
    {
        _inner = inner;
        _budget = budget;
    }

    // Lecturas puras: Windows las resuelve contra la tabla de objetos de
    // win32k, no enviando un mensaje a la cola de la ventana ajena. No
    // bloquean sobre un proceso ocupado, así que pasan derecho.
    public bool IsWindow(nint handle) => _inner.IsWindow(handle);
    public bool IsVisible(nint handle) => _inner.IsVisible(handle);
    public Rect GetBounds(nint handle) => _inner.GetBounds(handle);
    public uint GetStyle(nint handle) => _inner.GetStyle(handle);

    public void SetStyle(nint handle, uint style)
        => RunBounded(handle, nameof(SetStyle), () => _inner.SetStyle(handle, style));

    public void ApplyFrameChange(nint handle)
        => RunBounded(handle, nameof(ApplyFrameChange), () => _inner.ApplyFrameChange(handle));

    public void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition)
        => RunBounded(handle, nameof(SetPlacement), () => _inner.SetPlacement(handle, showCmd, normalPosition));

    public void Show(nint handle, ShowCommand showCmd)
        => RunBounded(handle, nameof(Show), () => _inner.Show(handle, showCmd));

    private void RunBounded(nint handle, string op, Action call)
    {
        if (!_inFlight.TryAdd(handle, 0))
            throw new WindowUnresponsiveException(
                $"{op}: otra llamada contra esta ventana sigue en curso (nunca volvió); " +
                "se salta esta para no intercalar dos mutaciones contra el mismo hwnd");

        ExceptionDispatchInfo? captured = null;

        var worker = new Thread(() =>
        {
            try
            {
                call();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                _inFlight.TryRemove(handle, out _);
            }
        })
        {
            IsBackground = true,
            Name = "MonSelect.BoundedWin32Call",
        };
        worker.Start();

        if (!worker.Join(_budget))
            throw new WindowUnresponsiveException(
                $"{op} contra la ventana no volvió dentro de {_budget.TotalMilliseconds:0}ms; " +
                "se abandona y se sigue con la próxima — el hilo real queda corriendo en el " +
                "fondo por si la ventana se destraba sola");

        captured?.Throw();
    }
}
