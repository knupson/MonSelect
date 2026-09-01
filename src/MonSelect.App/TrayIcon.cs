using System.Drawing;
using System.Drawing.Drawing2D;

namespace MonSelect.App;

/// <summary>
/// Dibuja el icono de bandeja en código: un pequeño glyph de monitor. El icono
/// anterior era <c>SystemIcons.Application</c>, indistinguible de cualquier otra
/// cosa en la bandeja — el dueño del producto no lograba encontrarlo. No hace
/// falta agregar un .ico al build: 16x16 alcanza y se genera en un instante.
/// </summary>
internal static class TrayIcon
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Fondo del glyph: grafito, el mismo tono "ink" de la GUI.
            using var body = new SolidBrush(Color.FromArgb(255, 14, 17, 22));
            using var frame = new Pen(Color.FromArgb(255, 214, 220, 228), 1.4f);
            using var screen = new SolidBrush(Color.FromArgb(255, 232, 163, 61)); // accent ámbar

            // Marco del monitor.
            var monitorRect = new Rectangle(1, 1, 14, 10);
            g.FillRectangle(body, monitorRect);
            g.DrawRectangle(frame, monitorRect);

            // Pantalla interior, en el color de acento: es lo que lo hace
            // reconocible de un vistazo entre íconos grises.
            g.FillRectangle(screen, 3, 3, 10, 6);

            // Pie del monitor.
            using var stand = new Pen(Color.FromArgb(255, 214, 220, 228), 1.4f);
            g.DrawLine(stand, 8, 11, 8, 13);
            g.DrawLine(stand, 5, 14, 11, 14);
        }

        var handle = bitmap.GetHicon();
        try
        {
            // Icon.FromHandle no copia el handle; hay que clonarlo antes de que
            // DestroyIcon lo invalide, o el icono de bandeja queda roto.
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool DestroyIcon(nint handle);
    }
}
