using System.Diagnostics;
using MonSelect.Core.Engine;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

if (args.Contains("--check-rules"))
{
    var path = args.SkipWhile(a => a != "--check-rules").Skip(1).FirstOrDefault();
    if (path is null)
    {
        Console.WriteLine("Uso: --check-rules <path a rules.yaml>");
        return;
    }

    try
    {
        var set = YamlStore.Parse(File.ReadAllText(path));
        Console.WriteLine($"OK: {set.Monitors.Count} monitor(es), {set.Rules.Count} regla(s).");
        foreach (var rule in set.Rules)
            Console.WriteLine($"  - {rule.Name}: state={rule.Place.State} rect={rule.Place.Rect} bleed={(rule.Bleed?.ToString() ?? "auto")}");
    }
    catch (RuleSetFormatException ex)
    {
        Console.WriteLine($"INVÁLIDO: {ex.Message}");
    }
    return;
}

if (args.Contains("--placement"))
{
    RunPlacementExperiment(new Win32MonitorSystem());
    return;
}

if (args.Contains("--windows"))
{
    var processNameHint = args.SkipWhile(a => a != "--windows").Skip(1).FirstOrDefault();
    if (processNameHint is null)
    {
        Console.WriteLine("Uso: --windows <nombre de proceso>, p.ej. --windows rustdesk");
        return;
    }

    var hwnd = FindVisibleWindowByProcessName(processNameHint);
    if (hwnd == 0)
    {
        Console.WriteLine($"No se encontró una ventana visible cuyo proceso contenga '{processNameHint}'.");
        return;
    }

    var probe = new WindowProbe(new Win32WindowSystem());
    var info = probe.Describe(hwnd);
    if (info is null)
    {
        Console.WriteLine("No se pudo describir la ventana.");
        return;
    }

    Console.WriteLine($"pid      : {info.ProcessId}");
    Console.WriteLine($"exe      : {info.ExePath ?? "<sin acceso>"}");
    Console.WriteLine($"cmdline  : {info.CommandLine ?? "<sin acceso>"}");
    Console.WriteLine($"class    : {info.ClassName}");
    Console.WriteLine($"title    : {info.Title}");
    Console.WriteLine($"bounds   : {info.Bounds}");
    Console.WriteLine($"state    : {info.CurrentState}");
    return;
}

if (args.Contains("--measure-bleed"))
{
    // F2: medición read-only del borde que la propia app dibuja adentro de su
    // rect visible. No mueve, activa ni toca ninguna ventana — sólo enumera y
    // lee rects, para poder correr esto contra apps reales del usuario sin
    // riesgo (WhatsApp, Discord, Chrome ya abiertos en su escritorio).
    var system = new Win32WindowSystem();
    var found = 0;
    NativeMethods.EnumWindows((hwnd, _) =>
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindowTextLengthW(hwnd) == 0)
            return true;

        var buffer = new char[256];
        var len = NativeMethods.GetClassNameW(hwnd, buffer, buffer.Length);
        var className = len > 0 ? new string(buffer, 0, len) : "";

        var titleLen = NativeMethods.GetWindowTextLengthW(hwnd);
        var titleBuf = new char[titleLen + 1];
        var titleWritten = NativeMethods.GetWindowTextW(hwnd, titleBuf, titleBuf.Length);
        var title = titleWritten > 0 ? new string(titleBuf, 0, titleWritten) : "";

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        string procName;
        try { procName = Process.GetProcessById((int)pid).ProcessName; }
        catch { procName = "?"; }

        var wanted = args.SkipWhile(a => a != "--measure-bleed").Skip(1).FirstOrDefault();
        if (wanted is not null
            && !procName.Contains(wanted, StringComparison.OrdinalIgnoreCase)
            && !className.Contains(wanted, StringComparison.OrdinalIgnoreCase)
            && !title.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            return true;

        var outer = system.GetBounds(hwnd);
        var visible = system.GetVisibleBounds(hwnd);
        var inset = system.MeasureContentInset(hwnd);

        Console.WriteLine($"proceso  : {procName} (hwnd 0x{hwnd:X})");
        Console.WriteLine($"clase    : {className}");
        Console.WriteLine($"título   : {title}");
        Console.WriteLine($"outer    : {outer}");
        Console.WriteLine($"visible  : {visible}");
        Console.WriteLine($"bleed    : {inset}px");
        Console.WriteLine();
        found++;
        return true;
    }, 0);

    if (found == 0)
        Console.WriteLine("No se encontró ninguna ventana visible que matchee.");
    return;
}

