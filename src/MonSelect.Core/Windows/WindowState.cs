namespace MonSelect.Core.Windows;

/// <summary>Estado en el que MonSelect deja una ventana.</summary>
public enum WindowState
{
    /// <summary>Ventana normal con el rect exacto que define la regla.</summary>
    Normal,

    /// <summary>Maximizada respetando el área de trabajo (no tapa la taskbar).</summary>
    Maximized,

    /// <summary>Minimizada.</summary>
    Minimized,

    /// <summary>Sin caption ni thickframe, cubriendo el monitor completo.</summary>
    Borderless,
}
