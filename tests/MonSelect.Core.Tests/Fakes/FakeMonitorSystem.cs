using MonSelect.Core.Monitors;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Tests.Fakes;

/// <summary>
/// Reproduce el layout de cuatro monitores medido en el spec, seccion 3.2.
/// Los tests que necesiten otro layout construyen esta clase con su propia lista.
/// </summary>
public sealed class FakeMonitorSystem : IMonitorSystem
{
    private readonly List<MonitorInfo> _monitors;

    public FakeMonitorSystem(IEnumerable<MonitorInfo>? monitors = null)
        => _monitors = (monitors ?? Default()).ToList();

    public static MonitorInfo Primary => new(
        new MonitorId(@"\\?\DISPLAY#RDG3150#1&aaaa&0&UID256#{guid}"),
        @"\\.\DISPLAY1",
        Rect.FromLtrb(0, 0, 1920, 1080),
        Rect.FromLtrb(0, 0, 1920, 1048),
        IsPrimary: true);

    public static MonitorInfo Above => new(
        new MonitorId(@"\\?\DISPLAY#OOO2223#1&aaaa&0&UID260#{guid}"),
        @"\\.\DISPLAY2",
        Rect.FromLtrb(0, -1080, 1920, 0),
        Rect.FromLtrb(0, -1080, 1920, -32),
        IsPrimary: false);

    public static MonitorInfo Vertical => new(
        new MonitorId(@"\\?\DISPLAY#GSM57EE#1&aaaa&0&UID264#{guid}"),
        @"\\.\DISPLAY3",
        Rect.FromLtrb(1920, -842, 3000, 1078),
        Rect.FromLtrb(1920, -842, 3000, 1046),
        IsPrimary: false);

    public static MonitorInfo Right => new(
        new MonitorId(@"\\?\DISPLAY#BNQ7820#1&aaaa&0&UID268#{guid}"),
        @"\\.\DISPLAY4",
        Rect.FromLtrb(3000, 0, 4920, 1080),
        Rect.FromLtrb(3000, 0, 4920, 1048),
        IsPrimary: false);

    public static MonitorInfo Disconnected => new(
        new MonitorId(@"\\?\DISPLAY#NOPE0000#1&aaaa&0&UID999#{guid}"),
        @"\\.\DISPLAY9",
        Rect.FromLtrb(9000, 0, 10920, 1080),
        Rect.FromLtrb(9000, 0, 10920, 1048),
        IsPrimary: false);

    private static IEnumerable<MonitorInfo> Default()
        => new[] { Primary, Above, Vertical, Right };

    public IReadOnlyList<MonitorInfo> GetMonitors() => _monitors;

    public MonitorInfo? GetMonitorForRect(Rect rect)
        => _monitors.FirstOrDefault(m =>
            rect.Left < m.Bounds.Right && rect.Right > m.Bounds.Left &&
            rect.Top < m.Bounds.Bottom && rect.Bottom > m.Bounds.Top);
}