if (args.Contains("--watch"))
{
    using var watcher = new WindowWatcher();
    var probe = new WindowProbe(new Win32WindowSystem());

    watcher.WindowAppeared += hwnd =>
    {
        var info = probe.Describe(hwnd);
        if (info is null || string.IsNullOrEmpty(info.Title))
            return;

        Console.WriteLine($"{info.Title,-45} {Path.GetFileName(info.ExePath) ?? "?",-20} {info.ClassName}");
    };

    watcher.Start();
    Console.WriteLine("Escuchando. Abrí aplicaciones. Enter para salir.");
    Console.ReadLine();
    return;
}

Console.WriteLine("=== monitores ===");
foreach (var m in new Win32MonitorSystem().GetMonitors())
{
    Console.WriteLine($"{m.GdiName,-14} bounds={m.Bounds}");
    Console.WriteLine($"{"",-14} work  ={m.WorkArea}  primary={m.IsPrimary}");
    Console.WriteLine($"{"",-14} id    ={m.Id.DevicePath}");
}

return;

// --- Experimento: ¿en qué espacio de coordenadas vive rcNormalPosition? ---
//
// Método (self-driving, sin intervención humana): se lanza una aplicación
// real como proceso hijo, se espera a que su ventana principal exista y sea
// visible, se la mueve con SetWindowPos a un rect conocido bien adentro de
// cada monitor bajo prueba, dejándola en estado restaurado (no maximizada),
// y se lee GetWindowRect + GetWindowPlacement. Si rcNormalPosition coincide
// con GetWindowRect, las coordenadas son de pantalla y no hace falta
// corrección. Si difieren, la diferencia es el offset a aplicar. Se repite
// con una segunda aplicación para que la conclusión no dependa de las
// rarezas de un solo programa.
static void RunPlacementExperiment(Win32MonitorSystem monitorSystem)
{
    var monitors = monitorSystem.GetMonitors();

    // DISPLAY4: no primario, con taskbar. DISPLAY2: enteramente en Y negativa,
    // el caso donde un error de signo en la corrección pasaría desapercibido.
    var wantedGdiNames = new[] { @"\\.\DISPLAY4", @"\\.\DISPLAY2" };
    var targets = new List<MonitorInfo>();
    foreach (var name in wantedGdiNames)
    {
        var found = monitors.FirstOrDefault(m => m.GdiName == name);
        if (found is null)
        {
            Console.WriteLine($"ADVERTENCIA: no se encontró el monitor {name} en este equipo; se omite.");
            continue;
        }

        targets.Add(found);
    }

    if (targets.Count == 0)
    {
        Console.WriteLine("BLOCKED: no se encontró ninguno de los monitores esperados (DISPLAY4, DISPLAY2).");
        return;
    }

    foreach (var target in targets)
    {
        Console.WriteLine($"Monitor de prueba: {target.GdiName}  primary={target.IsPrimary}");
        Console.WriteLine($"  bounds = {target.Bounds}");
        Console.WriteLine($"  work   = {target.WorkArea}");
        Console.WriteLine($"  offset work-bounds = ({target.WorkArea.Left - target.Bounds.Left}, " +
                          $"{target.WorkArea.Top - target.Bounds.Top})");
    }

    Console.WriteLine();

    var results = new List<Measurement>();

    if (!MeasureApp("notepad.exe", targets, results))
        Console.WriteLine("BLOCKED: notepad.exe no pudo medirse.");

    // mspaint.exe no siempre está presente (removido en algunas instalaciones
    // de Windows 11); si falla, probar con regedit.exe (ventana Win32 clásica,
    // dueña directa de su HWND). Se descartaron dos candidatos antes de
    // llegar a este: calc.exe, porque su rcNormalPosition queda congelado en
    // el valor de lanzamiento sin seguir a SetWindowPos (rareza de la app
    // empaquetada), y cmd.exe, porque la ventana de consola pertenece al
    // proceso conhost.exe y no a cmd.exe, lo que rompe la búsqueda por nombre
    // de imagen. Ninguno de los dos aporta una segunda opinión válida.
    string? usedSecondApp = null;
    foreach (var candidate in new[] { "mspaint.exe", "regedit.exe" })
    {
        if (MeasureApp(candidate, targets, results))
        {
            usedSecondApp = candidate;
            break;
        }

        Console.WriteLine($"{candidate} no disponible o no se pudo medir en este equipo; probando siguiente candidato.");
    }

    if (usedSecondApp is null)
        Console.WriteLine("BLOCKED: ninguna segunda aplicación candidata (mspaint.exe, regedit.exe) pudo medirse.");
    else
        Console.WriteLine($"Segunda aplicación usada: {usedSecondApp}");

    Console.WriteLine();
    Console.WriteLine("=== Resumen ===");
    foreach (var m in results)
    {
        Console.WriteLine($"{m.App,-12} {m.MonitorName,-14} delta=({m.PlacementRect.Left - m.ScreenRect.Left}," +
                          $"{m.PlacementRect.Top - m.ScreenRect.Top})");
    }

    if (results.Count == 0)
    {
        Console.WriteLine("BLOCKED: no se obtuvo ninguna medición.");
        return;
    }

    var allScreenSpace = results.All(m => m.PlacementRect == m.ScreenRect);
    var allOffsetSpace = results.All(m => m.PlacementRect != m.ScreenRect);
    if (allScreenSpace)
    {
        Console.WriteLine("CONCLUSIÓN: todas las mediciones coinciden. rcNormalPosition está en coordenadas de PANTALLA.");
    }
    else if (allOffsetSpace)
    {
        Console.WriteLine("CONCLUSIÓN: todas las mediciones difieren de forma consistente. rcNormalPosition está desplazado.");
    }
    else
    {
        Console.WriteLine("BLOCKED: mediciones inconsistentes entre sí. Ver detalle arriba antes de sacar una conclusión.");
    }
}

