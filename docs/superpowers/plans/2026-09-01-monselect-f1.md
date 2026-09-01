# MonSelect F1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un servicio de bandeja que, al aparecer una ventana en Windows 11, la coloca en el monitor y con el estado que dicte la primera regla que matchee en `rules.yaml`.

**Architecture:** `MonSelect.Core` contiene toda la lógica y no llama a Win32 directamente: accede a través de `IWindowSystem` e `IMonitorSystem`, cuyas implementaciones reales viven en el mismo proyecto pero se sustituyen por fakes en los tests. Un único hilo con message pump es dueño del hook y de todas las mutaciones de ventanas; la app WPF de bandeja se comunica con él por cola.

**Tech Stack:** C# 13 / .NET 10 (`net10.0-windows`), xunit, YamlDotNet, WPF + WinForms `NotifyIcon` para la bandeja.

**Spec:** `docs/superpowers/specs/2026-09-01-monselect-design.md`

## Global Constraints

- Target framework de todos los proyectos: `net10.0-windows`.
- `<Nullable>enable</Nullable>` y `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` en todos los proyectos.
- DPI awareness `PerMonitorV2`, declarada en el manifest de la app. Todo cálculo de coordenadas asume píxeles físicos.
- `MonSelect.Core` no referencia WPF ni WinForms. Si un tipo de UI aparece en `Core`, es un error de diseño.
- Precedencia de reglas: gana la primera que matchea en el orden del archivo. Sin scoring.
- Presupuesto de retry por defecto: `[0, 150, 400, 800]` ms.
- Config en `%APPDATA%\MonSelect\rules.yaml`. Los tests nunca tocan esa ruta: usan un directorio temporal.
- Estados de ventana: `Normal`, `Maximized`, `Minimized`, `Borderless`.
- Políticas `if_missing`: `Skip` (default), `Primary`, `Nearest`.
- Modos `apply`: `All` (default), `First`, `Rotate`.
- Identidad de monitor: `monitorDevicePath` de `QueryDisplayConfig`. Nunca el índice `\\.\DISPLAYn` ni el serial EDID.
- Commits en inglés, formato Conventional Commits.

---

## File Structure

**`src/MonSelect.Core/`** — sin dependencias de UI

| Archivo | Responsabilidad |
|---|---|
| `Win32/NativeTypes.cs` | `RECT`, `POINT`, `WINDOWPLACEMENT`, `MONITORINFOEX`, enums `WS`, `SW`, `SWP`, `GWL` |
| `Win32/NativeMethods.cs` | P/Invoke a user32, shcore, ntdll, kernel32 |
| `Win32/DisplayConfig.cs` | Structs y P/Invoke de `QueryDisplayConfig` |
| `Monitors/MonitorId.cs` | Clave estable de monitor (device path) |
| `Monitors/MonitorInfo.cs` | Geometría y metadatos de un monitor |
| `Monitors/IMonitorSystem.cs` | Frontera testeable hacia el subsistema de display |
| `Monitors/Win32MonitorSystem.cs` | Implementación real |
| `Monitors/MonitorRegistry.cs` | Resolución alias → monitor, políticas `if_missing` |
| `Windows/WindowState.cs` | Enum de los cuatro estados |
| `Windows/WindowInfo.cs` | Snapshot inmutable de una ventana |
| `Windows/IWindowSystem.cs` | Frontera testeable hacia las ventanas |
| `Windows/Win32WindowSystem.cs` | Implementación real |
| `Windows/WindowProbe.cs` | Construye `WindowInfo` desde un hwnd |
| `Windows/PlacementCalculator.cs` | **Puro.** Dado monitor + estado, calcula el rect y el `showCmd` objetivo |
| `Windows/WindowPlacer.cs` | Aplica un `Placement` a una ventana |
| `Windows/StyleStore.cs` | Persiste el style original para revertir `Borderless` |
| `Rules/MatchCriteria.cs` | Criterios de matcheo |
| `Rules/Placement.cs` | Destino: monitor(es), estado, rect opcional |
| `Rules/Rule.cs` | Regla completa |
| `Rules/RuleSet.cs` | Documento de config: monitores, defaults, reglas |
| `Rules/RuleMatcher.cs` | **Puro.** Primera regla que matchea |
| `Rules/YamlStore.cs` | Carga y guardado de `rules.yaml` |
| `Engine/RetryScheduler.cs` | Presupuesto de reintentos con reloj inyectable |
| `Engine/WindowWatcher.cs` | Hook y message pump |
| `Engine/RuleEngine.cs` | Orquesta probe → match → place → retry |
| `Engine/ApplyLog.cs` | Registro estructurado de cada aplicación |

**`src/MonSelect.App/`** — bandeja

| Archivo | Responsabilidad |
|---|---|
| `App.xaml` / `App.xaml.cs` | Arranque, sin ventana principal |
| `TrayHost.cs` | Icono de bandeja y menú |
| `Bootstrap.cs` | Composición: crea config si falta, arma el motor |
| `DiagnoseMode.cs` | Modo `--diagnose` |
| `app.manifest` | DPI `PerMonitorV2` |

**`tests/MonSelect.Core.Tests/`** — un archivo de test por unidad, más `Fakes/FakeMonitorSystem.cs` y `Fakes/FakeWindowSystem.cs`.

---

## Task 1: Solution scaffolding

**Files:**
- Create: `MonSelect.sln`
- Create: `Directory.Build.props`
- Create: `src/MonSelect.Core/MonSelect.Core.csproj`
- Create: `src/MonSelect.Core/Windows/WindowState.cs`
- Test: `tests/MonSelect.Core.Tests/MonSelect.Core.Tests.csproj`
- Test: `tests/MonSelect.Core.Tests/WindowStateTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces: `MonSelect.Core.Windows.WindowState` — enum `{ Normal, Maximized, Minimized, Borderless }`. La solución compila y `dotnet test` corre en verde.

- [ ] **Step 1: Crear la solución y los proyectos**

```bash
cd E:/Claude/MonSelect
dotnet new sln -n MonSelect
dotnet new classlib -n MonSelect.Core -o src/MonSelect.Core -f net10.0
dotnet new xunit -n MonSelect.Core.Tests -o tests/MonSelect.Core.Tests -f net10.0
dotnet sln add src/MonSelect.Core/MonSelect.Core.csproj
dotnet sln add tests/MonSelect.Core.Tests/MonSelect.Core.Tests.csproj
dotnet add tests/MonSelect.Core.Tests/MonSelect.Core.Tests.csproj reference src/MonSelect.Core/MonSelect.Core.csproj
rm src/MonSelect.Core/Class1.cs
rm tests/MonSelect.Core.Tests/UnitTest1.cs
```

- [ ] **Step 2: Crear `Directory.Build.props` con las constraints globales**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
</Project>
```

`TargetFramework` acá pisa el `net10.0` que puso `dotnet new`. Borrar la línea `<TargetFramework>` de cada `.csproj` individual para que no haya dos fuentes de verdad.

- [ ] **Step 3: Escribir el test que falla**

`tests/MonSelect.Core.Tests/WindowStateTests.cs`:

```csharp
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class WindowStateTests
{
    [Fact]
    public void Defines_the_four_states_from_the_spec()
    {
        Assert.Equal(
            new[] { "Normal", "Maximized", "Minimized", "Borderless" },
            Enum.GetNames<WindowState>());
    }
}
```

- [ ] **Step 4: Correr el test y verificar que falla**

Run: `dotnet test`
Expected: FAIL — `The type or namespace name 'WindowState' could not be found`.

- [ ] **Step 5: Implementar el enum**

`src/MonSelect.Core/Windows/WindowState.cs`:

```csharp
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
```

- [ ] **Step 6: Correr el test y verificar que pasa**

Run: `dotnet test`
Expected: PASS, 1 test.

- [ ] **Step 7: Commit**

```bash
git add MonSelect.sln Directory.Build.props src tests
git commit -m "chore: scaffold solution with Core and test projects"
```

---

## Task 2: Tipos y constantes Win32

**Files:**
- Create: `src/MonSelect.Core/Win32/NativeTypes.cs`
- Test: `tests/MonSelect.Core.Tests/NativeTypesTests.cs`

**Interfaces:**
- Consumes: nada.
- Produces:
  - `readonly record struct Rect(int Left, int Top, int Right, int Bottom)` con `Width`, `Height`, `IsEmpty`, y `static Rect FromLtrb(int, int, int, int)`.
  - `[Flags] enum WindowStyles : uint` con `Caption = 0x00C00000`, `ThickFrame = 0x00040000`, `Maximize = 0x01000000`, `Minimize = 0x20000000`, `Visible = 0x10000000`, `Popup = 0x80000000`, `Child = 0x40000000`.
  - `enum ShowCommand { Hide = 0, Normal = 1, Minimized = 2, Maximized = 3, Restore = 9 }`.
  - `[Flags] enum SetWindowPosFlags : uint` con `NoSize = 0x0001`, `NoMove = 0x0002`, `NoZOrder = 0x0004`, `NoActivate = 0x0010`, `FrameChanged = 0x0020`.
  - `static class StyleMath` con `uint StripBorders(uint style)` y `bool IsBorderless(uint style)`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/NativeTypesTests.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Tests;

public class NativeTypesTests
{
    [Fact]
    public void Rect_computes_width_and_height()
    {
        var r = Rect.FromLtrb(3000, 0, 4920, 1080);
        Assert.Equal(1920, r.Width);
        Assert.Equal(1080, r.Height);
        Assert.False(r.IsEmpty);
    }

    [Fact]
    public void Rect_with_zero_area_is_empty()
    {
        Assert.True(Rect.FromLtrb(10, 10, 10, 400).IsEmpty);
        Assert.True(Rect.FromLtrb(10, 10, 400, 10).IsEmpty);
    }

    // El style medido en la ventana de RustDesk en el spec, seccion 3.3.
    private const uint RustDeskBorderless = 0x150B0000;

    [Fact]
    public void Recognises_the_measured_borderless_style()
    {
        Assert.True(StyleMath.IsBorderless(RustDeskBorderless));
    }

    [Fact]
    public void A_normal_overlapped_window_is_not_borderless()
    {
        uint overlapped = (uint)(WindowStyles.Visible
            | WindowStyles.Caption
            | WindowStyles.ThickFrame);
        Assert.False(StyleMath.IsBorderless(overlapped));
    }

    [Fact]
    public void Stripping_borders_produces_a_borderless_style_and_keeps_the_rest()
    {
        uint overlapped = (uint)(WindowStyles.Visible
            | WindowStyles.Caption
            | WindowStyles.ThickFrame);

        uint stripped = StyleMath.StripBorders(overlapped);

        Assert.True(StyleMath.IsBorderless(stripped));
        Assert.Equal((uint)WindowStyles.Visible, stripped & (uint)WindowStyles.Visible);
    }

