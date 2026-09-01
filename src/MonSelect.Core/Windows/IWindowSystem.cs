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

    /// <summary>
    /// Rectángulo realmente visible. Difiere de <see cref="GetBounds"/> porque
    /// DWM agrega un marco de redimensionado invisible (7px a los lados y abajo
    /// en Win11). Si se ignora, una ventana colocada en el borde del monitor
    /// deja una franja de escritorio a la vista, mientras que el snap de Windows
    /// no la deja: el snap razona en coordenadas visibles.
    /// </summary>
    Rect GetVisibleBounds(nint handle);

    /// <summary>
    /// Pide a DWM que no redondee las esquinas. Windows 11 las redondea en toda
    /// ventana en estado normal, y en una ventana pegada al borde del monitor
    /// eso deja ver el fondo de escritorio en las cuatro puntas.
    /// </summary>
    void SetSquareCorners(nint handle);
    uint GetStyle(nint handle);
    void SetStyle(nint handle, uint style);

    /// <summary>SetWindowPos con SWP_FRAMECHANGED, para que el cambio de style se aplique.</summary>
    void ApplyFrameChange(nint handle);

    /// <summary>SetWindowPlacement: fija showCmd y rcNormalPosition en una sola operación.</summary>
    void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition);

    void Show(nint handle, ShowCommand showCmd);
}