/// <summary>
/// Lanza <paramref name="appExe"/>, mide su WINDOWPLACEMENT en cada monitor de
/// <paramref name="targets"/> y agrega los resultados a <paramref name="results"/>.
/// Devuelve false sin lanzar si el ejecutable no existe en este equipo o si no
/// apareció una ventana visible a tiempo; en ambos casos no queda nada corriendo.
/// </summary>
static bool MeasureApp(string appExe, List<MonitorInfo> targets, List<Measurement> results)
{
    Console.WriteLine($"=== Aplicación: {appExe} ===");
    var imageHint = Path.GetFileNameWithoutExtension(appExe);
    Process? process;
    try
    {
        process = Process.Start(appExe);
    }
    catch (System.ComponentModel.Win32Exception ex)
    {
        Console.WriteLine($"No se pudo lanzar {appExe}: {ex.Message}");
        return false;
    }

    if (process is null)
    {
        Console.WriteLine($"No se pudo lanzar {appExe}.");
        return false;
    }

    var measuredAny = false;
    try
    {
        var hwnd = WaitForMainWindowHandle(process, imageHint, TimeSpan.FromSeconds(15));
        if (hwnd == 0)
        {
            Console.WriteLine($"No apareció una ventana principal visible para {appExe} dentro del timeout.");
            return false;
        }

        // La ventana real puede vivir en un proceso distinto del que lanzamos
        // (p.ej. un stub que reenvía a otro proceso). Matar por el pid dueño
        // de la ventana, no solo el proceso original, evita dejarla huérfana.
        NativeMethods.GetWindowThreadProcessId(hwnd, out var ownerPid);

        foreach (var target in targets)
        {
            var measurement = MoveAndMeasure(hwnd, appExe, target);
            results.Add(measurement);
            measuredAny = true;

            Console.WriteLine($"-- {appExe} en {target.GdiName} --");
            Console.WriteLine($"GetWindowRect       : {measurement.ScreenRect}");
            Console.WriteLine($"rcNormalPosition    : {measurement.PlacementRect}");
            Console.WriteLine($"showCmd             : {measurement.ShowCmd}");
            Console.WriteLine($"delta left = {measurement.PlacementRect.Left - measurement.ScreenRect.Left}");
            Console.WriteLine($"delta top  = {measurement.PlacementRect.Top - measurement.ScreenRect.Top}");
            Console.WriteLine(measurement.PlacementRect == measurement.ScreenRect
                ? "RESULTADO: coordenadas de PANTALLA en esta medición."
                : "RESULTADO: coordenadas desplazadas en esta medición.");
            Console.WriteLine();
        }

        TryKillPid((int)ownerPid);
        return measuredAny;
    }
    finally
    {
        TryKill(process);
    }
}