    [Fact]
    public void Stripping_borders_twice_changes_nothing_further()
    {
        uint once = StyleMath.StripBorders(0x00CF0000);
        Assert.Equal(once, StyleMath.StripBorders(once));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test --filter NativeTypesTests`
Expected: FAIL — `Rect`, `WindowStyles` y `StyleMath` no existen.

- [ ] **Step 3: Implementar los tipos**

`src/MonSelect.Core/Win32/NativeTypes.cs`:

```csharp
using System.Runtime.InteropServices;

namespace MonSelect.Core.Win32;

/// <summary>Rectángulo en coordenadas de pantalla, layout compatible con RECT de Win32.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect FromLtrb(int left, int top, int right, int bottom)
        => new(left, top, right, bottom);

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Point(int X, int Y);

[Flags]
public enum WindowStyles : uint
{
    Popup = 0x80000000,
    Child = 0x40000000,
    Minimize = 0x20000000,
    Visible = 0x10000000,
    ClipSiblings = 0x04000000,
    Maximize = 0x01000000,
    Caption = 0x00C00000,
    Border = 0x00800000,
    DlgFrame = 0x00400000,
    SysMenu = 0x00080000,
    ThickFrame = 0x00040000,
    MinimizeBox = 0x00020000,
    MaximizeBox = 0x00010000,
}

public enum ShowCommand
{
    Hide = 0,
    Normal = 1,
    Minimized = 2,
    Maximized = 3,
    Restore = 9,
}

[Flags]
public enum SetWindowPosFlags : uint
{
    NoSize = 0x0001,
    NoMove = 0x0002,
    NoZOrder = 0x0004,
    NoActivate = 0x0010,
    FrameChanged = 0x0020,
}

public static class GwlIndex
{
    public const int Style = -16;
    public const int ExStyle = -20;
}

/// <summary>
/// Operaciones sobre el style de una ventana. Se aísla acá para poder testearla
/// sin ventanas reales: es la parte del borderless que más fácil se rompe.
/// </summary>
public static class StyleMath
{
    private const uint BorderBits = (uint)(WindowStyles.Caption | WindowStyles.ThickFrame);

    /// <summary>Quita caption y thickframe, dejando el resto del style intacto.</summary>
    public static uint StripBorders(uint style) => style & ~BorderBits;

    /// <summary>True si la ventana no tiene ni caption ni thickframe.</summary>
    public static bool IsBorderless(uint style) => (style & BorderBits) == 0;
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test --filter NativeTypesTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/MonSelect.Core/Win32/NativeTypes.cs tests/MonSelect.Core.Tests/NativeTypesTests.cs
git commit -m "feat: add Win32 value types and style arithmetic"
```

---

## Task 3: Modelo de monitor y registry

**Files:**
- Create: `src/MonSelect.Core/Monitors/MonitorId.cs`
- Create: `src/MonSelect.Core/Monitors/MonitorInfo.cs`
- Create: `src/MonSelect.Core/Monitors/IMonitorSystem.cs`
- Create: `src/MonSelect.Core/Monitors/MonitorRegistry.cs`
- Test: `tests/MonSelect.Core.Tests/Fakes/FakeMonitorSystem.cs`
- Test: `tests/MonSelect.Core.Tests/MonitorRegistryTests.cs`

**Interfaces:**
- Consumes: `Rect` de la Task 2.
- Produces:
  - `readonly record struct MonitorId(string DevicePath)` con comparación case-insensitive.
  - `sealed record MonitorInfo(MonitorId Id, string GdiName, Rect Bounds, Rect WorkArea, bool IsPrimary)`.
  - `interface IMonitorSystem { IReadOnlyList<MonitorInfo> GetMonitors(); MonitorInfo? GetMonitorForRect(Rect rect); }`
  - `enum IfMissing { Skip, Primary, Nearest }`
  - `sealed class MonitorRegistry(IMonitorSystem system)` con `MonitorInfo? Resolve(MonitorId id, IfMissing policy, Rect fallbackAnchor)` y `MonitorInfo? Primary()`.

- [ ] **Step 1: Escribir el fake**

`tests/MonSelect.Core.Tests/Fakes/FakeMonitorSystem.cs`:

```csharp
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
```

- [ ] **Step 2: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/MonitorRegistryTests.cs`:

```csharp
using MonSelect.Core.Monitors;
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Tests;

public class MonitorRegistryTests
{
    private static MonitorRegistry NewRegistry() => new(new FakeMonitorSystem());

    [Fact]
    public void Resolves_a_connected_monitor_by_device_path()
    {
        var found = NewRegistry().Resolve(
            FakeMonitorSystem.Right.Id, IfMissing.Skip, Rect.FromLtrb(0, 0, 100, 100));

        Assert.NotNull(found);
        Assert.Equal(@"\\.\DISPLAY4", found!.GdiName);
    }

    [Fact]
    public void Device_paths_compare_case_insensitively()
    {
        var upper = new MonitorId(FakeMonitorSystem.Right.Id.DevicePath.ToUpperInvariant());

        Assert.NotNull(NewRegistry().Resolve(upper, IfMissing.Skip, default));
    }

    [Fact]
    public void Skip_returns_null_when_the_monitor_is_absent()
    {
        var found = NewRegistry().Resolve(
            FakeMonitorSystem.Disconnected.Id, IfMissing.Skip, Rect.FromLtrb(0, 0, 100, 100));

        Assert.Null(found);
    }

    [Fact]
    public void Primary_policy_falls_back_to_the_primary_monitor()
    {
        var found = NewRegistry().Resolve(
            FakeMonitorSystem.Disconnected.Id, IfMissing.Primary, Rect.FromLtrb(0, 0, 100, 100));

        Assert.NotNull(found);
        Assert.True(found!.IsPrimary);
    }

    [Fact]
    public void Nearest_policy_picks_the_monitor_closest_to_the_anchor()
    {
        // Ancla dentro de DISPLAY4, a la derecha del todo.
        var anchor = Rect.FromLtrb(4000, 400, 4400, 700);

        var found = NewRegistry().Resolve(FakeMonitorSystem.Disconnected.Id, IfMissing.Nearest, anchor);

        Assert.NotNull(found);
        Assert.Equal(@"\\.\DISPLAY4", found!.GdiName);
    }

    [Fact]
    public void Nearest_policy_handles_anchors_in_negative_coordinate_space()
    {
        // Ancla dentro de DISPLAY2, que vive enteramente en Y negativo.
        var anchor = Rect.FromLtrb(200, -900, 600, -600);

        var found = NewRegistry().Resolve(FakeMonitorSystem.Disconnected.Id, IfMissing.Nearest, anchor);

        Assert.NotNull(found);
        Assert.Equal(@"\\.\DISPLAY2", found!.GdiName);
    }

    [Fact]
    public void Primary_returns_the_flagged_monitor()
    {
        Assert.Equal(@"\\.\DISPLAY1", NewRegistry().Primary()!.GdiName);
    }

    [Fact]
    public void Primary_returns_null_when_no_monitor_is_flagged()
    {
        var registry = new MonitorRegistry(
            new FakeMonitorSystem(new[] { FakeMonitorSystem.Right }));

        Assert.Null(registry.Primary());
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test --filter MonitorRegistryTests`
Expected: FAIL — `MonitorRegistry` no existe.

- [ ] **Step 4: Implementar el modelo**

`src/MonSelect.Core/Monitors/MonitorId.cs`:

```csharp
namespace MonSelect.Core.Monitors;

/// <summary>
/// Clave estable de un monitor: el monitorDevicePath que devuelve QueryDisplayConfig.
/// No se usa el índice \\.\DISPLAYn porque se reasigna al reconectar, ni el serial
/// EDID porque en hardware real aparece duplicado o en cero (spec, sección 3.2).
/// </summary>
public readonly record struct MonitorId(string DevicePath)
{
    public bool Equals(MonitorId other)
        => string.Equals(DevicePath, other.DevicePath, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode()
        => StringComparer.OrdinalIgnoreCase.GetHashCode(DevicePath);

    public override string ToString() => DevicePath;
}
```

`src/MonSelect.Core/Monitors/MonitorInfo.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <param name="Bounds">Rect completo del monitor. Es el destino de Borderless.</param>
/// <param name="WorkArea">Rect sin taskbar ni appbars. Es el destino de Maximized.</param>
public sealed record MonitorInfo(
    MonitorId Id,
    string GdiName,
    Rect Bounds,
    Rect WorkArea,
    bool IsPrimary);
```

`src/MonSelect.Core/Monitors/IMonitorSystem.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <summary>Frontera hacia el subsistema de display. Los tests la sustituyen.</summary>
public interface IMonitorSystem
{
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Monitor que contiene el rect, o null si no lo toca ninguno.</summary>
    MonitorInfo? GetMonitorForRect(Rect rect);
}
```

`src/MonSelect.Core/Monitors/MonitorRegistry.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <summary>Qué hacer cuando el monitor que pide una regla no está conectado.</summary>
public enum IfMissing
{
    /// <summary>No aplicar la regla. Nunca colocar en un monitor equivocado.</summary>
    Skip,

    /// <summary>Caer al monitor principal.</summary>
    Primary,

    /// <summary>Caer al monitor más cercano a donde la ventana ya estaba.</summary>
    Nearest,
}

public sealed class MonitorRegistry(IMonitorSystem system)
{
    public IReadOnlyList<MonitorInfo> Monitors => system.GetMonitors();

    public MonitorInfo? Primary()
        => system.GetMonitors().FirstOrDefault(m => m.IsPrimary);

    /// <param name="fallbackAnchor">
    /// Posición actual de la ventana, usada sólo por <see cref="IfMissing.Nearest"/>.
    /// </param>
    public MonitorInfo? Resolve(MonitorId id, IfMissing policy, Rect fallbackAnchor)
    {
        var monitors = system.GetMonitors();

        var exact = monitors.FirstOrDefault(m => m.Id == id);
        if (exact is not null)
            return exact;

        return policy switch
        {
            IfMissing.Skip => null,
            IfMissing.Primary => Primary(),
            IfMissing.Nearest => Nearest(monitors, fallbackAnchor),
            _ => null,
        };
    }

    private static MonitorInfo? Nearest(IReadOnlyList<MonitorInfo> monitors, Rect anchor)
    {
        if (monitors.Count == 0)
            return null;

        var ax = anchor.Left + anchor.Width / 2.0;
        var ay = anchor.Top + anchor.Height / 2.0;

        return monitors
            .OrderBy(m =>
            {
                var mx = m.Bounds.Left + m.Bounds.Width / 2.0;
                var my = m.Bounds.Top + m.Bounds.Height / 2.0;
                var dx = mx - ax;
                var dy = my - ay;
                return dx * dx + dy * dy;
            })
            .First();
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test --filter MonitorRegistryTests`
Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MonSelect.Core/Monitors tests/MonSelect.Core.Tests
git commit -m "feat: add monitor model and registry with if-missing policies"
```

---

## Task 4: Implementación real de IMonitorSystem

**Files:**
- Create: `src/MonSelect.Core/Win32/NativeMethods.cs`
- Create: `src/MonSelect.Core/Win32/DisplayConfig.cs`
- Create: `src/MonSelect.Core/Monitors/Win32MonitorSystem.cs`
- Create: `tools/probe/Probe.csproj`
- Create: `tools/probe/Program.cs`

**Interfaces:**
- Consumes: `MonitorInfo`, `MonitorId`, `IMonitorSystem`, `Rect`.
- Produces: `sealed class Win32MonitorSystem : IMonitorSystem`. Y un ejecutable `tools/probe` que imprime lo que ve el sistema real — es la herramienta de verificación manual que se reusa en la Task 7.

**Nota sobre testing:** esta task no lleva tests unitarios. Enumerar monitores reales no se puede fakear sin reimplementar Windows; el valor está en que la salida del probe coincida con la medición del spec. La verificación es manual y está en el Step 5.

- [ ] **Step 1: Escribir los P/Invoke base**

`src/MonSelect.Core/Win32/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;

namespace MonSelect.Core.Win32;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MonitorInfoEx
{
    public int cbSize;
    public Rect rcMonitor;
    public Rect rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;

    public static MonitorInfoEx Create() => new()
    {
        cbSize = Marshal.SizeOf<MonitorInfoEx>(),
        szDevice = string.Empty,
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowPlacement
{
    public int length;
    public int flags;
    public int showCmd;
    public Point ptMinPosition;
    public Point ptMaxPosition;
    public Rect rcNormalPosition;

    public static WindowPlacement Create() => new()
    {
        length = Marshal.SizeOf<WindowPlacement>(),
    };
}

internal static class NativeMethods
{
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref Rect rect, nint data);

    [DllImport("user32.dll")]
    internal static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPlacement(nint hwnd, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int cmd);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(
        nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(nint hwnd, [Out] char[] buffer, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(nint hwnd, [Out] char[] buffer, int max);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLengthW(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
```

- [ ] **Step 2: Escribir el binding de QueryDisplayConfig**

`src/MonSelect.Core/Win32/DisplayConfig.cs`:

```csharp
using System.Runtime.InteropServices;

namespace MonSelect.Core.Win32;

// Sólo se declaran los campos que MonSelect usa; el resto se rellena como
// padding con el tamaño correcto para que el marshalling no se corra.

[StructLayout(LayoutKind.Sequential)]
internal struct Luid
{
    public uint LowPart;
    public int HighPart;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathSourceInfo
{
    public Luid adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathTargetInfo
{
    public Luid adapterId;
    public uint id;
    public uint modeInfoIdx;
    public uint outputTechnology;
    public uint rotation;
    public uint scaling;
    public uint refreshNumerator;
    public uint refreshDenominator;
    public uint scanLineOrdering;
    [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
    public uint statusFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigPathInfo
{
    public DisplayConfigPathSourceInfo sourceInfo;
    public DisplayConfigPathTargetInfo targetInfo;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigModeInfo
{
    public uint infoType;
    public uint id;
    public Luid adapterId;
    // Union de 64 bytes: targetMode / sourceMode / desktopImageInfo.
    // MonSelect no la lee, sólo necesita el tamaño correcto.
    public ulong union0;
    public ulong union1;
    public ulong union2;
    public ulong union3;
    public ulong union4;
    public ulong union5;
    public ulong union6;
    public ulong union7;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DisplayConfigDeviceInfoHeader
{
    public uint type;
    public uint size;
    public Luid adapterId;
    public uint id;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigTargetDeviceName
{
    public DisplayConfigDeviceInfoHeader header;
    public uint flags;
    public uint outputTechnology;
    public ushort edidManufactureId;
    public ushort edidProductCodeId;
    public uint connectorInstance;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string monitorFriendlyDeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string monitorDevicePath;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DisplayConfigSourceDeviceName
{
    public DisplayConfigDeviceInfoHeader header;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string viewGdiDeviceName;
}

internal static class DisplayConfigNative
{
    internal const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    internal const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
    internal const uint DEVICE_INFO_GET_TARGET_NAME = 2;
    internal const int ERROR_SUCCESS = 0;

    [DllImport("user32.dll")]
    internal static extern int GetDisplayConfigBufferSizes(
        uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    internal static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName info);

    [DllImport("user32.dll")]
    internal static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName info);
}
```

- [ ] **Step 3: Implementar `Win32MonitorSystem`**

`src/MonSelect.Core/Monitors/Win32MonitorSystem.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Monitors;

/// <summary>
/// Enumera monitores con EnumDisplayMonitors y les asigna identidad estable
/// cruzando el nombre GDI (\\.\DISPLAYn) contra los device paths que devuelve
/// QueryDisplayConfig.
/// </summary>
public sealed class Win32MonitorSystem : IMonitorSystem
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var pathsByGdiName = BuildDevicePathMap();
        var result = new List<MonitorInfo>();

        bool Callback(nint hMonitor, nint hdc, ref Rect rect, nint data)
        {
            var info = MonitorInfoEx.Create();
            if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
                return true;

            var gdiName = info.szDevice;
            var devicePath = pathsByGdiName.TryGetValue(gdiName, out var p)
                ? p
                // Sin device path la identidad no es estable, pero es mejor
                // degradar al nombre GDI que descartar el monitor entero.
                : gdiName;

            result.Add(new MonitorInfo(
                new MonitorId(devicePath),
                gdiName,
                info.rcMonitor,
                info.rcWork,
                (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));

            return true;
        }

        NativeMethods.EnumDisplayMonitors(0, 0, Callback, 0);
        return result;
    }

    public MonitorInfo? GetMonitorForRect(Rect rect)
    {
        var handleRect = rect;
        var handle = NativeMethods.MonitorFromRect(
            ref handleRect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (handle == 0)
            return null;

        var info = MonitorInfoEx.Create();
        if (!NativeMethods.GetMonitorInfoW(handle, ref info))
            return null;

        return GetMonitors().FirstOrDefault(m => m.GdiName == info.szDevice);
    }

    /// <summary>Mapea \\.\DISPLAYn al monitorDevicePath estable de cada salida activa.</summary>
    private static Dictionary<string, string> BuildDevicePathMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (DisplayConfigNative.GetDisplayConfigBufferSizes(
                DisplayConfigNative.QDC_ONLY_ACTIVE_PATHS,
                out var pathCount, out var modeCount) != DisplayConfigNative.ERROR_SUCCESS)
            return map;

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];

        if (DisplayConfigNative.QueryDisplayConfig(
                DisplayConfigNative.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount, paths, ref modeCount, modes, 0)
            != DisplayConfigNative.ERROR_SUCCESS)
            return map;

        for (var i = 0; i < pathCount; i++)
        {
            var source = new DisplayConfigSourceDeviceName
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DisplayConfigNative.DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal
                        .SizeOf<DisplayConfigSourceDeviceName>(),
                    adapterId = paths[i].sourceInfo.adapterId,
                    id = paths[i].sourceInfo.id,
                },
                viewGdiDeviceName = string.Empty,
            };

            var target = new DisplayConfigTargetDeviceName
            {
                header = new DisplayConfigDeviceInfoHeader
                {
                    type = DisplayConfigNative.DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)System.Runtime.InteropServices.Marshal
                        .SizeOf<DisplayConfigTargetDeviceName>(),
                    adapterId = paths[i].targetInfo.adapterId,
                    id = paths[i].targetInfo.id,
                },
                monitorFriendlyDeviceName = string.Empty,
                monitorDevicePath = string.Empty,
            };

            if (DisplayConfigNative.DisplayConfigGetDeviceInfo(ref source) != DisplayConfigNative.ERROR_SUCCESS)
                continue;
            if (DisplayConfigNative.DisplayConfigGetDeviceInfo(ref target) != DisplayConfigNative.ERROR_SUCCESS)
                continue;

            map[source.viewGdiDeviceName] = target.monitorDevicePath;
        }

        return map;
    }
}
```

- [ ] **Step 4: Crear el probe**

```bash
dotnet new console -n Probe -o tools/probe
dotnet sln add tools/probe/Probe.csproj
dotnet add tools/probe/Probe.csproj reference src/MonSelect.Core/MonSelect.Core.csproj
```

`tools/probe/Program.cs`:

```csharp
using MonSelect.Core.Monitors;

var system = new Win32MonitorSystem();

Console.WriteLine("=== monitores ===");
foreach (var m in system.GetMonitors())
{
    Console.WriteLine($"{m.GdiName,-14} bounds={m.Bounds}");
    Console.WriteLine($"{"",-14} work  ={m.WorkArea}  primary={m.IsPrimary}");
    Console.WriteLine($"{"",-14} id    ={m.Id.DevicePath}");
}
```

- [ ] **Step 5: Verificar contra la medición del spec**

Run: `dotnet run --project tools/probe`

Comprobar, contra la sección 3.2 del spec:

1. Aparecen cuatro monitores.
2. Los `bounds` coinciden: `(0,0)-(1920,1080)`, `(0,-1080)-(1920,0)`, `(1920,-842)-(3000,1078)`, `(3000,0)-(4920,1080)`.
3. Las `work` areas terminan 32 px antes en los monitores con taskbar.
4. Exactamente uno tiene `primary=True`.
5. **Cada `id` es un device path que empieza con `\\?\DISPLAY#`, no un `\\.\DISPLAYn`.** Si algún id cayó al nombre GDI, el mapeo de `QueryDisplayConfig` falló y hay que arreglarlo antes de seguir: toda la identidad de monitor depende de esto.
6. Los cuatro ids son distintos entre sí.

- [ ] **Step 6: Commit**

```bash
git add src/MonSelect.Core/Win32 src/MonSelect.Core/Monitors/Win32MonitorSystem.cs tools MonSelect.sln
git commit -m "feat: enumerate monitors with stable device-path identity"
```

---

## Task 5: Modelo de reglas y carga de YAML

**Files:**
- Create: `src/MonSelect.Core/Rules/MatchCriteria.cs`
- Create: `src/MonSelect.Core/Rules/Placement.cs`
- Create: `src/MonSelect.Core/Rules/Rule.cs`
- Create: `src/MonSelect.Core/Rules/RuleSet.cs`
- Create: `src/MonSelect.Core/Rules/YamlStore.cs`
- Test: `tests/MonSelect.Core.Tests/YamlStoreTests.cs`

**Interfaces:**
- Consumes: `IfMissing`, `MonitorId`, `WindowState`, `Rect`.
- Produces:
  - `sealed record MatchCriteria(string? Exe, string? CommandLine, string? ClassName, string? Title, string? Aumid)`.
  - `enum ApplyMode { All, First, Rotate }`.
  - `sealed record RulePlacement(IReadOnlyList<string> MonitorAliases, WindowState State, Rect? Rect)`.
  - `sealed record Rule(string Name, bool Enabled, MatchCriteria Match, RulePlacement Place, ApplyMode Apply, IfMissing IfMissing, IReadOnlyList<int> RetryMs)`.
  - `sealed record MonitorAlias(string Path, string Label)`.
  - `sealed record RuleSet(int Version, IReadOnlyDictionary<string, MonitorAlias> Monitors, IReadOnlyList<Rule> Rules)`.
  - `static class YamlStore` con `RuleSet Load(string path)`, `RuleSet Parse(string yaml)` y `void Save(string path, RuleSet set)`. `Parse` lanza `RuleSetFormatException` con mensaje legible ante YAML inválido.

- [ ] **Step 1: Agregar YamlDotNet**

```bash
dotnet add src/MonSelect.Core/MonSelect.Core.csproj package YamlDotNet
```

- [ ] **Step 2: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/YamlStoreTests.cs`:

```csharp
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class YamlStoreTests
{
    private const string FullDocument = """
        version: 1
        monitors:
          benq:
            path: '\\?\DISPLAY#BNQ7820#1&aaaa&0&UID268#{guid}'
            label: "BenQ (derecha)"
          vertical:
            path: '\\?\DISPLAY#GSM57EE#1&aaaa&0&UID264#{guid}'
            label: "LG (vertical)"
        defaults:
          if_missing: skip
          retry_ms: [0, 150, 400, 800]
        rules:
          - name: RustDesk
            enabled: true
            match:
              exe: "C:/Program Files/RustDesk/rustdesk.exe"
              cmdline: "--connect 123456789"
              class: RustdeskMultiWindow
              title: "^WK-EJEMPLO-01.*"
            place:
              monitor: benq
              state: borderless
            apply: all
          - name: Chrome rotando
            match:
              exe: "C:/Program Files/Google/Chrome/Application/chrome.exe"
            place:
              monitor: [benq, vertical]
              state: maximized
            apply: rotate
            if_missing: primary
            retry_ms: [0, 500]
        """;

    [Fact]
    public void Parses_monitor_aliases()
    {
        var set = YamlStore.Parse(FullDocument);

        Assert.Equal(2, set.Monitors.Count);
        Assert.Equal("BenQ (derecha)", set.Monitors["benq"].Label);
        Assert.Contains("UID268", set.Monitors["benq"].Path);
    }

    [Fact]
    public void Parses_all_match_criteria()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0];

        Assert.Equal("C:/Program Files/RustDesk/rustdesk.exe", rule.Match.Exe);
        Assert.Equal("--connect 123456789", rule.Match.CommandLine);
        Assert.Equal("RustdeskMultiWindow", rule.Match.ClassName);
        Assert.Equal("^WK-EJEMPLO-01.*", rule.Match.Title);
        Assert.Null(rule.Match.Aumid);
    }

    [Fact]
    public void A_single_monitor_becomes_a_one_element_list()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0];

        Assert.Equal(new[] { "benq" }, rule.Place.MonitorAliases);
        Assert.Equal(WindowState.Borderless, rule.Place.State);
    }

    [Fact]
    public void A_monitor_list_is_preserved_in_order()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[1];

        Assert.Equal(new[] { "benq", "vertical" }, rule.Place.MonitorAliases);
        Assert.Equal(ApplyMode.Rotate, rule.Apply);
    }

    [Fact]
    public void Rules_default_to_enabled_all_and_the_global_defaults()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[0];

        Assert.True(rule.Enabled);
        Assert.Equal(ApplyMode.All, rule.Apply);
        Assert.Equal(IfMissing.Skip, rule.IfMissing);
        Assert.Equal(new[] { 0, 150, 400, 800 }, rule.RetryMs);
    }

    [Fact]
    public void A_rule_overrides_the_global_defaults()
    {
        var rule = YamlStore.Parse(FullDocument).Rules[1];

        Assert.Equal(IfMissing.Primary, rule.IfMissing);
        Assert.Equal(new[] { 0, 500 }, rule.RetryMs);
    }

    [Fact]
    public void Round_trips_through_save_and_load()
    {
        var original = YamlStore.Parse(FullDocument);
        var dir = Directory.CreateTempSubdirectory("monselect-tests");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.yaml");
            YamlStore.Save(path, original);
            var reloaded = YamlStore.Load(path);

            Assert.Equal(original.Monitors.Count, reloaded.Monitors.Count);
            Assert.Equal(original.Rules.Count, reloaded.Rules.Count);
            Assert.Equal(original.Rules[1].Place.MonitorAliases, reloaded.Rules[1].Place.MonitorAliases);
            Assert.Equal(original.Rules[1].RetryMs, reloaded.Rules[1].RetryMs);
            Assert.Equal(original.Rules[0].Match.Title, reloaded.Rules[0].Match.Title);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_yaml_throws_a_readable_error()
    {
        var ex = Assert.Throws<RuleSetFormatException>(
            () => YamlStore.Parse("rules:\n  - name: roto\n   place: mal indentado\n"));

        Assert.Contains("rules.yaml", ex.Message);
    }

    [Fact]
    public void An_unknown_state_names_the_offending_value()
    {
        var ex = Assert.Throws<RuleSetFormatException>(() => YamlStore.Parse("""
            version: 1
            rules:
              - name: estado invalido
                place:
                  monitor: benq
                  state: pantalla-completa
            """));

        Assert.Contains("pantalla-completa", ex.Message);
    }

    [Fact]
    public void An_empty_document_yields_an_empty_rule_set()
    {
        var set = YamlStore.Parse("version: 1\n");

        Assert.Empty(set.Rules);
        Assert.Empty(set.Monitors);
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test --filter YamlStoreTests`
Expected: FAIL — `YamlStore` no existe.

- [ ] **Step 4: Implementar el modelo de reglas**

`src/MonSelect.Core/Rules/MatchCriteria.cs`:

```csharp
namespace MonSelect.Core.Rules;

/// <summary>
/// Criterios de identificación de una ventana. Todos son opcionales; los que
/// están presentes se combinan con AND. Un <see cref="MatchCriteria"/> sin
/// ningún campo matchea cualquier ventana.
/// </summary>
public sealed record MatchCriteria(
    string? Exe = null,
    string? CommandLine = null,
    string? ClassName = null,
    string? Title = null,
    string? Aumid = null)
{
    public static readonly MatchCriteria Any = new();

    public bool IsEmpty =>
        Exe is null && CommandLine is null && ClassName is null
        && Title is null && Aumid is null;
}
```

`src/MonSelect.Core/Rules/Placement.cs`:

```csharp
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Rules;

/// <summary>Qué hacer cuando una regla matchea varias ventanas.</summary>
public enum ApplyMode
{
    /// <summary>Aplicar a cada ventana que matchee.</summary>
    All,

    /// <summary>Aplicar sólo a la primera ventana mientras el proceso viva.</summary>
    First,

    /// <summary>Recorrer la lista de monitores, uno por ventana, reciclando al agotarla.</summary>
    Rotate,
}

/// <param name="MonitorAliases">
/// Alias definidos en el bloque monitors. Un solo alias para All y First;
/// la lista ordenada que recorre Rotate.
/// </param>
/// <param name="Rect">Sólo se usa con <see cref="WindowState.Normal"/>.</param>
public sealed record RulePlacement(
    IReadOnlyList<string> MonitorAliases,
    WindowState State,
    Rect? Rect = null);
```

`src/MonSelect.Core/Rules/Rule.cs`:

```csharp
using MonSelect.Core.Monitors;

namespace MonSelect.Core.Rules;

public sealed record Rule(
    string Name,
    MatchCriteria Match,
    RulePlacement Place,
    bool Enabled = true,
    ApplyMode Apply = ApplyMode.All,
    IfMissing IfMissing = IfMissing.Skip,
    IReadOnlyList<int>? RetryMs = null)
{
    public static readonly IReadOnlyList<int> DefaultRetryMs = new[] { 0, 150, 400, 800 };

    public IReadOnlyList<int> EffectiveRetryMs => RetryMs ?? DefaultRetryMs;
}
```

`src/MonSelect.Core/Rules/RuleSet.cs`:

```csharp
namespace MonSelect.Core.Rules;

public sealed record MonitorAlias(string Path, string Label);

public sealed record RuleSet(
    int Version,
    IReadOnlyDictionary<string, MonitorAlias> Monitors,
    IReadOnlyList<Rule> Rules)
{
    public static readonly RuleSet Empty = new(
        1,
        new Dictionary<string, MonitorAlias>(),
        Array.Empty<Rule>());
}

/// <summary>Config ilegible. El mensaje va directo al tray, así que tiene que ser humano.</summary>
public sealed class RuleSetFormatException(string message, Exception? inner = null)
    : Exception(message, inner);
```

- [ ] **Step 5: Implementar `YamlStore`**

`src/MonSelect.Core/Rules/YamlStore.cs`:

```csharp
using MonSelect.Core.Monitors;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;
using YamlDotNet.RepresentationModel;

namespace MonSelect.Core.Rules;

/// <summary>
/// Carga y guarda rules.yaml. Se parsea con el modelo de representación en vez
/// de con el deserializador por objetos porque el campo monitor acepta tanto un
/// escalar como una secuencia, y porque los errores tienen que nombrar el valor
/// exacto que el usuario escribió mal.
/// </summary>
public static class YamlStore
{
    public static RuleSet Load(string path)
    {
        if (!File.Exists(path))
            return RuleSet.Empty;

        return Parse(File.ReadAllText(path));
    }

    public static RuleSet Parse(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (Exception ex)
        {
            throw new RuleSetFormatException($"rules.yaml no es YAML válido: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
            return RuleSet.Empty;

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new RuleSetFormatException("rules.yaml tiene que empezar con un mapa de claves.");

        var version = (int)ReadScalarLong(root, "version", 1);
        var monitors = ReadMonitors(root);
        var (defaultIfMissing, defaultRetry) = ReadDefaults(root);
        var rules = ReadRules(root, defaultIfMissing, defaultRetry);

        return new RuleSet(version, monitors, rules);
    }

    public static void Save(string path, RuleSet set)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var writer = new StringWriter();
        writer.WriteLine($"version: {set.Version}");

        if (set.Monitors.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("monitors:");
            foreach (var (alias, monitor) in set.Monitors)
            {
                writer.WriteLine($"  {alias}:");
                writer.WriteLine($"    path: '{monitor.Path}'");
                writer.WriteLine($"    label: {Quote(monitor.Label)}");
            }
        }

        writer.WriteLine();
        writer.WriteLine("defaults:");
        writer.WriteLine("  if_missing: skip");
        writer.WriteLine($"  retry_ms: [{string.Join(", ", Rule.DefaultRetryMs)}]");

        writer.WriteLine();
        writer.WriteLine("rules:");
        foreach (var rule in set.Rules)
            WriteRule(writer, rule);

        File.WriteAllText(path, writer.ToString());
    }

    private static void WriteRule(TextWriter writer, Rule rule)
    {
        writer.WriteLine($"  - name: {Quote(rule.Name)}");
        if (!rule.Enabled)
            writer.WriteLine("    enabled: false");

        writer.WriteLine("    match:");
        WriteOptional(writer, "exe", rule.Match.Exe);
        WriteOptional(writer, "cmdline", rule.Match.CommandLine);
        WriteOptional(writer, "class", rule.Match.ClassName);
        WriteOptional(writer, "title", rule.Match.Title);
        WriteOptional(writer, "aumid", rule.Match.Aumid);

        writer.WriteLine("    place:");
        writer.WriteLine(rule.Place.MonitorAliases.Count == 1
            ? $"      monitor: {rule.Place.MonitorAliases[0]}"
            : $"      monitor: [{string.Join(", ", rule.Place.MonitorAliases)}]");
        writer.WriteLine($"      state: {rule.Place.State.ToString().ToLowerInvariant()}");
        if (rule.Place.Rect is { } r)
            writer.WriteLine($"      rect: [{r.Left}, {r.Top}, {r.Right}, {r.Bottom}]");

        if (rule.Apply != ApplyMode.All)
            writer.WriteLine($"    apply: {rule.Apply.ToString().ToLowerInvariant()}");
        if (rule.IfMissing != IfMissing.Skip)
            writer.WriteLine($"    if_missing: {rule.IfMissing.ToString().ToLowerInvariant()}");
        if (rule.RetryMs is { } retry)
            writer.WriteLine($"    retry_ms: [{string.Join(", ", retry)}]");
    }

    private static void WriteOptional(TextWriter writer, string key, string? value)
    {
        if (value is not null)
            writer.WriteLine($"      {key}: {Quote(value)}");
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static IReadOnlyDictionary<string, MonitorAlias> ReadMonitors(YamlMappingNode root)
    {
        var result = new Dictionary<string, MonitorAlias>(StringComparer.OrdinalIgnoreCase);
        if (!TryGet(root, "monitors", out var node) || node is not YamlMappingNode monitors)
            return result;

        foreach (var (key, value) in monitors.Children)
        {
            var alias = ((YamlScalarNode)key).Value ?? string.Empty;
            if (value is not YamlMappingNode entry)
                throw new RuleSetFormatException($"El monitor '{alias}' tiene que ser un mapa con path y label.");

            var path = ReadScalarString(entry, "path")
                ?? throw new RuleSetFormatException($"El monitor '{alias}' no tiene path.");
            var label = ReadScalarString(entry, "label") ?? alias;

            result[alias] = new MonitorAlias(path, label);
        }

        return result;
    }

    private static (IfMissing, IReadOnlyList<int>) ReadDefaults(YamlMappingNode root)
    {
        var ifMissing = IfMissing.Skip;
        var retry = Rule.DefaultRetryMs;

        if (!TryGet(root, "defaults", out var node) || node is not YamlMappingNode defaults)
            return (ifMissing, retry);

        if (ReadScalarString(defaults, "if_missing") is { } raw)
            ifMissing = ParseEnum<IfMissing>(raw, "if_missing");

        if (ReadIntList(defaults, "retry_ms") is { } list)
            retry = list;

        return (ifMissing, retry);
    }

    private static IReadOnlyList<Rule> ReadRules(
        YamlMappingNode root, IfMissing defaultIfMissing, IReadOnlyList<int> defaultRetry)
    {
        if (!TryGet(root, "rules", out var node))
            return Array.Empty<Rule>();

        if (node is not YamlSequenceNode sequence)
            throw new RuleSetFormatException("rules tiene que ser una lista.");

        var rules = new List<Rule>();
        foreach (var item in sequence)
        {
            if (item is not YamlMappingNode entry)
                throw new RuleSetFormatException("Cada regla tiene que ser un mapa.");

            var name = ReadScalarString(entry, "name") ?? $"regla {rules.Count + 1}";
            var enabled = ReadScalarString(entry, "enabled") is not "false";

            var match = TryGet(entry, "match", out var m) && m is YamlMappingNode matchNode
                ? new MatchCriteria(
                    ReadScalarString(matchNode, "exe"),
                    ReadScalarString(matchNode, "cmdline"),
                    ReadScalarString(matchNode, "class"),
                    ReadScalarString(matchNode, "title"),
                    ReadScalarString(matchNode, "aumid"))
                : MatchCriteria.Any;

            if (!TryGet(entry, "place", out var p) || p is not YamlMappingNode placeNode)
                throw new RuleSetFormatException($"La regla '{name}' no tiene bloque place.");

            var place = new RulePlacement(
                ReadMonitorAliases(placeNode, name),
                ParseEnum<WindowState>(
                    ReadScalarString(placeNode, "state") ?? "normal", "state"),
                ReadRect(placeNode));

            var apply = ParseEnum<ApplyMode>(ReadScalarString(entry, "apply") ?? "all", "apply");
            var ifMissing = ReadScalarString(entry, "if_missing") is { } rawIfMissing
                ? ParseEnum<IfMissing>(rawIfMissing, "if_missing")
                : defaultIfMissing;
            var retry = ReadIntList(entry, "retry_ms") ?? defaultRetry;

            if (apply == ApplyMode.Rotate && place.MonitorAliases.Count < 2)
                throw new RuleSetFormatException(
                    $"La regla '{name}' usa apply: rotate pero place.monitor no es una lista de dos o más alias.");

            rules.Add(new Rule(name, match, place, enabled, apply, ifMissing, retry));
        }

        return rules;
    }

    private static IReadOnlyList<string> ReadMonitorAliases(YamlMappingNode place, string ruleName)
    {
        if (!TryGet(place, "monitor", out var node))
            throw new RuleSetFormatException($"La regla '{ruleName}' no dice a qué monitor va.");

        return node switch
        {
            YamlScalarNode scalar when scalar.Value is { Length: > 0 } v => new[] { v },
            YamlSequenceNode seq => seq
                .OfType<YamlScalarNode>()
                .Select(s => s.Value ?? string.Empty)
                .Where(s => s.Length > 0)
                .ToArray(),
            _ => throw new RuleSetFormatException(
                $"La regla '{ruleName}' tiene un monitor que no es ni un alias ni una lista de alias."),
        };
    }

    private static Rect? ReadRect(YamlMappingNode place)
    {
        if (!TryGet(place, "rect", out var node) || node is not YamlSequenceNode seq)
            return null;

        var values = seq.OfType<YamlScalarNode>()
            .Select(s => int.TryParse(s.Value, out var v)
                ? v
                : throw new RuleSetFormatException($"rect tiene un valor no numérico: '{s.Value}'."))
            .ToArray();

        if (values.Length != 4)
            throw new RuleSetFormatException("rect tiene que tener exactamente cuatro enteros: [left, top, right, bottom].");

        return Rect.FromLtrb(values[0], values[1], values[2], values[3]);
    }

    private static IReadOnlyList<int>? ReadIntList(YamlMappingNode node, string key)
    {
        if (!TryGet(node, key, out var value) || value is not YamlSequenceNode seq)
            return null;

        return seq.OfType<YamlScalarNode>()
            .Select(s => int.TryParse(s.Value, out var v)
                ? v
                : throw new RuleSetFormatException($"{key} tiene un valor no numérico: '{s.Value}'."))
            .ToArray();
    }

    private static bool TryGet(YamlMappingNode node, string key, out YamlNode value)
    {
        foreach (var (k, v) in node.Children)
        {
            if (k is YamlScalarNode scalar
                && string.Equals(scalar.Value, key, StringComparison.OrdinalIgnoreCase))
            {
                value = v;
                return true;
            }
        }

        value = null!;
        return false;
    }

    private static string? ReadScalarString(YamlMappingNode node, string key)
        => TryGet(node, key, out var value) && value is YamlScalarNode scalar
            && scalar.Value is { Length: > 0 } text
            && !string.Equals(text, "null", StringComparison.OrdinalIgnoreCase)
                ? text
                : null;

    private static long ReadScalarLong(YamlMappingNode node, string key, long fallback)
        => ReadScalarString(node, key) is { } text && long.TryParse(text, out var value)
            ? value
            : fallback;

    private static T ParseEnum<T>(string raw, string field) where T : struct, Enum
        => Enum.TryParse<T>(raw, ignoreCase: true, out var value)
            ? value
            : throw new RuleSetFormatException(
                $"'{raw}' no es un valor válido para {field}. Opciones: {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test --filter YamlStoreTests`
Expected: PASS, 10 tests.

- [ ] **Step 7: Commit**

```bash
git add src/MonSelect.Core/Rules tests/MonSelect.Core.Tests/YamlStoreTests.cs
git commit -m "feat: add rule model and YAML config store"
```

---

## Task 6: RuleMatcher

**Files:**
- Create: `src/MonSelect.Core/Windows/WindowInfo.cs`
- Create: `src/MonSelect.Core/Rules/RuleMatcher.cs`
- Test: `tests/MonSelect.Core.Tests/RuleMatcherTests.cs`

**Interfaces:**
- Consumes: `Rule`, `MatchCriteria`, `Rect`, `MonitorId`, `WindowState`.
- Produces:
  - `sealed record WindowInfo(nint Handle, uint ProcessId, string? ExePath, string? CommandLine, string ClassName, string Title, string? Aumid, Rect Bounds, WindowState CurrentState)`.
  - `static class RuleMatcher` con `Rule? FirstMatch(IReadOnlyList<Rule> rules, WindowInfo window)` y `bool Matches(Rule rule, WindowInfo window)`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/RuleMatcherTests.cs`:

```csharp
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class RuleMatcherTests
{
    private static WindowInfo RustDesk(
        string? exe = @"C:\Program Files\RustDesk\rustdesk.exe",
        string? cmdline = @"""C:\Program Files\RustDesk\rustdesk.exe"" --connect 123456789",
        string className = "RustdeskMultiWindow",
        string title = "WK-EJEMPLO-01 - Remote Desktop - RustDesk",
        string? aumid = null)
        => new(
            Handle: 1234,
            ProcessId: 23340,
            ExePath: exe,
            CommandLine: cmdline,
            ClassName: className,
            Title: title,
            Aumid: aumid,
            Bounds: Rect.FromLtrb(3000, 0, 4920, 1080),
            CurrentState: WindowState.Maximized);

    private static Rule RuleWith(string name, MatchCriteria match)
        => new(name, match, new RulePlacement(new[] { "benq" }, WindowState.Borderless));

    [Fact]
    public void An_empty_criteria_matches_anything()
    {
        Assert.True(RuleMatcher.Matches(RuleWith("todo", MatchCriteria.Any), RustDesk()));
    }

    [Fact]
    public void Exe_paths_compare_case_insensitively_and_normalise_separators()
    {
        var rule = RuleWith("exe", new MatchCriteria(
            Exe: "c:/program files/rustdesk/RUSTDESK.EXE"));

        Assert.True(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void A_different_exe_does_not_match()
    {
        var rule = RuleWith("exe", new MatchCriteria(Exe: @"C:\Windows\notepad.exe"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void Cmdline_matches_as_a_substring_by_default()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("cmd", new MatchCriteria(CommandLine: "--connect 123456789")), RustDesk()));
    }

    [Fact]
    public void Cmdline_wrapped_in_slashes_is_a_regex()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("cmd", new MatchCriteria(CommandLine: @"/--connect \d+/")), RustDesk()));
    }

    [Fact]
    public void A_criterion_on_a_missing_field_never_matches()
    {
        // Command line ausente: proceso elevado o de otro usuario.
        var rule = RuleWith("cmd", new MatchCriteria(CommandLine: "--connect 123456789"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk(cmdline: null)));
    }

    [Fact]
    public void Class_name_compares_exactly()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("c", new MatchCriteria(ClassName: "RustdeskMultiWindow")), RustDesk()));
        Assert.False(RuleMatcher.Matches(
            RuleWith("c", new MatchCriteria(ClassName: "Rustdesk")), RustDesk()));
    }

    [Fact]
    public void Title_is_always_a_regex()
    {
        Assert.True(RuleMatcher.Matches(
            RuleWith("t", new MatchCriteria(Title: "^WK-EJEMPLO-01.*")), RustDesk()));
        Assert.False(RuleMatcher.Matches(
            RuleWith("t", new MatchCriteria(Title: "^OTRA-MAQUINA")), RustDesk()));
    }

    [Fact]
    public void An_invalid_title_regex_never_matches_instead_of_throwing()
    {
        var rule = RuleWith("t", new MatchCriteria(Title: "[sin-cerrar"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void All_present_criteria_must_hold()
    {
        var rule = RuleWith("and", new MatchCriteria(
            Exe: @"C:\Program Files\RustDesk\rustdesk.exe",
            ClassName: "RustdeskMultiWindow",
            Title: "^NO-COINCIDE"));

        Assert.False(RuleMatcher.Matches(rule, RustDesk()));
    }

    [Fact]
    public void The_first_matching_rule_wins_regardless_of_specificity()
    {
        var rules = new[]
        {
            RuleWith("generica", new MatchCriteria(Exe: @"C:\Program Files\RustDesk\rustdesk.exe")),
            RuleWith("especifica", new MatchCriteria(
                Exe: @"C:\Program Files\RustDesk\rustdesk.exe",
                CommandLine: "--connect 123456789")),
        };

        Assert.Equal("generica", RuleMatcher.FirstMatch(rules, RustDesk())!.Name);
    }

    [Fact]
    public void Disabled_rules_are_skipped()
    {
        var rules = new[]
        {
            RuleWith("apagada", MatchCriteria.Any) with { Enabled = false },
            RuleWith("prendida", MatchCriteria.Any),
        };

        Assert.Equal("prendida", RuleMatcher.FirstMatch(rules, RustDesk())!.Name);
    }

    [Fact]
    public void No_matching_rule_returns_null()
    {
        var rules = new[] { RuleWith("nada", new MatchCriteria(Exe: @"C:\Windows\notepad.exe")) };

        Assert.Null(RuleMatcher.FirstMatch(rules, RustDesk()));
    }

    [Fact]
    public void Aumid_compares_exactly_when_present()
    {
        var rule = RuleWith("uwp", new MatchCriteria(Aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));

        Assert.True(RuleMatcher.Matches(
            rule, RustDesk(aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")));
        Assert.False(RuleMatcher.Matches(rule, RustDesk(aumid: null)));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test --filter RuleMatcherTests`
Expected: FAIL — `WindowInfo` y `RuleMatcher` no existen.

- [ ] **Step 3: Implementar `WindowInfo`**

`src/MonSelect.Core/Windows/WindowInfo.cs`:

```csharp
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
```

- [ ] **Step 4: Implementar `RuleMatcher`**

`src/MonSelect.Core/Rules/RuleMatcher.cs`:

```csharp
using System.Text.RegularExpressions;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Rules;

/// <summary>
/// Decide qué regla gobierna una ventana. Puro y sin estado: es la pieza que
/// más se testea porque es donde el usuario va a discutir el comportamiento.
/// </summary>
public static class RuleMatcher
{
    /// <summary>
    /// Primera regla habilitada que matchea, en el orden del archivo. No hay
    /// scoring por especificidad: con veinte reglas, un ganador impredecible es
    /// imposible de depurar.
    /// </summary>
    public static Rule? FirstMatch(IReadOnlyList<Rule> rules, WindowInfo window)
    {
        foreach (var rule in rules)
        {
            if (rule.Enabled && Matches(rule, window))
                return rule;
        }

        return null;
    }

    public static bool Matches(Rule rule, WindowInfo window)
    {
        var c = rule.Match;

        if (c.Exe is not null && !ExeMatches(c.Exe, window.ExePath))
            return false;

        if (c.CommandLine is not null && !TextMatches(c.CommandLine, window.CommandLine))
            return false;

        if (c.ClassName is not null
            && !string.Equals(c.ClassName, window.ClassName, StringComparison.Ordinal))
            return false;

        if (c.Title is not null && !RegexMatches(c.Title, window.Title))
            return false;

        if (c.Aumid is not null
            && !string.Equals(c.Aumid, window.Aumid, StringComparison.Ordinal))
            return false;

        return true;
    }

    /// <summary>Compara paths normalizando separadores y sin distinguir mayúsculas.</summary>
    private static bool ExeMatches(string pattern, string? actual)
    {
        if (actual is null)
            return false;

        return string.Equals(Normalise(pattern), Normalise(actual), StringComparison.OrdinalIgnoreCase);

        static string Normalise(string path) => path.Replace('/', '\\').TrimEnd('\\');
    }

    /// <summary>Substring por defecto; regex si el patrón viene envuelto entre barras.</summary>
    private static bool TextMatches(string pattern, string? actual)
    {
        if (actual is null)
            return false;

        if (pattern.Length >= 2 && pattern[0] == '/' && pattern[^1] == '/')
            return RegexMatches(pattern[1..^1], actual);

        return actual.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Un regex inválido no matchea en vez de tirar excepción: la config la
    /// escribe una persona a mano y no puede tumbar el servicio.
    /// </summary>
    private static bool RegexMatches(string pattern, string? actual)
    {
        if (actual is null)
            return false;

        try
        {
            return Regex.IsMatch(actual, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test --filter RuleMatcherTests`
Expected: PASS, 14 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MonSelect.Core/Windows/WindowInfo.cs src/MonSelect.Core/Rules/RuleMatcher.cs tests/MonSelect.Core.Tests/RuleMatcherTests.cs
git commit -m "feat: add rule matcher with first-match precedence"
```

---

## Task 7: Verificación empírica de las coordenadas de WINDOWPLACEMENT

**Files:**
- Modify: `tools/probe/Program.cs`
- Create: `docs/superpowers/findings/windowplacement-coordinates.md`

**Interfaces:**
- Consumes: `NativeMethods`, `Win32MonitorSystem`.
- Produces: un documento con la respuesta a una sola pregunta, que la Task 8 consume como constante de diseño.

**Por qué esta task existe:** el spec, sección 13, marca esto como el único punto donde el diseño depende de algo no verificado. La documentación de `WINDOWPLACEMENT` dice que `rcNormalPosition` está en coordenadas de *workspace*, no de pantalla. Si eso es cierto en un escritorio multi-monitor con orígenes negativos, `PlacementCalculator` tiene que corregir por `rcWork - rcMonitor`; si es falso, la corrección introduciría un error de 32 px en cada colocación. Construir la Task 8 sobre una suposición y descubrir el error después significa rehacer todos sus tests.

- [ ] **Step 1: Agregar el experimento al probe**

Añadir a `tools/probe/Program.cs`:

```csharp
using System.Runtime.InteropServices;
using MonSelect.Core.Win32;

// --- Experimento: ¿en qué espacio de coordenadas vive rcNormalPosition? ---
//
// Método: se crea una ventana en una posición conocida de un monitor no
// primario con taskbar, se lee su WINDOWPLACEMENT y se compara rcNormalPosition
// contra GetWindowRect. Si coinciden, las coordenadas son de pantalla y no hace
// falta corrección. Si difieren, la diferencia es el offset a aplicar.

if (args.Contains("--placement"))
{
    var monitors = new Win32MonitorSystem().GetMonitors();
    var target = monitors.FirstOrDefault(m => !m.IsPrimary && m.WorkArea != m.Bounds)
                 ?? monitors[0];

    Console.WriteLine($"Monitor de prueba: {target.GdiName}");
    Console.WriteLine($"  bounds = {target.Bounds}");
    Console.WriteLine($"  work   = {target.WorkArea}");
    Console.WriteLine($"  offset work-bounds = ({target.WorkArea.Left - target.Bounds.Left}, " +
                      $"{target.WorkArea.Top - target.Bounds.Top})");
    Console.WriteLine();
    Console.WriteLine("Abrí una ventana cualquiera, movela a ese monitor, dejala en estado");
    Console.WriteLine("normal (ni maximizada ni minimizada) y poné el foco en ella.");
    Console.WriteLine("Tenés 8 segundos.");
    Thread.Sleep(8000);

    var hwnd = GetForegroundWindow();
    if (hwnd == 0)
    {
        Console.WriteLine("No se pudo obtener la ventana en primer plano.");
        return;
    }

    NativeMethods.GetWindowRect(hwnd, out var screenRect);
    var placement = WindowPlacement.Create();
    NativeMethods.GetWindowPlacement(hwnd, ref placement);

    Console.WriteLine();
    Console.WriteLine($"GetWindowRect       : {screenRect}");
    Console.WriteLine($"rcNormalPosition    : {placement.rcNormalPosition}");
    Console.WriteLine($"showCmd             : {placement.showCmd}");
    Console.WriteLine();
    Console.WriteLine($"delta left = {placement.rcNormalPosition.Left - screenRect.Left}");
    Console.WriteLine($"delta top  = {placement.rcNormalPosition.Top - screenRect.Top}");
    Console.WriteLine();
    Console.WriteLine(placement.rcNormalPosition == screenRect
        ? "RESULTADO: coordenadas de PANTALLA. No corregir."
        : "RESULTADO: coordenadas desplazadas. El delta de arriba es la corrección.");
    return;
}

[DllImport("user32.dll")]
static extern nint GetForegroundWindow();
```

Nota: `WindowPlacement` y `NativeMethods` son `internal` en `MonSelect.Core`. Para que el probe los vea, agregar en `src/MonSelect.Core/MonSelect.Core.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Probe" />
  <InternalsVisibleTo Include="MonSelect.Core.Tests" />
</ItemGroup>
```

- [ ] **Step 2: Correr el experimento**

Run: `dotnet run --project tools/probe -- --placement`

Seguir la instrucción en pantalla: mover el Bloc de notas al monitor no primario que el probe nombró, dejarlo en estado normal, darle foco, esperar.

- [ ] **Step 3: Repetir en el monitor con origen negativo**

Repetir el paso 2 pero moviendo la ventana a `\\.\DISPLAY2`, que vive enteramente en Y negativo. Ése es el caso donde un error de signo en la corrección pasaría desapercibido en los otros tres monitores.

- [ ] **Step 4: Documentar el hallazgo**

Crear `docs/superpowers/findings/windowplacement-coordinates.md` con esta plantilla, completando la sección de resultados con lo observado:

```markdown
# ¿En qué coordenadas vive WINDOWPLACEMENT.rcNormalPosition?

**Fecha:** <fecha>
**Método:** `dotnet run --project tools/probe -- --placement`

## Resultado

| Monitor | GetWindowRect | rcNormalPosition | delta (left, top) |
|---|---|---|---|
| <DISPLAYn, no primario con taskbar> | | | |
| `\\.\DISPLAY2` (origen negativo) | | | |

## Conclusión

<Una de estas dos, borrar la otra:>

- **Coordenadas de pantalla.** `PlacementCalculator` usa los rects tal cual,
  sin corrección. La constante `WorkspaceOffsetApplies` es `false`.
- **Coordenadas desplazadas por el área de trabajo.** `PlacementCalculator`
  resta `WorkArea - Bounds` del monitor de destino antes de escribir
  `rcNormalPosition`. La constante `WorkspaceOffsetApplies` es `true`.

## Por qué importa

Un error acá desplaza cada ventana colocada por el alto de la taskbar (32 px en
este equipo) sin que nada falle de forma visible.
```

- [ ] **Step 5: Commit**

```bash
git add tools/probe docs/superpowers/findings src/MonSelect.Core/MonSelect.Core.csproj
git commit -m "docs: record empirical WINDOWPLACEMENT coordinate space finding"
```

---

## Task 8: PlacementCalculator

**Files:**
- Create: `src/MonSelect.Core/Windows/TargetPlacement.cs`
- Create: `src/MonSelect.Core/Windows/PlacementCalculator.cs`
- Test: `tests/MonSelect.Core.Tests/PlacementCalculatorTests.cs`

**Interfaces:**
- Consumes: `MonitorInfo`, `WindowState`, `Rect`, `ShowCommand`.
- Produces:
  - `sealed record TargetPlacement(ShowCommand ShowCmd, Rect NormalPosition, bool StripBorders, Rect ExpectedBounds)`.
  - `static class PlacementCalculator` con `TargetPlacement Compute(MonitorInfo monitor, WindowState state, Rect? explicitRect, Rect currentBounds)` y la constante `bool WorkspaceOffsetApplies`.

**Antes de empezar:** leer `docs/superpowers/findings/windowplacement-coordinates.md` de la Task 7 y fijar `WorkspaceOffsetApplies` según su conclusión. Los tests de abajo asumen `false`; si el hallazgo dijo lo contrario, ajustar los valores esperados de `NormalPosition` restando el offset del monitor, y dejar un comentario en el test que lo explique.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/PlacementCalculatorTests.cs`:

```csharp
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class PlacementCalculatorTests
{
    // Ventana chica sentada en el monitor primario, antes de ser movida.
    private static readonly Rect Current = Rect.FromLtrb(100, 100, 900, 700);

    [Fact]
    public void Maximized_targets_the_work_area_so_the_taskbar_stays_visible()
    {
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Maximized, null, Current);

        Assert.Equal(ShowCommand.Maximized, result.ShowCmd);
        Assert.Equal(FakeMonitorSystem.Right.WorkArea, result.ExpectedBounds);
        Assert.False(result.StripBorders);
    }

    [Fact]
    public void Maximized_puts_the_restore_rect_inside_the_target_monitor()
    {
        // Windows maximiza donde la ventana ya está, así que rcNormalPosition
        // tiene que caer dentro del monitor destino o la ventana se maximiza
        // en el monitor equivocado.
        var target = FakeMonitorSystem.Right;

        var result = PlacementCalculator.Compute(target, WindowState.Maximized, null, Current);

        Assert.True(result.NormalPosition.Left >= target.WorkArea.Left);
        Assert.True(result.NormalPosition.Top >= target.WorkArea.Top);
        Assert.True(result.NormalPosition.Right <= target.WorkArea.Right);
        Assert.True(result.NormalPosition.Bottom <= target.WorkArea.Bottom);
    }

    [Fact]
    public void Maximized_preserves_the_current_window_size_in_the_restore_rect()
    {
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Maximized, null, Current);

        Assert.Equal(Current.Width, result.NormalPosition.Width);
        Assert.Equal(Current.Height, result.NormalPosition.Height);
    }

    [Fact]
    public void Borderless_targets_the_full_monitor_and_strips_the_frame()
    {
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Borderless, null, Current);

        Assert.Equal(ShowCommand.Maximized, result.ShowCmd);
        Assert.Equal(FakeMonitorSystem.Right.Bounds, result.ExpectedBounds);
        Assert.True(result.StripBorders);
    }

    [Fact]
    public void Borderless_reproduces_the_rect_measured_on_RustDesk()
    {
        // Spec seccion 3.3: (3000,0)-(4920,1080), tapando la taskbar.
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Borderless, null, Current);

        Assert.Equal(Rect.FromLtrb(3000, 0, 4920, 1080), result.ExpectedBounds);
    }

    [Fact]
    public void Minimized_still_moves_the_restore_rect_to_the_target_monitor()
    {
        // Al restaurarla, la ventana tiene que aparecer en el monitor de la regla.
        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Vertical, WindowState.Minimized, null, Current);

        Assert.Equal(ShowCommand.Minimized, result.ShowCmd);
        Assert.True(result.NormalPosition.Left >= FakeMonitorSystem.Vertical.WorkArea.Left);
        Assert.True(result.NormalPosition.Top >= FakeMonitorSystem.Vertical.WorkArea.Top);
    }

    [Fact]
    public void Normal_without_an_explicit_rect_centres_the_current_size_on_the_monitor()
    {
        var target = FakeMonitorSystem.Right;

        var result = PlacementCalculator.Compute(target, WindowState.Normal, null, Current);

        Assert.Equal(ShowCommand.Normal, result.ShowCmd);
        Assert.Equal(Current.Width, result.NormalPosition.Width);
        Assert.Equal(Current.Height, result.NormalPosition.Height);

        var expectedLeft = target.WorkArea.Left + (target.WorkArea.Width - Current.Width) / 2;
        Assert.Equal(expectedLeft, result.NormalPosition.Left);
    }

    [Fact]
    public void Normal_with_an_explicit_rect_uses_it_verbatim()
    {
        var wanted = Rect.FromLtrb(3100, 50, 4000, 800);

        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Normal, wanted, Current);

        Assert.Equal(wanted, result.NormalPosition);
        Assert.Equal(wanted, result.ExpectedBounds);
    }

    [Fact]
    public void Works_on_a_monitor_whose_origin_is_negative()
    {
        var target = FakeMonitorSystem.Above; // (0,-1080)-(1920,0)

        var result = PlacementCalculator.Compute(target, WindowState.Maximized, null, Current);

        Assert.Equal(target.WorkArea, result.ExpectedBounds);
        Assert.True(result.NormalPosition.Top < 0);
        Assert.True(result.NormalPosition.Bottom <= target.WorkArea.Bottom);
    }

    [Fact]
    public void A_window_larger_than_the_target_monitor_is_clamped_to_the_work_area()
    {
        var huge = Rect.FromLtrb(0, 0, 4000, 3000);
        var target = FakeMonitorSystem.Right;

        var result = PlacementCalculator.Compute(target, WindowState.Normal, null, huge);

        Assert.True(result.NormalPosition.Width <= target.WorkArea.Width);
        Assert.True(result.NormalPosition.Height <= target.WorkArea.Height);
        Assert.True(result.NormalPosition.Left >= target.WorkArea.Left);
        Assert.True(result.NormalPosition.Top >= target.WorkArea.Top);
    }

    [Fact]
    public void An_explicit_rect_is_ignored_for_states_other_than_normal()
    {
        var wanted = Rect.FromLtrb(3100, 50, 4000, 800);

        var result = PlacementCalculator.Compute(
            FakeMonitorSystem.Right, WindowState.Maximized, wanted, Current);

        Assert.Equal(FakeMonitorSystem.Right.WorkArea, result.ExpectedBounds);
    }

    [Fact]
    public void The_computation_is_deterministic()
    {
        var a = PlacementCalculator.Compute(FakeMonitorSystem.Right, WindowState.Maximized, null, Current);
        var b = PlacementCalculator.Compute(FakeMonitorSystem.Right, WindowState.Maximized, null, Current);

        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test --filter PlacementCalculatorTests`
Expected: FAIL — `PlacementCalculator` no existe.

- [ ] **Step 3: Implementar `TargetPlacement`**

`src/MonSelect.Core/Windows/TargetPlacement.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <param name="ShowCmd">Valor de showCmd que va en WINDOWPLACEMENT.</param>
/// <param name="NormalPosition">Rect de restauración. Determina en qué monitor maximiza Windows.</param>
/// <param name="StripBorders">True sólo para Borderless.</param>
/// <param name="ExpectedBounds">
/// Dónde debería quedar la ventana si la aplicación coopera. El RetryScheduler
/// compara contra esto para decidir si hace falta otro intento.
/// </param>
public sealed record TargetPlacement(
    ShowCommand ShowCmd,
    Rect NormalPosition,
    bool StripBorders,
    Rect ExpectedBounds);
```

- [ ] **Step 4: Implementar `PlacementCalculator`**

`src/MonSelect.Core/Windows/PlacementCalculator.cs`:

```csharp
using MonSelect.Core.Monitors;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Traduce "este monitor, este estado" a los valores concretos que consume
/// SetWindowPlacement. Puro y sin efectos: es la aritmética que más fácil se
/// equivoca y la que más barato sale testear.
/// </summary>
public static class PlacementCalculator
{
    /// <summary>
    /// Si rcNormalPosition vive en coordenadas de workspace en vez de pantalla,
    /// hay que restar el offset del área de trabajo antes de escribirlo.
    /// El valor sale de la verificación empírica de la Task 7; ver
    /// docs/superpowers/findings/windowplacement-coordinates.md.
    /// </summary>
    public const bool WorkspaceOffsetApplies = false;

    public static TargetPlacement Compute(
        MonitorInfo monitor,
        WindowState state,
        Rect? explicitRect,
        Rect currentBounds)
    {
        return state switch
        {
            WindowState.Borderless => new TargetPlacement(
                ShowCommand.Maximized,
                ToPlacementSpace(CentreOn(monitor.WorkArea, currentBounds), monitor),
                StripBorders: true,
                // Sin caption ni thickframe, una ventana maximizada se expande
                // al monitor completo y no al área de trabajo. Es la firma que
                // se midió en RustDesk (spec, sección 3.3).
                ExpectedBounds: monitor.Bounds),

            WindowState.Maximized => new TargetPlacement(
                ShowCommand.Maximized,
                ToPlacementSpace(CentreOn(monitor.WorkArea, currentBounds), monitor),
                StripBorders: false,
                ExpectedBounds: monitor.WorkArea),

            WindowState.Minimized => new TargetPlacement(
                ShowCommand.Minimized,
                ToPlacementSpace(CentreOn(monitor.WorkArea, currentBounds), monitor),
                StripBorders: false,
                // Minimizada no tiene bounds observables; el retry no compara.
                ExpectedBounds: Rect.FromLtrb(0, 0, 0, 0)),

            WindowState.Normal => NormalPlacement(monitor, explicitRect, currentBounds),

            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Estado desconocido."),
        };
    }

    private static TargetPlacement NormalPlacement(
        MonitorInfo monitor, Rect? explicitRect, Rect currentBounds)
    {
        var rect = explicitRect ?? CentreOn(monitor.WorkArea, currentBounds);

        return new TargetPlacement(
            ShowCommand.Normal,
            ToPlacementSpace(rect, monitor),
            StripBorders: false,
            ExpectedBounds: rect);
    }

    /// <summary>
    /// Coloca un rect del tamaño de <paramref name="size"/> centrado dentro de
    /// <paramref name="area"/>, recortándolo si no entra.
    /// </summary>
    private static Rect CentreOn(Rect area, Rect size)
    {
        var w = Math.Min(size.Width, area.Width);
        var h = Math.Min(size.Height, area.Height);

        var left = area.Left + (area.Width - w) / 2;
        var top = area.Top + (area.Height - h) / 2;

        return Rect.FromLtrb(left, top, left + w, top + h);
    }

    private static Rect ToPlacementSpace(Rect screenRect, MonitorInfo monitor)
    {
        if (!WorkspaceOffsetApplies)
            return screenRect;

        var dx = monitor.WorkArea.Left - monitor.Bounds.Left;
        var dy = monitor.WorkArea.Top - monitor.Bounds.Top;

        return Rect.FromLtrb(
            screenRect.Left - dx,
            screenRect.Top - dy,
            screenRect.Right - dx,
            screenRect.Bottom - dy);
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test --filter PlacementCalculatorTests`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add src/MonSelect.Core/Windows tests/MonSelect.Core.Tests/PlacementCalculatorTests.cs
git commit -m "feat: compute target placement per monitor and window state"
```

---

## Task 9: IWindowSystem y StyleStore

**Files:**
- Create: `src/MonSelect.Core/Windows/IWindowSystem.cs`
- Create: `src/MonSelect.Core/Windows/StyleStore.cs`
- Test: `tests/MonSelect.Core.Tests/Fakes/FakeWindowSystem.cs`
- Test: `tests/MonSelect.Core.Tests/StyleStoreTests.cs`

**Interfaces:**
- Consumes: `Rect`, `ShowCommand`, `StyleMath`.
- Produces:
  - `interface IWindowSystem` con `bool IsWindow(nint)`, `bool IsVisible(nint)`, `Rect GetBounds(nint)`, `uint GetStyle(nint)`, `void SetStyle(nint, uint)`, `void ApplyFrameChange(nint)`, `void SetPlacement(nint, ShowCommand, Rect)`, `void Show(nint, ShowCommand)`.
  - `sealed record BorderlessRecord(long Handle, uint ProcessId, long ProcessStartTicks, uint OriginalStyle)`.
  - `sealed class StyleStore(string path)` con `Remember`, `Forget`, `TryGet`, `All`, `Save`, `Load`.
  - `FakeWindowSystem` para los tests, que registra las llamadas recibidas.

- [ ] **Step 1: Escribir `IWindowSystem`**

`src/MonSelect.Core/Windows/IWindowSystem.cs`:

```csharp
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
```

- [ ] **Step 2: Escribir el fake**

`tests/MonSelect.Core.Tests/Fakes/FakeWindowSystem.cs`:

```csharp
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests.Fakes;

/// <summary>
/// Ventanas de mentira que registran lo que se les hizo. Sirve para dos cosas:
/// verificar el orden de las operaciones, y simular apps rebeldes que se
/// vuelven a mover después de que las colocamos.
/// </summary>
public sealed class FakeWindowSystem : IWindowSystem
{
    public sealed class Window
    {
        public Rect Bounds { get; set; }
        public uint Style { get; set; }
        public ShowCommand ShowCmd { get; set; } = ShowCommand.Normal;
        public Rect NormalPosition { get; set; }

        /// <summary>
        /// Rect al que la app se mueve sola después de cada intento, simulando
        /// una app que pelea. Null significa que coopera.
        /// </summary>
        public Rect? FightsBackTo { get; set; }

        /// <summary>Cuántos intentos resiste antes de rendirse.</summary>
        public int FightsForAttempts { get; set; }
    }

    private readonly Dictionary<nint, Window> _windows = new();

    public List<string> Calls { get; } = new();

    public Window Add(nint handle, Rect bounds, uint style)
    {
        var w = new Window { Bounds = bounds, Style = style, NormalPosition = bounds };
        _windows[handle] = w;
        return w;
    }

    public Window this[nint handle] => _windows[handle];

    public bool IsWindow(nint handle) => _windows.ContainsKey(handle);

    public bool IsVisible(nint handle) => _windows.ContainsKey(handle);

    public Rect GetBounds(nint handle) => _windows[handle].Bounds;

    public uint GetStyle(nint handle) => _windows[handle].Style;

    public void SetStyle(nint handle, uint style)
    {
        Calls.Add($"SetStyle({handle},0x{style:X8})");
        _windows[handle].Style = style;
    }

    public void ApplyFrameChange(nint handle) => Calls.Add($"ApplyFrameChange({handle})");

    public void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition)
    {
        Calls.Add($"SetPlacement({handle},{showCmd},{normalPosition})");

        var w = _windows[handle];
        w.ShowCmd = showCmd;
        w.NormalPosition = normalPosition;
        w.Bounds = showCmd == ShowCommand.Normal ? normalPosition : w.Bounds;

        Settle(w);
    }

    public void Show(nint handle, ShowCommand showCmd)
    {
        Calls.Add($"Show({handle},{showCmd})");
        _windows[handle].ShowCmd = showCmd;
    }

    /// <summary>Deja que la ventana se rebele, si el test la configuró para eso.</summary>
    private static void Settle(Window w)
    {
        if (w.FightsBackTo is { } rebel && w.FightsForAttempts > 0)
        {
            w.FightsForAttempts--;
            w.Bounds = rebel;
        }
    }

    /// <summary>
    /// Fuerza los bounds observables y deja que la ventana se rebele, igual que
    /// hace SetPlacement. Es lo que usan los tests de retry como "intento".
    /// </summary>
    public void SetObservedBounds(nint handle, Rect bounds)
    {
        var w = _windows[handle];
        w.Bounds = bounds;
        Settle(w);
    }
}
```

- [ ] **Step 3: Escribir los tests de `StyleStore` que fallan**

`tests/MonSelect.Core.Tests/StyleStoreTests.cs`:

```csharp
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class StyleStoreTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("monselect-style");

    private string Path => System.IO.Path.Combine(_dir.FullName, "borderless.json");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Remembers_and_returns_an_original_style()
    {
        var store = new StyleStore(Path);
        store.Remember(new BorderlessRecord(1234, 23340, 638000000000, 0x00CF0000));

        Assert.True(store.TryGet(1234, out var found));
        Assert.Equal(0x00CF0000u, found.OriginalStyle);
    }

    [Fact]
    public void Forget_returns_the_record_and_removes_it()
    {
        var store = new StyleStore(Path);
        store.Remember(new BorderlessRecord(1234, 23340, 1, 0x00CF0000));

        var removed = store.Forget(1234);

        Assert.NotNull(removed);
        Assert.Equal(0x00CF0000u, removed!.OriginalStyle);
        Assert.False(store.TryGet(1234, out _));
    }

    [Fact]
    public void Forgetting_an_unknown_handle_returns_null()
    {
        Assert.Null(new StyleStore(Path).Forget(999));
    }

    [Fact]
    public void Remembering_the_same_handle_twice_keeps_the_first_style()
    {
        // La segunda vez el style ya está mutilado; guardarlo perdería el original.
        var store = new StyleStore(Path);
        store.Remember(new BorderlessRecord(1234, 1, 1, 0x00CF0000));
        store.Remember(new BorderlessRecord(1234, 1, 1, 0x000F0000));

        store.TryGet(1234, out var found);
        Assert.Equal(0x00CF0000u, found.OriginalStyle);
    }

    [Fact]
    public void Survives_a_save_and_reload()
    {
        var first = new StyleStore(Path);
        first.Remember(new BorderlessRecord(1234, 23340, 638000000000, 0x00CF0000));
        first.Save();

        var second = new StyleStore(Path);
        second.Load();

        Assert.True(second.TryGet(1234, out var found));
        Assert.Equal(23340u, found.ProcessId);
        Assert.Equal(638000000000, found.ProcessStartTicks);
    }

    [Fact]
    public void Loading_a_missing_file_yields_an_empty_store()
    {
        var store = new StyleStore(Path);
        store.Load();

        Assert.Empty(store.All());
    }

    [Fact]
    public void Loading_a_corrupt_file_yields_an_empty_store_instead_of_throwing()
    {
        File.WriteAllText(Path, "{ esto no es json");

        var store = new StyleStore(Path);
        store.Load();

        Assert.Empty(store.All());
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que fallan**

Run: `dotnet test --filter StyleStoreTests`
Expected: FAIL — `StyleStore` no existe.

- [ ] **Step 5: Implementar `StyleStore`**

`src/MonSelect.Core/Windows/StyleStore.cs`:

```csharp
using System.Text.Json;

namespace MonSelect.Core.Windows;

/// <param name="Handle">hwnd como long, para que serialice igual en 32 y 64 bits.</param>
/// <param name="ProcessStartTicks">
/// Desambigua un pid reciclado: si el proceso arrancó en otro momento, el
/// registro es de una ventana que ya no existe.
/// </param>
public sealed record BorderlessRecord(
    long Handle,
    uint ProcessId,
    long ProcessStartTicks,
    uint OriginalStyle);

/// <summary>
/// Recuerda el style original de las ventanas a las que se les quitó el marco.
/// Se persiste en disco porque, sin eso, un reinicio de MonSelect deja ventanas
/// sin barra de título que el usuario no puede restaurar.
/// </summary>
public sealed class StyleStore(string path)
{
    private readonly Dictionary<long, BorderlessRecord> _records = new();

    /// <summary>No pisa un registro existente: el segundo style ya está mutilado.</summary>
    public void Remember(BorderlessRecord record)
        => _records.TryAdd(record.Handle, record);

    public bool TryGet(long handle, out BorderlessRecord record)
        => _records.TryGetValue(handle, out record!);

    public BorderlessRecord? Forget(long handle)
    {
        if (!_records.Remove(handle, out var record))
            return null;

        return record;
    }

    public IReadOnlyCollection<BorderlessRecord> All() => _records.Values;

    public void Save()
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, JsonSerializer.Serialize(_records.Values));
    }

    /// <summary>
    /// Un archivo ilegible se descarta en silencio. Perder el historial de styles
    /// es molesto; no arrancar por eso sería peor.
    /// </summary>
    public void Load()
    {
        _records.Clear();

        if (!File.Exists(path))
            return;

        try
        {
            var loaded = JsonSerializer.Deserialize<List<BorderlessRecord>>(File.ReadAllText(path));
            foreach (var record in loaded ?? new List<BorderlessRecord>())
                _records[record.Handle] = record;
        }
        catch (JsonException)
        {
            _records.Clear();
        }
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test --filter StyleStoreTests`
Expected: PASS, 7 tests.

- [ ] **Step 7: Commit**

```bash
git add src/MonSelect.Core/Windows/IWindowSystem.cs src/MonSelect.Core/Windows/StyleStore.cs tests/MonSelect.Core.Tests
git commit -m "feat: add window system boundary and borderless style store"
```

---

## Task 10: WindowPlacer

**Files:**
- Create: `src/MonSelect.Core/Windows/WindowPlacer.cs`
- Test: `tests/MonSelect.Core.Tests/WindowPlacerTests.cs`

**Interfaces:**
- Consumes: `IWindowSystem`, `StyleStore`, `TargetPlacement`, `StyleMath`, `ShowCommand`.
- Produces: `sealed class WindowPlacer(IWindowSystem system, StyleStore styles)` con `void Apply(nint handle, uint processId, long processStartTicks, TargetPlacement target)` y `bool Revert(nint handle)`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/WindowPlacerTests.cs`:

```csharp
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class WindowPlacerTests : IDisposable
{
    private const nint Hwnd = 1234;
    private const uint Overlapped = 0x00CF0000; // caption + thickframe + botones

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("monselect-placer");
    private readonly FakeWindowSystem _system = new();
    private readonly StyleStore _styles;
    private readonly WindowPlacer _placer;

    public WindowPlacerTests()
    {
        _styles = new StyleStore(Path.Combine(_dir.FullName, "borderless.json"));
        _placer = new WindowPlacer(_system, _styles);
        _system.Add(Hwnd, Rect.FromLtrb(100, 100, 900, 700), Overlapped);
    }

    public void Dispose() => _dir.Delete(recursive: true);

    private static TargetPlacement Target(
        ShowCommand cmd, bool strip, Rect? normal = null, Rect? expected = null)
        => new(cmd,
               normal ?? Rect.FromLtrb(3100, 200, 3900, 800),
               strip,
               expected ?? Rect.FromLtrb(3000, 0, 4920, 1048));

    [Fact]
    public void Maximized_sets_placement_without_touching_the_style()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: false));

        Assert.Equal(Overlapped, _system[Hwnd].Style);
        Assert.Contains(_system.Calls, c => c.StartsWith("SetPlacement("));
        Assert.DoesNotContain(_system.Calls, c => c.StartsWith("SetStyle("));
    }

    [Fact]
    public void Borderless_strips_the_frame_before_setting_placement()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        var setStyle = _system.Calls.FindIndex(c => c.StartsWith("SetStyle("));
        var frame = _system.Calls.FindIndex(c => c.StartsWith("ApplyFrameChange("));
        var placement = _system.Calls.FindIndex(c => c.StartsWith("SetPlacement("));

        Assert.True(setStyle >= 0 && frame > setStyle && placement > frame,
            $"Orden incorrecto: {string.Join(" -> ", _system.Calls)}");
    }

    [Fact]
    public void Borderless_leaves_the_window_without_caption_or_thickframe()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        Assert.True(StyleMath.IsBorderless(_system[Hwnd].Style));
    }

    [Fact]
    public void Borderless_records_the_original_style_for_reverting()
    {
        _placer.Apply(Hwnd, 23340, 638000000000, Target(ShowCommand.Maximized, strip: true));

        Assert.True(_styles.TryGet(Hwnd, out var record));
        Assert.Equal(Overlapped, record.OriginalStyle);
        Assert.Equal(23340u, record.ProcessId);
    }

    [Fact]
    public void Applying_borderless_twice_keeps_the_first_original_style()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        _styles.TryGet(Hwnd, out var record);
        Assert.Equal(Overlapped, record.OriginalStyle);
    }

    [Fact]
    public void Revert_restores_the_original_style_and_clears_the_record()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Maximized, strip: true));

        Assert.True(_placer.Revert(Hwnd));
        Assert.Equal(Overlapped, _system[Hwnd].Style);
        Assert.False(_styles.TryGet(Hwnd, out _));
    }

    [Fact]
    public void Revert_on_an_untouched_window_reports_that_it_did_nothing()
    {
        Assert.False(_placer.Revert(Hwnd));
    }

    [Fact]
    public void Minimized_moves_the_window_before_minimising_it()
    {
        _placer.Apply(Hwnd, 1, 1, Target(ShowCommand.Minimized, strip: false));

        // El rect de restauración tiene que quedar seteado, así al restaurarla
        // aparece en el monitor de la regla y no donde estaba antes.
        Assert.Equal(Rect.FromLtrb(3100, 200, 3900, 800), _system[Hwnd].NormalPosition);
        Assert.Equal(ShowCommand.Minimized, _system[Hwnd].ShowCmd);
    }

    [Fact]
    public void Applying_to_a_dead_handle_does_nothing_instead_of_throwing()
    {
        _placer.Apply(9999, 1, 1, Target(ShowCommand.Maximized, strip: false));

        Assert.Empty(_system.Calls);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test --filter WindowPlacerTests`
Expected: FAIL — `WindowPlacer` no existe.

- [ ] **Step 3: Implementar `WindowPlacer`**

`src/MonSelect.Core/Windows/WindowPlacer.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Aplica un <see cref="TargetPlacement"/> a una ventana concreta. No decide
/// nada: el qué lo calculó <see cref="PlacementCalculator"/>, acá sólo está el cómo.
/// </summary>
public sealed class WindowPlacer(IWindowSystem system, StyleStore styles)
{
    public void Apply(nint handle, uint processId, long processStartTicks, TargetPlacement target)
    {
        if (!system.IsWindow(handle))
            return;

        if (target.StripBorders)
            StripFrame(handle, processId, processStartTicks);

        // SetWindowPlacement fija showCmd y rcNormalPosition juntos. Hacerlo en
        // dos pasos haría que la ventana aparezca en el monitor viejo y salte.
        system.SetPlacement(handle, target.ShowCmd, target.NormalPosition);
    }

    /// <summary>Devuelve false si la ventana nunca fue convertida a borderless.</summary>
    public bool Revert(nint handle)
    {
        var record = styles.Forget(handle);
        if (record is null)
            return false;

        if (!system.IsWindow(handle))
            return false;

        system.SetStyle(handle, record.OriginalStyle);
        system.ApplyFrameChange(handle);
        return true;
    }

    private void StripFrame(nint handle, uint processId, long processStartTicks)
    {
        var current = system.GetStyle(handle);

        // Sólo se guarda si todavía tiene marco. Si ya es borderless, el style
        // actual no es el original y guardarlo perdería la posibilidad de revertir.
        if (!StyleMath.IsBorderless(current))
        {
            styles.Remember(new BorderlessRecord(handle, processId, processStartTicks, current));
            styles.Save();
        }

        system.SetStyle(handle, StyleMath.StripBorders(current));

        // Sin SWP_FRAMECHANGED el cambio de style no se refleja: la ventana
        // conserva el área no cliente hasta el próximo recálculo.
        system.ApplyFrameChange(handle);
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test --filter WindowPlacerTests`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/MonSelect.Core/Windows/WindowPlacer.cs tests/MonSelect.Core.Tests/WindowPlacerTests.cs
git commit -m "feat: apply target placement with reversible borderless"
```

---

## Task 11: RetryScheduler

**Files:**
- Create: `src/MonSelect.Core/Engine/RetryScheduler.cs`
- Test: `tests/MonSelect.Core.Tests/RetrySchedulerTests.cs`

**Interfaces:**
- Consumes: `IWindowSystem`, `Rect`.
- Produces:
  - `interface IDelay { Task WaitAsync(int milliseconds, CancellationToken ct); }` y `sealed class RealDelay : IDelay`.
  - `sealed record RetryOutcome(bool Settled, int Attempts, IReadOnlyList<Rect> Observed)`.
  - `sealed class RetryScheduler(IWindowSystem system, IDelay delay)` con `Task<RetryOutcome> RunAsync(nint handle, IReadOnlyList<int> scheduleMs, Rect expectedBounds, Action attempt, CancellationToken ct)`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/RetrySchedulerTests.cs`:

```csharp
using MonSelect.Core.Engine;
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Tests;

public class RetrySchedulerTests
{
    private const nint Hwnd = 1234;
    private static readonly Rect Wanted = Rect.FromLtrb(3000, 0, 4920, 1048);
    private static readonly Rect Elsewhere = Rect.FromLtrb(100, 100, 900, 700);
    private static readonly int[] Schedule = { 0, 150, 400, 800 };

    /// <summary>Reloj de mentira: no espera, sólo anota cuánto le pidieron esperar.</summary>
    private sealed class FakeDelay : IDelay
    {
        public List<int> Waits { get; } = new();

        public Task WaitAsync(int milliseconds, CancellationToken ct)
        {
            Waits.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Stops_after_one_attempt_when_the_app_cooperates()
    {
        var system = new FakeWindowSystem();
        system.Add(Hwnd, Elsewhere, 0);
        var delay = new FakeDelay();
        var scheduler = new RetryScheduler(system, delay);

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, Wanted),
            CancellationToken.None);

        Assert.True(result.Settled);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Retries_until_a_stubborn_app_gives_up()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 1; // resiste el primer intento

        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, Wanted),
            CancellationToken.None);

        // Lecturas: [Elsewhere, Wanted, Wanted]. Recién en la tercera hay dos
        // lecturas consecutivas iguales y en el objetivo, que es la condición
        // de corte. Con un solo forcejeo hacen falta tres intentos, no dos.
        Assert.True(result.Settled);
        Assert.Equal(3, result.Attempts);
    }

    [Fact]
    public async Task Gives_up_after_the_budget_and_reports_what_it_saw()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99; // nunca cede

        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => system.SetObservedBounds(Hwnd, Wanted),
            CancellationToken.None);

        Assert.False(result.Settled);
        Assert.Equal(Schedule.Length, result.Attempts);
        Assert.Equal(Schedule.Length, result.Observed.Count);
        Assert.All(result.Observed, r => Assert.Equal(Elsewhere, r));
    }

    [Fact]
    public async Task Honours_the_configured_schedule()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99;
        var delay = new FakeDelay();

        await new RetryScheduler(system, delay).RunAsync(
            Hwnd, new[] { 0, 250 }, Wanted, attempt: () => { }, CancellationToken.None);

        Assert.Equal(new[] { 0, 250 }, delay.Waits);
    }

    [Fact]
    public async Task An_empty_expected_rect_means_a_single_attempt()
    {
        // Es el caso de Minimized: no hay bounds observables que comparar.
        var system = new FakeWindowSystem();
        system.Add(Hwnd, Elsewhere, 0);
        var delay = new FakeDelay();

        var result = await new RetryScheduler(system, delay).RunAsync(
            Hwnd, Schedule, Rect.FromLtrb(0, 0, 0, 0), attempt: () => { }, CancellationToken.None);

        Assert.True(result.Settled);
        Assert.Equal(1, result.Attempts);
    }

    [Fact]
    public async Task Stops_when_the_window_disappears_mid_retry()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99;

        var scheduler = new RetryScheduler(system, new FakeDelay());

        var result = await scheduler.RunAsync(
            9999, Schedule, Wanted, attempt: () => { }, CancellationToken.None);

        Assert.False(result.Settled);
        Assert.Equal(0, result.Attempts);
    }

    [Fact]
    public async Task Cancellation_stops_the_retry_loop()
    {
        var system = new FakeWindowSystem();
        var window = system.Add(Hwnd, Elsewhere, 0);
        window.FightsBackTo = Elsewhere;
        window.FightsForAttempts = 99;
        using var cts = new CancellationTokenSource();

        var result = await new RetryScheduler(system, new FakeDelay()).RunAsync(
            Hwnd, Schedule, Wanted,
            attempt: () => cts.Cancel(),
            cts.Token);

        Assert.False(result.Settled);
        Assert.Equal(1, result.Attempts);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test --filter RetrySchedulerTests`
Expected: FAIL — `RetryScheduler` no existe.

- [ ] **Step 3: Implementar `RetryScheduler`**

`src/MonSelect.Core/Engine/RetryScheduler.cs`:

```csharp
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Engine;

/// <summary>Espera inyectable, para que los tests no tarden segundos reales.</summary>
public interface IDelay
{
    Task WaitAsync(int milliseconds, CancellationToken ct);
}

public sealed class RealDelay : IDelay
{
    public Task WaitAsync(int milliseconds, CancellationToken ct)
        => milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, ct);
}

/// <param name="Settled">True si la ventana terminó donde se quería.</param>
/// <param name="Observed">Bounds leídos después de cada intento, para el log.</param>
public sealed record RetryOutcome(bool Settled, int Attempts, IReadOnlyList<Rect> Observed);

/// <summary>
/// Reaplica una colocación hasta que la ventana se queda quieta donde
/// corresponde. Existe porque muchas apps (Electron y Qt sobre todo) se
/// reposicionan solas después de mostrarse: un único intento falla en silencio.
/// </summary>
public sealed class RetryScheduler(IWindowSystem system, IDelay delay)
{
    public async Task<RetryOutcome> RunAsync(
        nint handle,
        IReadOnlyList<int> scheduleMs,
        Rect expectedBounds,
        Action attempt,
        CancellationToken ct)
    {
        var observed = new List<Rect>();

        if (!system.IsWindow(handle))
            return new RetryOutcome(false, 0, observed);

        // Minimizada no tiene bounds observables: se aplica una vez y listo.
        var comparable = !expectedBounds.IsEmpty;

        for (var i = 0; i < scheduleMs.Count; i++)
        {
            await delay.WaitAsync(scheduleMs[i], ct).ConfigureAwait(false);

            if (!system.IsWindow(handle))
                return new RetryOutcome(false, i, observed);

            attempt();

            if (!comparable)
                return new RetryOutcome(true, i + 1, observed);

            var actual = system.GetBounds(handle);
            observed.Add(actual);

            if (ct.IsCancellationRequested)
                return new RetryOutcome(false, i + 1, observed);

            // Se corta cuando el resultado es el buscado y además es estable:
            // dos lecturas seguidas iguales significan que la app dejó de pelear.
            var onTarget = actual == expectedBounds;
            var stable = observed.Count >= 2 && observed[^1] == observed[^2];

            if (onTarget && (stable || observed.Count == 1))
                return new RetryOutcome(true, i + 1, observed);
        }

        return new RetryOutcome(false, scheduleMs.Count, observed);
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test --filter RetrySchedulerTests`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/MonSelect.Core/Engine/RetryScheduler.cs tests/MonSelect.Core.Tests/RetrySchedulerTests.cs
git commit -m "feat: add retry scheduler for windows that reposition themselves"
```

---

## Task 12: Win32WindowSystem y WindowProbe

**Files:**
- Create: `src/MonSelect.Core/Win32/ProcessQuery.cs`
- Create: `src/MonSelect.Core/Windows/Win32WindowSystem.cs`
- Create: `src/MonSelect.Core/Windows/WindowProbe.cs`
- Modify: `tools/probe/Program.cs`

**Interfaces:**
- Consumes: `NativeMethods`, `IWindowSystem`, `WindowInfo`, `IMonitorSystem`.
- Produces:
  - `static class ProcessQuery` con `string? GetExePath(uint pid)`, `string? GetCommandLine(uint pid)`, `long GetStartTicks(uint pid)`.
  - `sealed class Win32WindowSystem : IWindowSystem`.
  - `sealed class WindowProbe(IWindowSystem system)` con `WindowInfo? Describe(nint handle)`.

**Testing:** sin tests unitarios. Leer el PEB de otro proceso no se puede fakear sin reimplementar el kernel. La verificación es el paso 5, que compara la salida contra la medición del spec.

- [ ] **Step 1: Implementar la consulta de procesos**

`src/MonSelect.Core/Win32/ProcessQuery.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MonSelect.Core.Win32;

/// <summary>
/// Datos del proceso dueño de una ventana. El command line se lee del PEB en
/// vez de con WMI porque Win32_Process cuesta entre 50 y 200 ms por consulta, y
/// esto corre en el camino crítico de una ventana que acaba de aparecer.
/// </summary>
public static class ProcessQuery
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint Reserved1;
        public nint PebBaseAddress;
        public nint Reserved2_0;
        public nint Reserved2_1;
        public nuint UniqueProcessId;
        public nint Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(
        nint process, uint flags, StringBuilder buffer, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        nint process, nint address, nint buffer, nint size, out nint read);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        nint process, int infoClass, ref ProcessBasicInformation info, int length, out int returned);

    public static string? GetExePath(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
            return null;

        try
        {
            var size = 1024u;
            var buffer = new StringBuilder((int)size);
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static long GetStartTicks(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.StartTime.Ticks;
        }
        catch (Exception)
        {
            // Proceso muerto, elevado, o de otro usuario. Cero es un valor válido
            // de "no sé": sólo se usa para desambiguar pids reciclados.
            return 0;
        }
    }

    /// <summary>
    /// Devuelve null cuando el proceso es elevado o de otro usuario. Eso no es
    /// un error: la regla que dependa del command line simplemente no matchea.
    /// </summary>
    public static unsafe string? GetCommandLine(uint pid)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
        if (handle == 0)
            return null;

        try
        {
            var info = new ProcessBasicInformation();
            if (NtQueryInformationProcess(handle, 0, ref info, Marshal.SizeOf(info), out _) != 0)
                return null;
            if (info.PebBaseAddress == 0)
                return null;

            // PEB.ProcessParameters está en el offset 0x20 en x64.
            nint parameters;
            if (!ReadProcessMemory(handle, info.PebBaseAddress + 0x20,
                    (nint)(&parameters), sizeof(nint), out _))
                return null;

            // RTL_USER_PROCESS_PARAMETERS.CommandLine está en 0x70 en x64.
            UnicodeString commandLine;
            if (!ReadProcessMemory(handle, parameters + 0x70,
                    (nint)(&commandLine), Marshal.SizeOf<UnicodeString>(), out _))
                return null;

            if (commandLine.Length == 0 || commandLine.Buffer == 0)
                return null;

            var bytes = Marshal.AllocHGlobal(commandLine.Length);
            try
            {
                if (!ReadProcessMemory(handle, commandLine.Buffer, bytes, commandLine.Length, out _))
                    return null;

                return Marshal.PtrToStringUni(bytes, commandLine.Length / 2);
            }
            finally
            {
                Marshal.FreeHGlobal(bytes);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
```

- [ ] **Step 2: Implementar `Win32WindowSystem`**

`src/MonSelect.Core/Windows/Win32WindowSystem.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

public sealed class Win32WindowSystem : IWindowSystem
{
    private const uint FrameChangeFlags =
        (uint)(SetWindowPosFlags.NoMove | SetWindowPosFlags.NoSize
               | SetWindowPosFlags.NoZOrder | SetWindowPosFlags.NoActivate
               | SetWindowPosFlags.FrameChanged);

    public bool IsWindow(nint handle) => NativeMethods.IsWindow(handle);

    public bool IsVisible(nint handle) => NativeMethods.IsWindowVisible(handle);

    public Rect GetBounds(nint handle)
        => NativeMethods.GetWindowRect(handle, out var rect) ? rect : default;

    public uint GetStyle(nint handle)
        => (uint)NativeMethods.GetWindowLongPtr(handle, GwlIndex.Style).ToInt64();

    public void SetStyle(nint handle, uint style)
        => NativeMethods.SetWindowLongPtr(handle, GwlIndex.Style, (nint)style);

    public void ApplyFrameChange(nint handle)
        => NativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0, FrameChangeFlags);

    public void SetPlacement(nint handle, ShowCommand showCmd, Rect normalPosition)
    {
        var placement = WindowPlacement.Create();
        if (!NativeMethods.GetWindowPlacement(handle, ref placement))
            return;

        placement.showCmd = (int)showCmd;
        placement.rcNormalPosition = normalPosition;

        NativeMethods.SetWindowPlacement(handle, ref placement);
    }

    public void Show(nint handle, ShowCommand showCmd)
        => NativeMethods.ShowWindow(handle, (int)showCmd);
}
```

- [ ] **Step 3: Implementar `WindowProbe`**

`src/MonSelect.Core/Windows/WindowProbe.cs`:

```csharp
using MonSelect.Core.Win32;

namespace MonSelect.Core.Windows;

/// <summary>
/// Construye el <see cref="WindowInfo"/> de un hwnd. Cachea exe path y command
/// line por pid: no cambian mientras el proceso viva, y leer el PEB en cada
/// evento sería caro.
/// </summary>
public sealed class WindowProbe(IWindowSystem system)
{
    private readonly Dictionary<uint, (string? Exe, string? CommandLine, long StartTicks)> _byPid = new();

    public WindowInfo? Describe(nint handle)
    {
        if (!system.IsWindow(handle))
            return null;

        NativeMethods.GetWindowThreadProcessId(handle, out var pid);

        var (exe, commandLine, startTicks) = ProcessFacts(pid);
        var style = system.GetStyle(handle);
        var bounds = system.GetBounds(handle);

        return new WindowInfo(
            handle,
            pid,
            exe,
            commandLine,
            ClassName(handle),
            Title(handle),
            Aumid: null, // F1 no lee AppUserModelID; se agrega con el soporte de apps de Store.
            bounds,
            CurrentState(style));
    }

    public long StartTicksOf(uint pid) => ProcessFacts(pid).StartTicks;

    private (string? Exe, string? CommandLine, long StartTicks) ProcessFacts(uint pid)
    {
        if (_byPid.TryGetValue(pid, out var cached))
            return cached;

        var facts = (
            ProcessQuery.GetExePath(pid),
            ProcessQuery.GetCommandLine(pid),
            ProcessQuery.GetStartTicks(pid));

        _byPid[pid] = facts;
        return facts;
    }

    /// <summary>Olvida el cache de un proceso que murió, para no envenenar un pid reciclado.</summary>
    public void ForgetProcess(uint pid) => _byPid.Remove(pid);

    private static WindowState CurrentState(uint style)
    {
        if ((style & (uint)WindowStyles.Minimize) != 0)
            return WindowState.Minimized;

        if ((style & (uint)WindowStyles.Maximize) != 0)
            return StyleMath.IsBorderless(style) ? WindowState.Borderless : WindowState.Maximized;

        return WindowState.Normal;
    }

    private static string ClassName(nint handle)
    {
        var buffer = new char[256];
        var length = NativeMethods.GetClassNameW(handle, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string Title(nint handle)
    {
        var length = NativeMethods.GetWindowTextLengthW(handle);
        if (length <= 0)
            return string.Empty;

        var buffer = new char[length + 1];
        var written = NativeMethods.GetWindowTextW(handle, buffer, buffer.Length);
        return written > 0 ? new string(buffer, 0, written) : string.Empty;
    }
}
```

- [ ] **Step 4: Agregar el volcado de ventanas al probe**

Añadir a `tools/probe/Program.cs`, **antes** de la declaración de `GetForegroundWindow` que agregó la Task 7** (misma función local, no redeclararla):

```csharp
if (args.Contains("--windows"))
{
    var probe = new WindowProbe(new Win32WindowSystem());

    Console.WriteLine("Poné el foco en la ventana que querés inspeccionar. Tenés 5 segundos.");
    Thread.Sleep(5000);

    var info = probe.Describe(GetForegroundWindow());
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
```

- [ ] **Step 5: Verificar contra la medición del spec**

Con RustDesk abierto y conectado, correr:

Run: `dotnet run --project tools/probe -- --windows`

Comprobar contra la sección 3.3 del spec:

1. `exe` es el path completo a `rustdesk.exe`, no vacío.
2. **`cmdline` incluye `--connect` y su número.** Si dice `<sin acceso>`, la lectura del PEB falló y el matching por command line no va a funcionar: hay que arreglarlo antes de seguir.
3. `class` es `RustdeskMultiWindow`.
4. `title` incluye el nombre de la máquina remota.
5. `state` es `Borderless`, porque RustDesk en pantalla completa no tiene caption ni thickframe pero sí `WS_MAXIMIZE`.

Repetir con el Bloc de notas en estado normal y verificar que `state` sea `Normal`, y maximizado que sea `Maximized`.

- [ ] **Step 6: Commit**

```bash
git add src/MonSelect.Core/Win32/ProcessQuery.cs src/MonSelect.Core/Windows/Win32WindowSystem.cs src/MonSelect.Core/Windows/WindowProbe.cs tools/probe/Program.cs
git commit -m "feat: read window and process facts from the live system"
```

---

## Task 13: WindowWatcher

**Files:**
- Create: `src/MonSelect.Core/Engine/WindowWatcher.cs`
- Modify: `tools/probe/Program.cs`

**Interfaces:**
- Consumes: `NativeMethods`.
- Produces: `sealed class WindowWatcher : IDisposable` con el evento `Action<nint>? WindowAppeared`, y los métodos `void Start()` y `void Post(Action work)`. El hilo que crea es el dueño único de las mutaciones de ventanas: todo trabajo se encola con `Post`.

**Testing:** sin tests unitarios. Un hook global de Windows no se puede fakear de forma útil. La verificación es el paso 3.

- [ ] **Step 1: Agregar los P/Invoke del hook**

Añadir a `src/MonSelect.Core/Win32/NativeMethods.cs`:

```csharp
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    internal delegate void WinEventProc(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint min, uint max, nint module, WinEventProc callback, uint pid, uint thread, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint hook);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public Point pt;
    }

    [DllImport("user32.dll")]
    internal static extern int GetMessageW(out Msg msg, nint hwnd, uint min, uint max);

    [DllImport("user32.dll")]
    internal static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    internal static extern nint DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    internal static extern bool PostThreadMessageW(uint thread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
```

- [ ] **Step 2: Implementar `WindowWatcher`**

`src/MonSelect.Core/Engine/WindowWatcher.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MonSelect.Core.Win32;

namespace MonSelect.Core.Engine;

/// <summary>
/// Dueño del hook y del hilo que muta ventanas. Un solo hilo con message pump
/// recibe los eventos y ejecuta las colocaciones: SetWindowPos desde varios
/// hilos contra la misma ventana da resultados dependientes del orden, y los
/// reintentos competirían entre sí.
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private const uint WM_RUN_WORK = 0x0400 + 1; // WM_APP + 1
    private const uint WM_QUIT_PUMP = 0x0400 + 2;

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private uint _threadId;
    private nint _hook;

    // El delegate se guarda en un campo para que el GC no lo mueva ni lo
    // recolecte: si eso pasa, el hook muere con una violación de acceso que no
    // deja rastro útil.
    private NativeMethods.WinEventProc? _callback;
    private GCHandle _callbackHandle;

    /// <summary>Se dispara en el hilo dueño, con el hwnd de la ventana que apareció.</summary>
    public event Action<nint>? WindowAppeared;

    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("El watcher ya está corriendo.");

        _thread = new Thread(Pump) { IsBackground = true, Name = "MonSelect.WindowWatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    /// <summary>Encola trabajo para que corra en el hilo dueño de las ventanas.</summary>
    public void Post(Action work)
    {
        _queue.Enqueue(work);
        if (_threadId != 0)
            NativeMethods.PostThreadMessageW(_threadId, WM_RUN_WORK, 0, 0);
    }

    private void Pump()
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        _callback = OnWinEvent;
        _callbackHandle = GCHandle.Alloc(_callback);

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_OBJECT_SHOW,
            0,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        _ready.Set();

        while (NativeMethods.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            if (msg.message == WM_QUIT_PUMP)
                break;

            if (msg.message == WM_RUN_WORK)
            {
                while (_queue.TryDequeue(out var work))
                    RunSafely(work);
                continue;
            }

            NativeMethods.TranslateMessage(ref msg);
            NativeMethods.DispatchMessageW(ref msg);
        }

        if (_hook != 0)
            NativeMethods.UnhookWinEvent(_hook);
    }

    private void OnWinEvent(
        nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // Sólo interesa la ventana en sí, no sus controles hijos.
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF)
            return;

        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            return;

        RunSafely(() => WindowAppeared?.Invoke(hwnd));
    }

    /// <summary>
    /// Una excepción que escape al callback del hook mata el pump y con él todo
    /// MonSelect, sin dejar rastro. Se traga acá y se registra.
    /// </summary>
    private static void RunSafely(Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[watcher] excepción no manejada: {ex}");
        }
    }

    public void Dispose()
    {
        if (_threadId != 0)
            NativeMethods.PostThreadMessageW(_threadId, WM_QUIT_PUMP, 0, 0);

        _thread?.Join(TimeSpan.FromSeconds(2));

        if (_callbackHandle.IsAllocated)
            _callbackHandle.Free();

        _ready.Dispose();
    }
}
```

- [ ] **Step 3: Verificar que el hook ve ventanas reales**

Añadir a `tools/probe/Program.cs`:

```csharp
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
}
```

Run: `dotnet run --project tools/probe -- --watch`

Abrir el Bloc de notas, la Calculadora y una ventana del Explorador. Comprobar que cada una aparece en la salida con su título, su exe y su clase. Si no aparece nada, el hook no está enganchado y no tiene sentido seguir.

- [ ] **Step 4: Commit**

```bash
git add src/MonSelect.Core/Engine/WindowWatcher.cs src/MonSelect.Core/Win32/NativeMethods.cs tools/probe/Program.cs
git commit -m "feat: watch for appearing windows on a dedicated pump thread"
```

---

## Task 14: RuleEngine y ApplyLog

**Files:**
- Create: `src/MonSelect.Core/Engine/ApplyLog.cs`
- Create: `src/MonSelect.Core/Engine/RuleEngine.cs`
- Modify: `src/MonSelect.Core/Windows/WindowProbe.cs` (extraer `IWindowDescriber`)
- Test: `tests/MonSelect.Core.Tests/RuleEngineTests.cs`

**Interfaces:**
- Consumes: todo lo anterior.
- Produces:
  - `enum ApplyResult { NoMatch, Skipped, Applied, Resisted, Ignored }`.
  - `sealed record ApplyEntry(DateTimeOffset At, nint Handle, string Title, string? RuleName, ApplyResult Result, int Attempts, string? Detail)`.
  - `sealed class ApplyLog(int capacity = 200)` con `void Add(ApplyEntry)` y `IReadOnlyList<ApplyEntry> Recent()`.
  - `sealed class RuleEngine` con `Task<ApplyResult> HandleAsync(nint handle, CancellationToken ct)`, `void UpdateRules(RuleSet set)` y `Task ApplyAllAsync(IEnumerable<nint> handles, CancellationToken ct)`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/MonSelect.Core.Tests/RuleEngineTests.cs`:

```csharp
using MonSelect.Core.Engine;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Tests.Fakes;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class RuleEngineTests : IDisposable
{
    private const string Exe = @"C:\Program Files\RustDesk\rustdesk.exe";

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("monselect-engine");
    private readonly FakeWindowSystem _windows = new();
    private readonly FakeMonitorSystem _monitors = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private sealed class NoDelay : IDelay
    {
        public Task WaitAsync(int milliseconds, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>Probe de mentira: devuelve el WindowInfo que el test decida.</summary>
    private sealed class StubProbe(Dictionary<nint, WindowInfo> windows) : IWindowDescriber
    {
        public WindowInfo? Describe(nint handle)
            => windows.TryGetValue(handle, out var info) ? info : null;

        public long StartTicksOf(uint pid) => 1;
    }

    private static RuleSet SetWith(params Rule[] rules) => new(
        1,
        new Dictionary<string, MonitorAlias>(StringComparer.OrdinalIgnoreCase)
        {
            ["benq"] = new(FakeMonitorSystem.Right.Id.DevicePath, "BenQ"),
            ["vertical"] = new(FakeMonitorSystem.Vertical.Id.DevicePath, "LG"),
            ["fantasma"] = new(FakeMonitorSystem.Disconnected.Id.DevicePath, "No conectado"),
        },
        rules);

    private (RuleEngine Engine, ApplyLog Log) Build(
        RuleSet set, Dictionary<nint, WindowInfo> described)
    {
        var styles = new StyleStore(Path.Combine(_dir.FullName, "borderless.json"));
        var log = new ApplyLog();
        var engine = new RuleEngine(
            new StubProbe(described),
            new MonitorRegistry(_monitors),
            new WindowPlacer(_windows, styles),
            new RetryScheduler(_windows, new NoDelay()),
            log);

        engine.UpdateRules(set);
        return (engine, log);
    }

    private WindowInfo Describe(nint handle, string title = "RustDesk")
        => new(handle, 100, Exe, "--connect 1", "RustdeskMultiWindow", title, null,
               Rect.FromLtrb(100, 100, 900, 700), WindowState.Normal);

    private static Rule MakeRule(
        string name,
        WindowState state = WindowState.Maximized,
        ApplyMode apply = ApplyMode.All,
        IfMissing ifMissing = IfMissing.Skip,
        IReadOnlyList<string>? monitors = null)
        => new(name,
               new MatchCriteria(Exe: Exe),
               new RulePlacement(monitors ?? new[] { "benq" }, state),
               true,
               apply,
               ifMissing,
               new[] { 0 });

    [Fact]
    public async Task A_window_with_no_matching_rule_is_left_alone()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        var described = new Dictionary<nint, WindowInfo>
        {
            [1] = Describe(1) with { ExePath = @"C:\Windows\notepad.exe" },
        };
        var (engine, _) = Build(SetWith(MakeRule("rustdesk")), described);

        Assert.Equal(ApplyResult.NoMatch, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Empty(_windows.Calls);
    }

    [Fact]
    public async Task A_matching_rule_places_the_window_and_logs_it()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        _windows.SetObservedBounds(1, FakeMonitorSystem.Right.WorkArea);
        var (engine, log) = Build(
            SetWith(MakeRule("rustdesk")),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        var result = await engine.HandleAsync(1, CancellationToken.None);

        Assert.Equal(ApplyResult.Applied, result);
        Assert.Contains(log.Recent(), e => e.RuleName == "rustdesk" && e.Result == ApplyResult.Applied);
    }

    [Fact]
    public async Task A_missing_monitor_with_skip_does_not_place_the_window()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        var (engine, log) = Build(
            SetWith(MakeRule("fantasma", monitors: new[] { "fantasma" })),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Skipped, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Empty(_windows.Calls);
        Assert.Contains(log.Recent(), e => e.Result == ApplyResult.Skipped);
    }

    [Fact]
    public async Task A_missing_monitor_with_primary_falls_back()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        _windows.SetObservedBounds(1, FakeMonitorSystem.Primary.WorkArea);
        var (engine, _) = Build(
            SetWith(MakeRule("fantasma", ifMissing: IfMissing.Primary, monitors: new[] { "fantasma" })),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Applied, await engine.HandleAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task An_alias_that_is_not_declared_is_skipped_and_logged()
    {
        _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        var (engine, log) = Build(
            SetWith(MakeRule("typo", monitors: new[] { "beqn" })),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Skipped, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Contains(log.Recent(), e => e.Detail is not null && e.Detail.Contains("beqn"));
    }

    [Fact]
    public async Task Apply_all_places_every_matching_window()
    {
        foreach (nint h in new nint[] { 1, 2, 3 })
        {
            _windows.Add(h, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
            _windows.SetObservedBounds(h, FakeMonitorSystem.Right.WorkArea);
        }

        var described = new Dictionary<nint, WindowInfo>
        {
            [1] = Describe(1), [2] = Describe(2), [3] = Describe(3),
        };
        var (engine, _) = Build(SetWith(MakeRule("todas")), described);

        foreach (nint h in new nint[] { 1, 2, 3 })
            Assert.Equal(ApplyResult.Applied, await engine.HandleAsync(h, CancellationToken.None));
    }

    [Fact]
    public async Task Apply_first_only_places_the_first_window_of_a_process()
    {
        foreach (nint h in new nint[] { 1, 2 })
        {
            _windows.Add(h, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
            _windows.SetObservedBounds(h, FakeMonitorSystem.Right.WorkArea);
        }

        var described = new Dictionary<nint, WindowInfo> { [1] = Describe(1), [2] = Describe(2) };
        var (engine, _) = Build(SetWith(MakeRule("primera", apply: ApplyMode.First)), described);

        Assert.Equal(ApplyResult.Applied, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Equal(ApplyResult.Ignored, await engine.HandleAsync(2, CancellationToken.None));
    }

    [Fact]
    public async Task Apply_rotate_cycles_through_the_monitor_list()
    {
        foreach (nint h in new nint[] { 1, 2, 3 })
            _windows.Add(h, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);

        var described = new Dictionary<nint, WindowInfo>
        {
            [1] = Describe(1), [2] = Describe(2), [3] = Describe(3),
        };
        var (engine, log) = Build(
            SetWith(MakeRule("rotando", apply: ApplyMode.Rotate, monitors: new[] { "benq", "vertical" })),
            described);

        foreach (nint h in new nint[] { 1, 2, 3 })
            await engine.HandleAsync(h, CancellationToken.None);

        var details = log.Recent().Select(e => e.Detail ?? "").ToList();
        Assert.Contains(details, d => d.Contains("DISPLAY4"));
        Assert.Contains(details, d => d.Contains("DISPLAY3"));
        // La tercera ventana recicla al primer monitor de la lista.
        Assert.Equal(2, details.Count(d => d.Contains("DISPLAY4")));
    }

    [Fact]
    public async Task A_window_that_never_settles_is_logged_as_resisted()
    {
        var window = _windows.Add(1, Rect.FromLtrb(100, 100, 900, 700), 0x00CF0000);
        window.FightsBackTo = Rect.FromLtrb(100, 100, 900, 700);
        window.FightsForAttempts = 99;

        var (engine, log) = Build(
            SetWith(MakeRule("rebelde") with { RetryMs = new[] { 0, 0 } }),
            new Dictionary<nint, WindowInfo> { [1] = Describe(1) });

        Assert.Equal(ApplyResult.Resisted, await engine.HandleAsync(1, CancellationToken.None));
        Assert.Contains(log.Recent(), e => e.Result == ApplyResult.Resisted && e.Attempts == 2);
    }

    [Fact]
    public void The_log_keeps_only_the_most_recent_entries()
    {
        var log = new ApplyLog(capacity: 3);
        for (var i = 0; i < 10; i++)
            log.Add(new ApplyEntry(DateTimeOffset.Now, i, $"w{i}", null, ApplyResult.NoMatch, 0, null));

        Assert.Equal(3, log.Recent().Count);
        Assert.Equal("w9", log.Recent()[^1].Title);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test --filter RuleEngineTests`
Expected: FAIL — `RuleEngine`, `ApplyLog` e `IWindowDescriber` no existen.

- [ ] **Step 3: Extraer `IWindowDescriber`**

Añadir a `src/MonSelect.Core/Windows/WindowProbe.cs` y hacer que `WindowProbe` la implemente:

```csharp
/// <summary>Lo que el motor necesita saber de una ventana. Existe para poder stubbearlo.</summary>
public interface IWindowDescriber
{
    WindowInfo? Describe(nint handle);
    long StartTicksOf(uint pid);
}
```

Cambiar la declaración a `public sealed class WindowProbe(IWindowSystem system) : IWindowDescriber`.

- [ ] **Step 4: Implementar `ApplyLog`**

`src/MonSelect.Core/Engine/ApplyLog.cs`:

```csharp
namespace MonSelect.Core.Engine;

public enum ApplyResult
{
    /// <summary>Ninguna regla matcheó. La ventana queda donde Windows la puso.</summary>
    NoMatch,

    /// <summary>Matcheó, pero el monitor no está y la política dijo que no la toque.</summary>
    Skipped,

    /// <summary>Colocada donde correspondía.</summary>
    Applied,

    /// <summary>Se agotó el presupuesto de reintentos y la ventana se quedó en otro lado.</summary>
    Resisted,

    /// <summary>Matcheó pero el modo apply decidió no tocarla, como First con una segunda ventana.</summary>
    Ignored,
}

public sealed record ApplyEntry(
    DateTimeOffset At,
    nint Handle,
    string Title,
    string? RuleName,
    ApplyResult Result,
    int Attempts,
    string? Detail);

/// <summary>Buffer circular de las últimas aplicaciones. Es lo que se ve en la GUI de F2.</summary>
public sealed class ApplyLog(int capacity = 200)
{
    private readonly Queue<ApplyEntry> _entries = new();
    private readonly Lock _gate = new();

    public void Add(ApplyEntry entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > capacity)
                _entries.Dequeue();
        }
    }

    public IReadOnlyList<ApplyEntry> Recent()
    {
        lock (_gate)
            return _entries.ToArray();
    }
}
```

- [ ] **Step 5: Implementar `RuleEngine`**

`src/MonSelect.Core/Engine/RuleEngine.cs`:

```csharp
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Win32;
using MonSelect.Core.Windows;

namespace MonSelect.Core.Engine;

/// <summary>
/// Orquesta el camino completo: describir la ventana, elegir la regla,
/// resolver el monitor, calcular el destino, aplicarlo y reintentar.
/// </summary>
public sealed class RuleEngine(
    IWindowDescriber probe,
    MonitorRegistry monitors,
    WindowPlacer placer,
    RetryScheduler retries,
    ApplyLog log)
{
    private readonly Lock _gate = new();
    private RuleSet _set = RuleSet.Empty;

    /// <summary>Ventanas ya vistas por reglas con apply: first, por proceso.</summary>
    private readonly HashSet<(string RuleName, uint Pid)> _firstSeen = new();

    /// <summary>Próximo índice de monitor para cada regla con apply: rotate.</summary>
    private readonly Dictionary<string, int> _rotation = new();

    /// <summary>
    /// Ventanas que estamos tratando ahora mismo. Nuestro propio SetWindowPos
    /// dispara eventos que volverían a entrar acá.
    /// </summary>
    private readonly HashSet<nint> _inFlight = new();

    public void UpdateRules(RuleSet set)
    {
        lock (_gate)
        {
            _set = set;
            _rotation.Clear();
        }
    }

    public async Task ApplyAllAsync(IEnumerable<nint> handles, CancellationToken ct)
    {
        foreach (var handle in handles)
        {
            if (ct.IsCancellationRequested)
                return;

            await HandleAsync(handle, ct).ConfigureAwait(false);
        }
    }

    public async Task<ApplyResult> HandleAsync(nint handle, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!_inFlight.Add(handle))
                return ApplyResult.Ignored;
        }

        try
        {
            return await HandleCoreAsync(handle, ct).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
                _inFlight.Remove(handle);
        }
    }

    private async Task<ApplyResult> HandleCoreAsync(nint handle, CancellationToken ct)
    {
        var info = probe.Describe(handle);
        if (info is null)
            return ApplyResult.NoMatch;

        RuleSet set;
        lock (_gate)
            set = _set;

        var rule = RuleMatcher.FirstMatch(set.Rules, info);
        if (rule is null)
            return Record(info, null, ApplyResult.NoMatch, 0, null);

        if (rule.Apply == ApplyMode.First)
        {
            lock (_gate)
            {
                if (!_firstSeen.Add((rule.Name, info.ProcessId)))
                    return Record(info, rule, ApplyResult.Ignored, 0, "ya se colocó la primera ventana");
            }
        }

        var alias = NextAlias(rule);
        if (!set.Monitors.TryGetValue(alias, out var declared))
            return Record(info, rule, ApplyResult.Skipped, 0,
                $"el alias '{alias}' no está declarado en el bloque monitors");

        var monitor = monitors.Resolve(new MonitorId(declared.Path), rule.IfMissing, info.Bounds);
        if (monitor is null)
            return Record(info, rule, ApplyResult.Skipped, 0,
                $"el monitor '{alias}' no está conectado y la política es {rule.IfMissing}");

        var target = PlacementCalculator.Compute(
            monitor, rule.Place.State, rule.Place.Rect, info.Bounds);

        var startTicks = probe.StartTicksOf(info.ProcessId);

        var outcome = await retries.RunAsync(
            handle,
            rule.EffectiveRetryMs,
            target.ExpectedBounds,
            () => placer.Apply(handle, info.ProcessId, startTicks, target),
            ct).ConfigureAwait(false);

        var detail = $"{monitor.GdiName} {rule.Place.State}";
        if (!outcome.Settled && outcome.Observed.Count > 0)
            detail += $"; último rect observado {outcome.Observed[^1]}";

        return Record(
            info, rule,
            outcome.Settled ? ApplyResult.Applied : ApplyResult.Resisted,
            outcome.Attempts, detail);
    }

    /// <summary>Para Rotate devuelve el siguiente monitor de la lista; si no, el primero.</summary>
    private string NextAlias(Rule rule)
    {
        var aliases = rule.Place.MonitorAliases;
        if (aliases.Count == 0)
            return string.Empty;

        if (rule.Apply != ApplyMode.Rotate)
            return aliases[0];

        lock (_gate)
        {
            var next = _rotation.TryGetValue(rule.Name, out var i) ? i : 0;
            _rotation[rule.Name] = (next + 1) % aliases.Count;
            return aliases[next];
        }
    }

    private ApplyResult Record(
        WindowInfo info, Rule? rule, ApplyResult result, int attempts, string? detail)
    {
        log.Add(new ApplyEntry(
            DateTimeOffset.Now, info.Handle, info.Title, rule?.Name, result, attempts, detail));
        return result;
    }

    /// <summary>Olvida el estado de apply: first cuando un proceso muere.</summary>
    public void ForgetProcess(uint pid)
    {
        lock (_gate)
            _firstSeen.RemoveWhere(key => key.Pid == pid);
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test --filter RuleEngineTests`
Expected: PASS, 10 tests.

- [ ] **Step 7: Correr la suite completa**

Run: `dotnet test`
Expected: PASS, 84 tests.

- [ ] **Step 8: Commit**

```bash
git add src/MonSelect.Core/Engine tests/MonSelect.Core.Tests/RuleEngineTests.cs src/MonSelect.Core/Windows/WindowProbe.cs
git commit -m "feat: wire probe, matcher, placer and retries into the rule engine"
```

---

## Task 15: App de bandeja

**Files:**
- Create: `src/MonSelect.App/MonSelect.App.csproj`
- Create: `src/MonSelect.App/app.manifest`
- Create: `src/MonSelect.App/ConfigPaths.cs`
- Create: `src/MonSelect.App/Bootstrap.cs`
- Create: `src/MonSelect.App/TrayHost.cs`
- Create: `src/MonSelect.App/TopLevelWindows.cs`
- Create: `src/MonSelect.App/ApplyLogFile.cs`
- Create: `src/MonSelect.App/DiagnoseMode.cs`
- Create: `src/MonSelect.App/Program.cs`
- Create: `src/MonSelect.App/Autostart.cs`
- Create: `src/MonSelect.Core/Rules/ConfigSeed.cs`
- Test: `tests/MonSelect.Core.Tests/ConfigSeedTests.cs`

**Interfaces:**
- Consumes: `RuleEngine`, `WindowWatcher`, `RuleSet`, `YamlStore`, `Win32MonitorSystem`.
- Produces: ejecutable `MonSelect.App` con bandeja, hot-reload, hotkey global, modo `--diagnose` y registro de tarea de autostart. Y `static class ConfigSeed` en `Core` con `RuleSet Seed(IReadOnlyList<MonitorInfo> monitors)`, que genera el bloque `monitors:` inicial.

- [ ] **Step 1: Escribir el test de la config semilla**

`tests/MonSelect.Core.Tests/ConfigSeedTests.cs`:

```csharp
using MonSelect.Core.Rules;
using MonSelect.Core.Tests.Fakes;

namespace MonSelect.Core.Tests;

public class ConfigSeedTests
{
    [Fact]
    public void Seeds_one_alias_per_connected_monitor()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.Equal(4, set.Monitors.Count);
    }

    [Fact]
    public void Aliases_are_short_lowercase_and_unique()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.All(set.Monitors.Keys, a => Assert.Equal(a.ToLowerInvariant(), a));
        Assert.Equal(set.Monitors.Count, set.Monitors.Keys.Distinct().Count());
    }

    [Fact]
    public void The_primary_monitor_is_aliased_primary()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.True(set.Monitors.ContainsKey("primary"));
        Assert.Equal(FakeMonitorSystem.Primary.Id.DevicePath, set.Monitors["primary"].Path);
    }

    [Fact]
    public void Each_alias_carries_the_full_device_path()
    {
        var set = ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors());

        Assert.All(set.Monitors.Values, m => Assert.Contains("DISPLAY#", m.Path));
    }

    [Fact]
    public void The_seed_has_no_rules()
    {
        Assert.Empty(ConfigSeed.Seed(new FakeMonitorSystem().GetMonitors()).Rules);
    }

    [Fact]
    public void Seeding_with_no_monitors_yields_an_empty_set()
    {
        var set = ConfigSeed.Seed(Array.Empty<MonSelect.Core.Monitors.MonitorInfo>());

        Assert.Empty(set.Monitors);
    }
}
```

- [ ] **Step 2: Implementar `ConfigSeed`**

`src/MonSelect.Core/Rules/ConfigSeed.cs`:

```csharp
using MonSelect.Core.Monitors;

namespace MonSelect.Core.Rules;

/// <summary>
/// Genera el bloque monitors: del primer arranque. El usuario no tiene por qué
/// escribir un device path a mano; renombra los alias y listo.
/// </summary>
public static class ConfigSeed
{
    public static RuleSet Seed(IReadOnlyList<MonitorInfo> monitors)
    {
        var aliases = new Dictionary<string, MonitorAlias>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monitor in monitors)
        {
            var alias = Unique(BaseAlias(monitor), used);
            used.Add(alias);
            aliases[alias] = new MonitorAlias(monitor.Id.DevicePath, Label(monitor));
        }

        return new RuleSet(1, aliases, Array.Empty<Rule>());
    }

    private static string BaseAlias(MonitorInfo monitor)
    {
        if (monitor.IsPrimary)
            return "primary";

        // \\.\DISPLAY3 -> display3
        var digits = new string(monitor.GdiName.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? $"display{digits}" : "monitor";
    }

    private static string Unique(string basis, HashSet<string> used)
    {
        if (!used.Contains(basis))
            return basis;

        for (var i = 2; ; i++)
        {
            var candidate = $"{basis}{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private static string Label(MonitorInfo monitor)
        => $"{monitor.GdiName} {monitor.Bounds.Width}x{monitor.Bounds.Height}"
           + (monitor.IsPrimary ? " (principal)" : string.Empty);
}
```

- [ ] **Step 3: Correr los tests y verificar que pasan**

Run: `dotnet test --filter ConfigSeedTests`
Expected: PASS, 6 tests.

- [ ] **Step 4: Crear el proyecto de la app**

```bash
dotnet new wpf -n MonSelect.App -o src/MonSelect.App
dotnet sln add src/MonSelect.App/MonSelect.App.csproj
dotnet add src/MonSelect.App/MonSelect.App.csproj reference src/MonSelect.Core/MonSelect.Core.csproj
rm src/MonSelect.App/MainWindow.xaml src/MonSelect.App/MainWindow.xaml.cs
```

Editar `src/MonSelect.App/MonSelect.App.csproj` para agregar:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <UseWPF>true</UseWPF>
  <UseWindowsForms>true</UseWindowsForms>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <StartupObject>MonSelect.App.Program</StartupObject>
</PropertyGroup>
```

`src/MonSelect.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
      <longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 5: Implementar rutas y composición**

`src/MonSelect.App/ConfigPaths.cs`:

```csharp
namespace MonSelect.App;

public static class ConfigPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonSelect");

    public static string Rules => Path.Combine(Root, "rules.yaml");
    public static string Borderless => Path.Combine(Root, "borderless.json");
    public static string LogDirectory => Path.Combine(Root, "logs");
}
```

`src/MonSelect.App/Bootstrap.cs`:

```csharp
using MonSelect.Core.Engine;
using MonSelect.Core.Monitors;
using MonSelect.Core.Rules;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>Arma el grafo de objetos y mantiene la config sincronizada con el disco.</summary>
public sealed class Bootstrap : IDisposable
{
    private readonly Win32MonitorSystem _monitorSystem = new();
    private readonly Win32WindowSystem _windowSystem = new();
    private readonly WindowWatcher _watcher = new();
    private readonly CancellationTokenSource _cts = new();

    private FileSystemWatcher? _configWatcher;
    private CancellationTokenSource? _reloadDebounce;

    public RuleEngine Engine { get; }
    public ApplyLog Log { get; } = new();
    public string? LastConfigError { get; private set; }

    public event Action? ConfigChanged;

    public Bootstrap()
    {
        var styles = new StyleStore(ConfigPaths.Borderless);
        styles.Load();

        Engine = new RuleEngine(
            new WindowProbe(_windowSystem),
            new MonitorRegistry(_monitorSystem),
            new WindowPlacer(_windowSystem, styles),
            new RetryScheduler(_windowSystem, new RealDelay()),
            Log);
    }

    public void Start()
    {
        EnsureConfigExists();
        ReloadConfig();
        WatchConfig();

        _watcher.WindowAppeared += hwnd =>
            _ = Engine.HandleAsync(hwnd, _cts.Token);

        _watcher.Start();
    }

    /// <summary>Encola trabajo en el hilo dueño de las ventanas.</summary>
    public void Post(Action work) => _watcher.Post(work);

    private void EnsureConfigExists()
    {
        if (File.Exists(ConfigPaths.Rules))
            return;

        YamlStore.Save(ConfigPaths.Rules, ConfigSeed.Seed(_monitorSystem.GetMonitors()));
    }

    /// <summary>
    /// Una config ilegible deja las reglas anteriores en memoria. Quedarse sin
    /// reglas por un dos puntos faltante sería peor que el error.
    /// </summary>
    public void ReloadConfig()
    {
        try
        {
            Engine.UpdateRules(YamlStore.Load(ConfigPaths.Rules));
            LastConfigError = null;
        }
        catch (RuleSetFormatException ex)
        {
            LastConfigError = ex.Message;
        }

        ConfigChanged?.Invoke();
    }

    private void WatchConfig()
    {
        Directory.CreateDirectory(ConfigPaths.Root);

        _configWatcher = new FileSystemWatcher(ConfigPaths.Root, "rules.yaml")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _configWatcher.Changed += (_, _) => DebouncedReload();
    }

    /// <summary>Los editores escriben en varios pasos; sin debounce se recarga a medio guardar.</summary>
    private void DebouncedReload()
    {
        _reloadDebounce?.Cancel();
        _reloadDebounce = new CancellationTokenSource();
        var token = _reloadDebounce.Token;

        _ = Task.Delay(300, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                ReloadConfig();
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _configWatcher?.Dispose();
        _reloadDebounce?.Dispose();
        _watcher.Dispose();
        _cts.Dispose();
    }
}
```

- [ ] **Step 6: Implementar la bandeja y el arranque**

`src/MonSelect.App/TrayHost.cs`:

```csharp
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MonSelect.Core.Engine;

namespace MonSelect.App;

public sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Bootstrap _bootstrap;

    public TrayHost(Bootstrap bootstrap)
    {
        _bootstrap = bootstrap;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Aplicar reglas ahora", null, (_, _) => ApplyAll());
        menu.Items.Add("Abrir rules.yaml", null, (_, _) => OpenConfig());
        menu.Items.Add("Recargar config", null, (_, _) => _bootstrap.ReloadConfig());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };

        _bootstrap.ConfigChanged += UpdateTooltip;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        var error = _bootstrap.LastConfigError;
        _icon.Text = error is null
            ? "MonSelect"
            : "MonSelect — error de config";

        if (error is not null)
        {
            _icon.BalloonTipTitle = "rules.yaml tiene un problema";
            // El globo se corta a 255 caracteres; el mensaje completo va al log.
            _icon.BalloonTipText = error.Length > 250 ? error[..250] : error;
            _icon.ShowBalloonTip(5000);
        }
    }

    private void ApplyAll()
        => _bootstrap.Post(() =>
        {
            var handles = TopLevelWindows.Enumerate().ToList();
            _ = _bootstrap.Engine.ApplyAllAsync(handles, CancellationToken.None);
        });

    private static void OpenConfig()
        => Process.Start(new ProcessStartInfo(ConfigPaths.Rules) { UseShellExecute = true });

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
```

`src/MonSelect.App/TopLevelWindows.cs`:

```csharp
using System.Runtime.InteropServices;

namespace MonSelect.App;

internal static class TopLevelWindows
{
    private delegate bool EnumProc(nint hwnd, nint param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, nint param);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(nint hwnd);

    public static IEnumerable<nint> Enumerate()
    {
        var found = new List<nint>();

        EnumWindows((hwnd, _) =>
        {
            // Sin título visible no es una ventana que le importe al usuario.
            if (IsWindowVisible(hwnd) && GetWindowTextLengthW(hwnd) > 0)
                found.Add(hwnd);

            return true;
        }, 0);

        return found;
    }
}
```

`src/MonSelect.App/Program.cs`:

```csharp
using System.Windows;

namespace MonSelect.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--install-autostart"))
            return Autostart.Install() ? 0 : 1;

        if (args.Contains("--uninstall-autostart"))
            return Autostart.Uninstall() ? 0 : 1;

        if (args.Contains("--diagnose"))
            return DiagnoseMode.Run();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        using var bootstrap = new Bootstrap();
        bootstrap.Start();
        using var tray = new TrayHost(bootstrap);

        return app.Run();
    }
}
```

`src/MonSelect.App/DiagnoseMode.cs`:

```csharp
using MonSelect.Core.Engine;
using MonSelect.Core.Windows;

namespace MonSelect.App;

/// <summary>
/// Vuelca cada ventana que aparece con todos sus campos de matcheo. Es la
/// herramienta con la que se escriben reglas para aplicaciones difíciles.
/// </summary>
public static class DiagnoseMode
{
    public static int Run()
    {
        using var watcher = new WindowWatcher();
        var probe = new WindowProbe(new Win32WindowSystem());

        watcher.WindowAppeared += hwnd =>
        {
            var info = probe.Describe(hwnd);
            if (info is null || string.IsNullOrEmpty(info.Title))
                return;

            Console.WriteLine(new string('-', 70));
            Console.WriteLine($"title   : {info.Title}");
            Console.WriteLine($"exe     : {info.ExePath ?? "<sin acceso>"}");
            Console.WriteLine($"cmdline : {info.CommandLine ?? "<sin acceso>"}");
            Console.WriteLine($"class   : {info.ClassName}");
            Console.WriteLine($"state   : {info.CurrentState}");
            Console.WriteLine($"bounds  : {info.Bounds}");
        };

        watcher.Start();
        Console.WriteLine("MonSelect --diagnose. Abrí aplicaciones. Enter para salir.");
        Console.ReadLine();
        return 0;
    }
}
```

`src/MonSelect.App/ApplyLogFile.cs` — el log rotativo que pide el spec, sección 10:

```csharp
using MonSelect.Core.Engine;

namespace MonSelect.App;

/// <summary>
/// Vuelca las aplicaciones a un archivo por día y borra los viejos. El log en
/// memoria de ApplyLog alimenta la GUI de F2; éste es el que sobrevive a un
/// reinicio y sirve para entender por qué una app no obedeció ayer.
/// </summary>
public sealed class ApplyLogFile(int keepDays = 7)
{
    private readonly Lock _gate = new();

    public void Write(ApplyEntry entry)
    {
        var line = string.Join('\t',
            entry.At.ToString("O"),
            entry.Result,
            entry.RuleName ?? "-",
            entry.Attempts,
            entry.Title,
            entry.Detail ?? "-");

        lock (_gate)
        {
            Directory.CreateDirectory(ConfigPaths.LogDirectory);
            File.AppendAllText(PathForToday(), line + Environment.NewLine);
        }
    }

    private static string PathForToday()
        => Path.Combine(ConfigPaths.LogDirectory, $"monselect-{DateTime.Now:yyyy-MM-dd}.log");

    public void Prune()
    {
        if (!Directory.Exists(ConfigPaths.LogDirectory))
            return;

        var cutoff = DateTime.Now.AddDays(-keepDays);

        foreach (var file in Directory.EnumerateFiles(ConfigPaths.LogDirectory, "monselect-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
            catch (IOException)
            {
                // Un archivo en uso se borra la próxima vez. No vale abortar por esto.
            }
        }
    }
}
```

Conectarlo en `Bootstrap`: agregar el campo `private readonly ApplyLogFile _file = new();`, llamar `_file.Prune()` dentro de `Start()`, y cambiar el suscriptor del watcher para que registre el resultado:

```csharp
        _watcher.WindowAppeared += hwnd => _ = HandleAndLogAsync(hwnd);
```

```csharp
    private async Task HandleAndLogAsync(nint hwnd)
    {
        var before = Log.Recent().Count;
        await Engine.HandleAsync(hwnd, _cts.Token).ConfigureAwait(false);

        foreach (var entry in Log.Recent().Skip(before))
            _file.Write(entry);
    }
```

- [ ] **Step 7: Implementar el autostart**

`src/MonSelect.App/Autostart.cs`:

```csharp
using System.Diagnostics;

namespace MonSelect.App;

/// <summary>
/// Registra MonSelect como tarea at-logon con privilegios máximos. No se usa la
/// carpeta Startup ni la clave Run porque sin elevación no se pueden manipular
/// ventanas de aplicaciones elevadas.
/// </summary>
public static class Autostart
{
    private const string TaskName = "MonSelect";

    public static bool Install()
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
            return false;

        return Run($"/Create /TN {TaskName} /TR \"\\\"{exe}\\\"\" /SC ONLOGON /RL HIGHEST /F");
    }

    public static bool Uninstall() => Run($"/Delete /TN {TaskName} /F");

    private static bool Run(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (process is null)
            return false;

        process.WaitForExit();

        if (process.ExitCode != 0)
            Console.Error.WriteLine(process.StandardError.ReadToEnd());

        return process.ExitCode == 0;
    }
}
```

- [ ] **Step 8: Compilar y correr toda la suite**

Run: `dotnet build && dotnet test`
Expected: build sin warnings (recordar que están como errores) y PASS, 90 tests.

- [ ] **Step 9: Commit**

```bash
git add src/MonSelect.App src/MonSelect.Core/Rules/ConfigSeed.cs tests/MonSelect.Core.Tests/ConfigSeedTests.cs MonSelect.sln
git commit -m "feat: add tray application with hot reload and diagnose mode"
```

---

## Task 16: Verificación de aceptación de F1

**Files:**
- Create: `docs/superpowers/findings/f1-acceptance.md`

**Interfaces:**
- Consumes: la aplicación completa.
- Produces: evidencia de que se cumple el criterio de terminado de F1 del spec: *RustDesk con `--connect` abre en el monitor BenQ en borderless, de forma repetible tras reinicio.*

**No hay tests unitarios acá.** Es la verificación manual de punta a punta. Ninguna de las tareas anteriores demuestra que el sistema completo funcione contra aplicaciones reales.

- [ ] **Step 1: Arrancar en modo diagnóstico y capturar los campos de RustDesk**

```bash
dotnet run --project src/MonSelect.App -- --diagnose
```

Abrir RustDesk conectando a una máquina. Copiar de la salida los valores exactos de `exe`, `cmdline` y `class`.

- [ ] **Step 2: Escribir la regla**

Abrir `%APPDATA%\MonSelect\rules.yaml`. El bloque `monitors:` ya está generado; renombrar el alias del BenQ a `benq` y agregar:

```yaml
rules:
  - name: RustDesk
    match:
      exe: "<el exe que imprimió --diagnose>"
      cmdline: "--connect"
    place:
      monitor: benq
      state: borderless
```

- [ ] **Step 3: Verificar la colocación**

Arrancar la app normal (`dotnet run --project src/MonSelect.App`), cerrar RustDesk y volver a abrirlo con `--connect`.

Comprobar:

1. La ventana aparece en el monitor BenQ, no en el primario.
2. No tiene barra de título.
3. Cubre la pantalla completa, tapando la taskbar.
4. Repetir tres veces: el comportamiento tiene que ser idéntico las tres.

- [ ] **Step 4: Verificar los otros tres estados**

Cambiar `state` a `maximized`, guardar, y comprobar que el hot-reload toma el cambio sin reiniciar la app: la próxima ventana tiene que aparecer maximizada respetando la taskbar y **con** barra de título. Repetir con `minimized` y con `normal`.

- [ ] **Step 5: Verificar la reversión del borderless**

Con una ventana en borderless, salir de MonSelect y volver a entrar. Comprobar que `%APPDATA%\MonSelect\borderless.json` contiene el registro con el style original — sin eso, la ventana quedaría mutilada sin forma de restaurarla.

- [ ] **Step 6: Verificar el comportamiento ante config rota**

Introducir un error de indentación en `rules.yaml` y guardar. Comprobar que aparece el globo de error en la bandeja y que **las reglas anteriores siguen funcionando**: abrir RustDesk y verificar que se sigue colocando bien.

- [ ] **Step 7: Verificar el cambio de monitores**

Desconectar el monitor BenQ. Abrir RustDesk. Con `if_missing: skip` por defecto, la ventana no debe moverse a ningún lado. Reconectar y comprobar que vuelve a funcionar sin reiniciar MonSelect.

- [ ] **Step 8: Probar contra una app de Electron y una de Qt**

Repetir el paso 3 con VS Code o Slack, y con una app Qt. Estas son las que más se reposicionan solas. Anotar cuántos reintentos hicieron falta según el log — si alguna aparece como `Resisted`, documentarlo con su rect observado: es la información que decide si hay que ampliar el presupuesto de retry.

- [ ] **Step 9: Verificar el autostart**

```bash
dotnet run --project src/MonSelect.App -- --install-autostart
schtasks /Query /TN MonSelect
```

Reiniciar sesión de Windows y comprobar que MonSelect aparece en la bandeja solo.

- [ ] **Step 10: Documentar los resultados**

Crear `docs/superpowers/findings/f1-acceptance.md` con una tabla de los diez pasos, su resultado, y cualquier aplicación que haya resistido junto con sus rects observados.

- [ ] **Step 11: Commit**

```bash
git add docs/superpowers/findings/f1-acceptance.md
git commit -m "docs: record F1 acceptance verification results"
```

---

## Notas de ejecución

**Orden.** Las tareas 1 a 6 son independientes del hardware y se pueden hacer de corrido. La 7 es un experimento cuyo resultado condiciona la 8: no saltearla ni adivinar su resultado. Las 12 a 16 necesitan la máquina con los cuatro monitores.

**El presupuesto de retry es un parámetro, no una verdad.** Los `[0, 150, 400, 800]` ms salen del spec, no de una medición. Si en la Task 16 alguna aplicación aparece repetidamente como `Resisted`, el ajuste correcto es ampliar su `retry_ms` en la regla, no cambiar el default global.

**Lo que F1 deja afuera a propósito:** GUI, hotkeys globales, `AppUserModelID`, reacción a `WM_DISPLAYCHANGE` y zonas custom. La GUI y los hotkeys son F2; las zonas, F3. `WindowInfo.Aumid` ya existe y siempre vale `null` en F1, así que agregar el soporte no cambia ninguna firma.

