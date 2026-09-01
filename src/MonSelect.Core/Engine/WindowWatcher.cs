using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Engine;

/// <summary>
/// Dueño del hook de ventanas y de la cola que serializa toda mutación de
/// ventanas. Corre en DOS hilos con roles estrictamente separados:
///
///  - el hilo del hook (<see cref="Pump"/>) sólo tiene el message pump que
///    <c>SetWinEventHook(WINEVENT_OUTOFCONTEXT)</c> necesita para entregar
///    callbacks. Nunca ejecuta código que pueda tocar una ventana ajena.
///  - el hilo de colocación (<see cref="RunPlacementLoop"/>) consume una cola
///    y ahí sí corren <see cref="WindowAppeared"/> y todo lo que llega por
///    <see cref="Post"/>: placement, retries, <c>WindowPlacer.Revert</c>, etc.
///
/// Por qué están separados (hallazgo, no está en el spec original — ver
/// docs/superpowers/findings/f1-acceptance.md y el commit que introdujo esto):
/// <c>SetWindowPos</c>/<c>SetWindowPlacement</c> contra una ventana de OTRO
/// proceso son llamadas síncronas que Windows resuelve enviando mensajes
/// (WM_WINDOWPOSCHANGING/...) a la cola de esa ventana y esperando la
/// respuesta. Si el proceso dueño de esa ventana está momentáneamente
/// ocupado (o colgado), la llamada no vuelve hasta que esa cola se despeje —
/// sin timeout. El primer intento de cada colocación corre síncrono (el
/// primer retry_ms suele ser 0, y <c>Task.CompletedTask</c> no suspende el
/// await), así que si esto corriera en el mismo hilo que <c>GetMessageW</c>
/// del hook, una sola ventana lenta bloquearía la entrega de CUALQUIER
/// evento nuevo — el proceso queda "Responding: True" pero deja de procesar
/// ventanas, sin excepción y sin señal, hasta que esa llamada vuelve (si
/// vuelve). Se reprodujo de forma controlada con una ventana que duerme
/// adentro de WM_WINDOWPOSCHANGING: con el hook y el placement en el mismo
/// hilo, el log deja de crecer por completo mientras dura el bloqueo.
///
/// El spec (sección 4.2) dice "un único hilo dueño de las ventanas, y ese
/// mismo thread ejecuta el placement". Esta clase sigue garantizando la
/// parte que importa de esa regla — un solo hilo ejecuta placement, así que
/// dos colocaciones contra la misma ventana nunca se interleavean y un retry
/// no compite contra sí mismo — pero ya NO es el hilo del hook. Esa mitad de
/// la frase original está mal: acoplar hook y placement es exactamente lo
/// que produce el cuelgue.
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private const uint WM_QUIT_PUMP = 0x0400 + 2;

    private readonly BlockingCollection<Action> _placementQueue = new();
    private readonly ManualResetEventSlim _hookReady = new(false);

    private Thread? _hookThread;
    private Thread? _placementThread;
    private uint _hookThreadId;
    private nint _hook;
    private int _disposed;

    // El delegate se guarda en un campo para que el GC no lo mueva ni lo
    // recolecte: si eso pasa, el hook muere con una violación de acceso que no
    // deja rastro útil.
    private NativeMethods.WinEventProc? _callback;
    private GCHandle _callbackHandle;

    /// <summary>Se dispara en el hilo de colocación, con el hwnd de la ventana que apareció.</summary>
    public event Action<nint>? WindowAppeared;

    public void Start()
    {
        if (_hookThread is not null)
            throw new InvalidOperationException("El watcher ya está corriendo.");

        _placementThread = new Thread(RunPlacementLoop)
        {
            IsBackground = true,
            Name = "MonSelect.PlacementWorker",
        };
        _placementThread.Start();

        _hookThread = new Thread(Pump) { IsBackground = true, Name = "MonSelect.WindowWatcher" };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
        _hookReady.Wait();
    }

    /// <summary>
    /// Encola trabajo en el hilo de colocación — el mismo que procesa
    /// <see cref="WindowAppeared"/> — para que toda mutación de ventanas quede
    /// serializada contra el mismo dueño.
    /// </summary>
    public void Post(Action work) => _placementQueue.Add(work);

    private void RunPlacementLoop()
    {
        foreach (var work in _placementQueue.GetConsumingEnumerable())
            RunSafely(work);
    }

    private void Pump()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();

        _callback = OnWinEvent;
        _callbackHandle = GCHandle.Alloc(_callback);

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_OBJECT_SHOW,
            0,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _hookReady.Set();

        while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_QUIT_PUMP)
                break;

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (_hook != 0)
            NativeMethods.UnhookWinEvent(_hook);
    }

    private void OnWinEvent(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Sólo interesa la ventana en sí, no sus controles hijos.
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
            return;

        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            return;

        // Este callback corre en el hilo del hook: encolar y volver ya mismo es
        // obligatorio. Nada de lo que cuelga de WindowAppeared puede ejecutarse
        // acá — ver el comentario de clase.
        _placementQueue.Add(() => WindowAppeared?.Invoke(hwnd));
    }

    /// <summary>
    /// Una excepción que escape al trabajo encolado mata el hilo de colocación
    /// y con él todo MonSelect, sin dejar rastro. Se traga acá y se registra.
    /// </summary>
    private static void RunSafely(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[watcher] excepción no manejada: {ex}");
        }
    }

    public void Dispose()
    {
        // Seguro llamar dos veces: la segunda vez no vuelve a postear el quit, ni
        // a hacer Join, ni a liberar el GCHandle.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_hookThreadId != 0)
            NativeMethods.PostThreadMessageW(_hookThreadId, WM_QUIT_PUMP, 0, 0);

        _placementQueue.CompleteAdding();

        // UnhookWinEvent sólo corre en Pump(), después de que el loop de mensajes
        // termina. Hasta que eso pase, el hook sigue instalado y Windows puede seguir
        // sosteniendo un puntero al delegate. Si el hilo no confirma su salida, NO
        // liberamos el GCHandle: liberarlo igual dejaría el delegate recolectable
        // mientras el hook sigue activo, exactamente la violación de acceso sin rastro
        // que el GCHandle existe para evitar. Perder el handle hasta que el proceso
        // termine cuesta unos bytes; liberarlo antes de tiempo cuesta un crash.
        var hookExited = _hookThread is null || _hookThread.Join(TimeSpan.FromSeconds(2));

        if (hookExited)
        {
            if (_callbackHandle.IsAllocated)
                _callbackHandle.Free();
        }
        else
        {
            Console.Error.WriteLine(
                "[watcher] el hilo del hook no terminó a tiempo; se retiene el GCHandle del callback a propósito.");
        }

        // El hilo de colocación puede estar bloqueado adentro de un SetWindowPos
        // contra una ventana ajena que nunca vuelve; no vale la pena esperarlo
        // más que esto — es background y el proceso va a terminar de todos modos.
        _placementThread?.Join(TimeSpan.FromSeconds(2));

        _hookReady.Dispose();
        _placementQueue.Dispose();
    }
}
