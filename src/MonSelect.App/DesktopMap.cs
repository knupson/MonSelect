using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using Panel = System.Windows.Controls.Panel;
using FontFamily = System.Windows.Media.FontFamily;

namespace MonSelect.App;

/// <summary>
/// Dibuja, a escala, todos los monitores conectados en su posición real
/// (incluidos orígenes negativos y el monitor vertical) y, sobre ellos, cada
/// ventana abierta. Es la superficie principal de la pestaña "Ventanas
/// abiertas": responde de un vistazo la pregunta "¿por qué no le está pasando
/// nada a esta ventana?" — una ventana con regla se dibuja en el color de
/// acento con el nombre de la regla; una sin regla, sólo en contorno.
/// </summary>
internal static class DesktopMap
{
    private const double Padding = 18;
    private const double LabelFontSize = 10.5;

    private readonly record struct Box(double X, double Y, double W, double H)
    {
        public double Area => Math.Max(0, W) * Math.Max(0, H);
    }

    public static void Render(
        Canvas canvas,
        IReadOnlyList<MonitorInfo> monitors,
        IReadOnlyList<OpenWindowRow> windows,
        RuleSet ruleSet,
        nint? selectedHandle,
        Action<nint> onWindowClicked)
    {
        canvas.Children.Clear();

        var width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 900;
        var height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 420;

        if (monitors.Count == 0)
        {
            AddText(canvas, "Sin monitores detectados.", Tokens.Muted, 12, 8, 8, Tokens.UiFont);
            return;
        }

        var minX = monitors.Min(m => m.Bounds.Left);
        var minY = monitors.Min(m => m.Bounds.Top);
        var maxX = monitors.Max(m => m.Bounds.Right);
        var maxY = monitors.Max(m => m.Bounds.Bottom);

        var totalW = Math.Max(1, maxX - minX);
        var totalH = Math.Max(1, maxY - minY);

        var scale = Math.Min(
            (width - Padding * 2) / totalW,
            (height - Padding * 2) / totalH);
        scale = scale is > 0 and < double.PositiveInfinity ? scale : 0.05;

        var usedW = totalW * scale;
        var usedH = totalH * scale;
        var offsetX = Padding + Math.Max(0, (width - Padding * 2 - usedW) / 2);
        var offsetY = Padding + Math.Max(0, (height - Padding * 2 - usedH) / 2);

        Point ToCanvas(int x, int y) => new(offsetX + (x - minX) * scale, offsetY + (y - minY) * scale);

        foreach (var monitor in monitors)
            DrawMonitorRect(canvas, monitor, ToCanvas);

        // Pass 1: decidir qué ventana se gana la etiqueta. La seleccionada
        // siempre la tiene; entre el resto, la más chica reclama primero — es
        // la más probable de ser la que el usuario quiere identificar — y
        // cualquiera cuyo rótulo caería sobre uno ya colocado se queda sin
        // texto (pero conserva su rectángulo). Sin esto, dos ventanas casi
        // superpuestas en el mismo monitor dejan una pila de texto ilegible.
        var geometry = windows.ToDictionary(w => w.Handle, w => WindowBox(w, ToCanvas, scale));
        var labelled = new HashSet<nint>();
        var placedLabels = new List<Box>();

        foreach (var window in windows
                     .OrderBy(w => w.Handle == selectedHandle ? 0 : 1)
                     .ThenBy(w => geometry[w.Handle].Area))
        {
            var box = geometry[window.Handle];
            if (box.W <= 34 || box.H <= 14)
                continue;

            var text = window.MatchedRule.Length > 0 ? window.MatchedRule : window.Title;
            var labelBox = new Box(box.X + 3, box.Y + 2, Math.Min(box.W - 6, MeasureWidth(text)), 14);

            if (OverlapsAny(placedLabels, labelBox))
                continue;

            labelled.Add(window.Handle);
            placedLabels.Add(labelBox);
        }

        // Pass 2: dibujar. Las ventanas más grandes van primero (quedan atrás);
        // la seleccionada y las más chicas van al final, así quedan arriba del
        // todo tanto visualmente como para recibir el click.
        foreach (var window in windows
                     .OrderByDescending(w => geometry[w.Handle].Area)
                     .ThenBy(w => w.Handle == selectedHandle ? 1 : 0))
        {
            DrawWindow(
                canvas, window, geometry[window.Handle], selectedHandle == window.Handle,
                labelled.Contains(window.Handle), onWindowClicked);
        }

        // Los chips de identidad de monitor van al final, opacos y por encima
        // de todo: así nunca quedan tapados por una ventana, y las etiquetas de
        // ventana (paso 1) ya los evitó por completo — nunca comparten espacio.
        foreach (var monitor in monitors)
            DrawMonitorChip(canvas, monitor, ruleSet, ToCanvas);
    }

    private static Box WindowBox(OpenWindowRow window, Func<int, int, Point> toCanvas, double scale)
    {
        var topLeft = toCanvas(window.VisibleBounds.Left, window.VisibleBounds.Top);
        return new Box(
            topLeft.X, topLeft.Y,
            Math.Max(2, window.VisibleBounds.Width * scale),
            Math.Max(2, window.VisibleBounds.Height * scale));
    }

