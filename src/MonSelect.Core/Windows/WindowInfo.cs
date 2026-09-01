using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Snapshot inmutable de una ventana en el momento en que se la examinó.
/// Los campos nullables son los que pueden faltar por permisos: leer el command
/// line de un proceso elevado o de otro usuario falla, y eso no es un error.
/// </summary>
public sealed record WindowInfo(
    nint Handle,
    uint ProcessId,
    string? ExePath,
    string? CommandLine,
    string ClassName,
    string Title,
    string? Aumid,
    Rect Bounds,
    WindowState CurrentState);
