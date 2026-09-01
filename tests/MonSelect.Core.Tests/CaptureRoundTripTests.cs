using MonSelect.Core.Rules;
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

/// <summary>
/// F3: "capturá la ventana donde la dejaste y aplicá la regla tiene que
/// devolverla exactamente ahí" es la promesa entera de la captura guiada.
/// Esto prueba, con fakes puros, que WindowToRule (encoge por el bleed
/// medido) y WindowPlacer.Apply (lo vuelve a expandir, y encima convierte a
/// rect externo por el marco de DWM) son inversos exactos entre sí, sin
/// tocar Win32 real.
/// </summary>
public class CaptureRoundTripTests : IDisposable
{
    private const nint Hwnd = 777;

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("monselect-capture");
    private readonly FakeWindowSystem _system = new();
    private readonly StyleStore _styles;
    private readonly WindowPlacer _placer;

    public CaptureRoundTripTests()
    {
        _styles = new StyleStore(Path.Combine(_dir.FullName, "borderless.json"));
        _placer = new WindowPlacer(_system, _styles);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    [Theory]
    [InlineData(0, 0)] // sin marco DWM, sin bleed: caso trivial
    [InlineData(7, 0)] // sólo marco DWM (Win11 real, ventana sin borde propio)
    [InlineData(0, 1)] // sólo bleed (app dibuja su propio borde de 1px)
    [InlineData(7, 1)] // los dos a la vez: WhatsApp/Discord/Chrome en el escritorio real
    public void Capturing_then_applying_reproduces_the_exact_visible_pixels(int frameInset, int bleed)
    {
        var visibleAtCapture = Rect.FromLtrb(1920, -842, 3000, 102);
        _system.Add(Hwnd, Rect.FromLtrb(1920 - frameInset, -842, 3000 + frameInset, 102 + frameInset), 0x00CF0000);
        _system[Hwnd].FrameInset = frameInset;

        // La ventana está exactamente donde el usuario la dejó: GetVisibleBounds
        // tiene que devolver visibleAtCapture con este FrameInset.
        Assert.Equal(visibleAtCapture, _system.GetVisibleBounds(Hwnd));

        // 1. Capturar: WindowToRule encoge el rect por el bleed medido en ese
        // momento y lo graba en la regla, junto con ese mismo bleed.
        var window = new WindowInfo(
            Hwnd, 100, @"C:\apps\whatsapp.exe", null, "WinUIDesktopWin32WindowClass", "WhatsApp", null,
            _system.GetBounds(Hwnd), WindowState.Normal);
        var rule = WindowToRule.Convert(
            window, visibleAtCapture, "display3", "WhatsApp",
            includeCommandLine: false, includeTitle: false, bleed: bleed);

        // 2. Aplicar: PlacementCalculator no hace más que pasar el rect de la
        // regla; WindowPlacer expande por el bleed grabado y convierte al
        // rect externo con el marco DWM ACTUAL de la ventana.
        var target = new TargetPlacement(
            ShowCommand.Normal, rule.Place.Rect!.Value, StripBorders: false, ExpectedBounds: rule.Place.Rect!.Value);
        _placer.Apply(Hwnd, processId: 100, processStartTicks: 1, target, rule.Bleed ?? 0);

        // 3. La ventana tiene que haber quedado exactamente donde estaba capturada.
        Assert.Equal(visibleAtCapture, _system.GetVisibleBounds(Hwnd));
    }
}