    private static void DrawMonitorRect(Canvas canvas, MonitorInfo monitor, Func<int, int, Point> toCanvas)
    {
        var topLeft = toCanvas(monitor.Bounds.Left, monitor.Bounds.Top);
        // El ancho/alto salen de restar dos puntos ya transformados, no de
        // re-derivar la escala acá: evita un redondeo doble frente al resto
        // del dibujo, que usa la misma toCanvas para todo.
        var bottomRight = toCanvas(monitor.Bounds.Right, monitor.Bounds.Bottom);

        var rect = new Rectangle
        {
            Width = Math.Max(1, bottomRight.X - topLeft.X),
            Height = Math.Max(1, bottomRight.Y - topLeft.Y),
            Fill = Tokens.Panel,
            Stroke = Tokens.Line,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(rect, topLeft.X);
        Canvas.SetTop(rect, topLeft.Y);
        Panel.SetZIndex(rect, 0);
        canvas.Children.Add(rect);
    }

    /// <summary>
    /// Alias + resolución del monitor, en un chip opaco anclado a la esquina
    /// superior derecha. Fijo a propósito (spec de diseño): la identidad del
    /// monitor no puede competir por espacio con los rótulos de ventana, que
    /// se anclan arriba a la izquierda de cada ventana.
    /// </summary>
    private static void DrawMonitorChip(Canvas canvas, MonitorInfo monitor, RuleSet ruleSet, Func<int, int, Point> toCanvas)
    {
        var topRight = toCanvas(monitor.Bounds.Right, monitor.Bounds.Top);

        var alias = ruleSet.AliasFor(monitor.Id) ?? monitor.GdiName;
        var text = $"{alias}  {monitor.Bounds.Width}×{monitor.Bounds.Height}"
                   + (monitor.IsPrimary ? "  ppal." : string.Empty);

        var width = MeasureWidth(text) + 12;
        var chip = new Rectangle
        {
            Width = width,
            Height = 18,
            Fill = Tokens.Panel,
            Stroke = Tokens.Line,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(chip, topRight.X - width - 4);
        Canvas.SetTop(chip, topRight.Y + 4);
        Panel.SetZIndex(chip, 20);
        canvas.Children.Add(chip);

        var label = new TextBlock
        {
            Text = text,
            Foreground = Tokens.Muted,
            FontSize = 10,
            FontFamily = new FontFamily(Tokens.MonoFont),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, topRight.X - width + 2);
        Canvas.SetTop(label, topRight.Y + 7);
        Panel.SetZIndex(label, 21);
        canvas.Children.Add(label);
    }

    private static void DrawWindow(
        Canvas canvas, OpenWindowRow window, Box box, bool selected, bool showLabel, Action<nint> onClicked)
    {
        var hasRule = !string.IsNullOrEmpty(window.MatchedRule);
        var stroke = hasRule ? Tokens.Accent : Tokens.Line;

        if (selected)
        {
            var glow = new Rectangle
            {
                Width = box.W + 6,
                Height = box.H + 6,
                Stroke = Tokens.Accent,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 },
            };
            Canvas.SetLeft(glow, box.X - 3);
            Canvas.SetTop(glow, box.Y - 3);
            Panel.SetZIndex(glow, 9);
            canvas.Children.Add(glow);
        }

        var rect = new Rectangle
        {
            Width = box.W,
            Height = box.H,
            Stroke = stroke,
            StrokeThickness = selected ? 2.5 : hasRule ? 1.6 : 1,
            Fill = hasRule ? Tokens.AccentFill : Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = window.Title,
        };
        Canvas.SetLeft(rect, box.X);
        Canvas.SetTop(rect, box.Y);
        Panel.SetZIndex(rect, 10);
        rect.MouseLeftButtonUp += (_, e) =>
        {
            onClicked(window.Handle);
            e.Handled = true;
        };
        canvas.Children.Add(rect);

        if (!showLabel)
            return;

        var text = window.MatchedRule.Length > 0 ? window.MatchedRule : window.Title;
        var label = new TextBlock
        {
            Text = text,
            Foreground = hasRule ? Tokens.Accent : Tokens.Muted,
            FontSize = LabelFontSize,
            FontFamily = new FontFamily(Tokens.UiFont),
            MaxWidth = Math.Max(0, box.W - 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, box.X + 3);
        Canvas.SetTop(label, box.Y + 2);
        Panel.SetZIndex(label, 11);
        canvas.Children.Add(label);
    }

    private static void AddText(
        Canvas canvas, string text, SolidColorBrush brush, double size, double x, double y, string font)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontSize = size,
            FontFamily = new FontFamily(font),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    /// <summary>
    /// Ancho aproximado del texto en píxeles, sin medir glyphs de verdad
    /// (FormattedText exige un contexto de DPI que acá no vale la pena pedir):
    /// alcanza para decidir colisiones de rótulos, no para layout exacto.
    /// </summary>
    private static double MeasureWidth(string text) => text.Length * LabelFontSize * 0.56;

    private static bool OverlapsAny(List<Box> placed, Box candidate)
    {
        foreach (var p in placed)
        {
            var ix = Math.Max(0, Math.Min(candidate.X + candidate.W, p.X + p.W) - Math.Max(candidate.X, p.X));
            var iy = Math.Max(0, Math.Min(candidate.Y + candidate.H, p.Y + p.H) - Math.Max(candidate.Y, p.Y));
            var overlap = ix * iy;

            if (candidate.Area > 0 && overlap / candidate.Area > 0.3)
                return true;
        }

        return false;
    }
}