static Measurement MoveAndMeasure(nint hwnd, string appName, MonitorInfo target)
{
    // Asegurar estado restaurado (ni maximizada ni minimizada) antes de mover.
    NativeMethods.ShowWindow(hwnd, (int)ShowCommand.Restore);
    Thread.Sleep(150);

    const int width = 600;
    const int height = 400;
    var x = target.Bounds.Left + 100;
    var y = target.Bounds.Top + 100;

    NativeMethods.SetWindowPos(
        hwnd,
        0,
        x,
        y,
        width,
        height,
        (uint)(SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate));

    // Dar tiempo a que el sistema termine de aplicar el movimiento.
    Thread.Sleep(300);

    NativeMethods.GetWindowRect(hwnd, out var screenRect);
    var placement = WindowPlacement.Create();
    NativeMethods.GetWindowPlacement(hwnd, ref placement);

    return new Measurement(appName, target.GdiName, screenRect, placement.rcNormalPosition, (ShowCommand)placement.showCmd);
}

static nint WaitForMainWindowHandle(Process process, string imageNameHint, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        process.Refresh();
        if (!process.HasExited)
        {
            var h = process.MainWindowHandle;
            if (h != 0 && NativeMethods.IsWindow(h) && NativeMethods.IsWindowVisible(h))
                return h;
        }

        // Algunas apps modernas (p.ej. paquetes empaquetados) crean la ventana
        // real en un proceso distinto del que lanzamos. Buscar por nombre de
        // imagen cubre ese caso.
        var fallback = FindVisibleWindowByProcessName(imageNameHint);
        if (fallback != 0)
            return fallback;

        Thread.Sleep(200);
    }

    return 0;
}

static nint FindVisibleWindowByProcessName(string imageNameHint)
{
    nint found = 0;
    NativeMethods.EnumWindows((hwnd, _) =>
    {
        if (!NativeMethods.IsWindowVisible(hwnd))
            return true;
        if (NativeMethods.GetWindowTextLengthW(hwnd) == 0)
            return true;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            if (proc.ProcessName.Contains(imageNameHint, StringComparison.OrdinalIgnoreCase))
            {
                found = hwnd;
                return false;
            }
        }
        catch (ArgumentException)
        {
            // El proceso terminó entre GetWindowThreadProcessId y GetProcessById.
        }

        return true;
    }, 0);
    return found;
}

static void TryKillPid(int pid)
{
    if (pid <= 0)
        return;

    try
    {
        using var proc = Process.GetProcessById(pid);
        if (!proc.HasExited)
            proc.Kill();
    }
    catch
    {
        // Proceso ya terminado o inaccesible; no es fatal para el experimento.
    }
}

static void TryKill(Process? process)
{
    if (process is null)
        return;

    try
    {
        process.Refresh();
        if (!process.HasExited)
            process.Kill();
    }
    catch
    {
        // Ya terminado o inaccesible; no dejar la excepción tapar el resultado del experimento.
    }
    finally
    {
        process.Dispose();
    }
}

internal readonly record struct Measurement(string App, string MonitorName, Rect ScreenRect, Rect PlacementRect, ShowCommand ShowCmd);
