using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Frontera hacia las ventanas del sistema. Todo lo que muta una ventana pasa
/// por acá, para que el motor se pueda testear sin un escritorio real.
/// </summary>
public interface IWindowSystem
{
    bool IsWindow(nint handle);
    bool IsVisible(nint handle);
    Rect GetBounds(nint handle);
    uint GetStyle(nint handle);
    void SetStyle(nint handle, uint style);

    /// <summary>SetWindowPos con SWP_FRAMECHANGED, para que el cambio de style se aplique.</summary>
    void ApplyFrameChange(nint handle);

    /// <summary>SetWindowPlacement: fija showCmd y rcNormalPosition en una sola operación.</summary>
    void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition);

    void Show(nint handle, ShowCommand showCmd);
}
