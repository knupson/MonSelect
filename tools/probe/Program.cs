using MonSelect.Core.Monitors;

var system = new Win32MonitorSystem();

Console.WriteLine("=== monitores ===");
foreach (var m in system.GetMonitors())
{
    Console.WriteLine($"{m.GdiName,-14} bounds={m.Bounds}");
    Console.WriteLine($"{"",-14} work  ={m.WorkArea}  primary={m.IsPrimary}");
    Console.WriteLine($"{"",-14} id    ={m.Id.DevicePath}");
}
