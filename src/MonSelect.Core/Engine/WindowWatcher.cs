using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Engine;

/// <summary>
/// Dueño del hook y del hilo que muta ventanas. Un solo hilo con message pump
/// recibe los eventos y ejecuta las colocaciones: SetWindowPos desde varios
/// hilos contra la misma ventana da resultados dependientes del orden, y los
/// reintentos competirían entre sí.
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private const uint WM_RUN_WORK = 0x0400 + 1; // WM_APP + 1
    private const uint WM_QUIT_PUMP = 0x0400 + 2;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private uint _threadId;
    private nint _hook;

    // El delegate se guarda en un campo para que el GC no lo mueva ni lo
    // recolecte: si eso pasa, el hook muere con una violación de acceso que no
    // deja rastro útil.
    private NativeMethods.WinEventProc? _callback;
    private GCHandle _callbackHandle;

    /// <summary>Se dispara en el hilo dueño, con el hwnd de la ventana que apareció.</summary>
    public event Action<nint>? WindowAppeared;

    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("El watcher ya está corriendo.");

        _thread = new Thread(Pump) { IsBackground = true, Name = "MonSelect.WindowWatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Encola trabajo para que corra en el hilo dueño de las ventanas.</summary>
    public void Post(Action work)
    {
        _queue.Enqueue(work);
        if (_threadId != 0)
            NativeMethods.PostThreadMessageW(_threadId, WM_RUN_WORK, 0, 0);
    }

    private void Pump()
    {
        _threadId = NativeMethods.GetCurrentThreadId();

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

        _ready.Set();

        while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_QUIT_PUMP)
                break;

            if (msg.message == WM_RUN_WORK)
            {
                while (_queue.TryDequeue(out var work))
                    RunSafely(work);
                continue;
            }

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

        RunSafely(() => WindowAppeared?.Invoke(hwnd));
    }

    /// <summary>
    /// Una excepción que escape al callback del hook mata el pump y con él todo
    /// MonSelect, sin dejar rastro. Se traga acá y se registra.
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
        if (_threadId != 0)
            NativeMethods.PostThreadMessageW(_threadId, WM_QUIT_PUMP, 0, 0);

        _thread?.Join(TimeSpan.FromSeconds(2));

        if (_callbackHandle.IsAllocated)
            _callbackHandle.Free();

        _ready.Dispose();
    }
}
