# MonSelect — Diseño

**Fecha:** 2026-09-01
**Estado:** aprobado para plan de implementación
**Stack:** C# / .NET 10 + WPF, app de bandeja

---

## 1. Problema

Windows 11 no permite definir, por aplicación, en qué monitor se abre una ventana ni con qué estado. Sólo tiene una heurística de "recordar la última posición" al cerrar, que es poco fiable y no cubre el estado maximizado.

MonSelect resuelve eso con reglas declarativas: dada una ventana que aparece, decidir a qué monitor va y en qué estado queda.

## 2. Alcance

**Dentro:**

- Reglas por aplicación que fijan monitor de destino y estado de ventana.
- Cuatro estados: `normal` (rect exacto), `maximized`, `minimized`, `borderless`.
- Matching por exe path, command line, window class, título (regex) y AppUserModelID.
- Aplicación automática al aparecer la ventana, por hotkey, y al cambiar la configuración de monitores.
- GUI para inspeccionar ventanas abiertas y construir reglas desde ellas.
- Config en YAML editable a mano, con hot-reload.

**Fuera:**

- **Exclusive fullscreen real.** No es forzable desde un proceso externo: requiere que la propia aplicación pida ownership del display al driver/GPU. Lo que sí se entrega es *borderless fullscreen*, que es lo que la mayoría de las apps (RustDesk entre ellas) llaman "pantalla completa".
- Tiling automático o gestión dinámica de layout.
- Sincronización de config entre máquinas.

**Diferido a F3:** zonas custom por monitor.

## 3. Hallazgos de investigación

### 3.1 Herramientas existentes

| Herramienta | Monitor per-app | Estado | Costo | Por qué no alcanza |
|---|---|---|---|---|
| DisplayFusion (Triggers) | sí | sí | pago | Descartado: requisito del usuario de no usar software pago |
| WindowManager (DeskSoft) | sí | parcial | pago | Ídem |
| WinSize2 | sí | sí | libre | Matchea por texto del título únicamente: frágil |
| PowerToys FancyZones | vía zonas | no | libre | Una zona no es un estado; no maximiza. Issues 7134, 23717, 16659, 16964 |

Ninguna combina matching robusto por exe/cmdline con el estado de ventana como concepto de primera clase.

### 3.2 Medición del sistema objetivo

Windows 11 Pro Insider, build 26220. Cuatro monitores, todos a 96 DPI (100%), lo que elimina la complejidad de DPI mixto en la primera versión.

```
\\.\DISPLAY1   1920x1080  @ (0,0)        work=(0,0)-(1920,1048)        PRIMARY
\\.\DISPLAY2   1920x1080  @ (0,-1080)    work=(0,-1080)-(1920,-32)
\\.\DISPLAY3   1080x1920  @ (1920,-842)  work=(1920,-842)-(3000,1046)  vertical
\\.\DISPLAY4   1920x1080  @ (3000,0)     work=(3000,0)-(4920,1048)
```

Identidad EDID vía `WmiMonitorID`:

```
GM3CC27         mfg=RDG  serial=0          UID256
MA2223J         mfg=OOO  serial=16843009   UID260   <- 0x01010101, valor basura
M2380A          mfg=GSM  serial=16843009   UID264   <- mismo valor duplicado
BenQ G2220HDA   mfg=BNQ  serial=21573      UID268
```

**Conclusión:** el serial EDID no es utilizable como clave única en este hardware. La identidad debe salir de `QueryDisplayConfig`.

### 3.3 Anatomía de "fullscreen" — caso RustDesk

Ventana medida en vivo:

```
proceso  rustdesk.exe (pid 23340)
cmdline  "C:\Program Files\RustDesk\rustdesk.exe" --connect 123456789
título   WK-EJEMPLO-01 - Remote Desktop - RustDesk
class    RustdeskMultiWindow
STYLE    0x150B0000 = WS_VISIBLE | WS_CLIPSIBLINGS | WS_MAXIMIZE
                    | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX
         WS_CAPTION    ausente
         WS_THICKFRAME ausente
showCmd  3 (SW_MAXIMIZE), IsZoomed = true
rect     (3000,0)-(4920,1080)  = 1920x1080
monitor  \\.\DISPLAY4   rcMonitor (3000,0)-(4920,1080)   rcWork ...-1048
restore  (3057,253)-(4486,857)
```

El rect llega a 1080 y no a 1048: la ventana tapa la taskbar. Ésa es la firma de borderless fullscreen — una ventana sin `WS_CAPTION` ni `WS_THICKFRAME` se expande al `rcMonitor` completo al maximizarse, en lugar de al `rcWork`.

**Es reproducible desde fuera** para cualquier aplicación:

1. `SetWindowLong(GWL_STYLE, style & ~(WS_CAPTION | WS_THICKFRAME))`
2. `SetWindowPos(..., SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER)`
3. `ShowWindow(SW_MAXIMIZE)`

RustDesk corre como un único proceso cuyo command line conserva `--connect`, así que el matching por command line distingue sesiones remotas a máquinas distintas.

## 4. Arquitectura

```
MonSelect.sln
├── src/MonSelect.Core/          sin dependencias de UI, testeable con fakes
│   ├── Win32/                   P/Invoke (user32, shcore, ntdll)
│   ├── Monitors/                MonitorRegistry
│   ├── Windows/                 WindowInfo, WindowProbe, WindowPlacer
│   ├── Rules/                   Rule, RuleSet, RuleMatcher, YamlStore
│   └── Engine/                  WindowWatcher, RuleEngine, RetryScheduler
├── src/MonSelect.App/           WPF: tray + GUI
└── tests/MonSelect.Core.Tests/  xunit
```

### 4.1 Flujo

```
WindowWatcher   SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_SYSTEM_FOREGROUND)
      | hwnd
WindowProbe     -> WindowInfo { hwnd, pid, exePath, commandLine, className,
      |                         title, aumid, monitorId, currentState }
RuleMatcher     -> primera Rule que matchea
      |
RuleEngine      -> Placement { monitorId, state, rect? }
      |
WindowPlacer    -> SetWindowLong / SetWindowPos / SetWindowPlacement
      |
RetryScheduler  -> reaplica hasta rect estable
```

### 4.2 Decisiones de diseño

**Core no habla Win32 directamente.** Todo acceso pasa por `IWindowSystem` e `IMonitorSystem`. La implementación real hace P/Invoke; los tests usan fakes. Sin esta frontera no hay forma de testear el matcher ni el motor sin un escritorio real con ventanas abiertas.

**Un único hilo dueño de las ventanas.** El callback del hook requiere un thread con message pump propio, y ese mismo thread ejecuta el placement. `SetWindowPos` desde varios hilos contra la misma ventana produce resultados dependientes del orden, y el retry competiría consigo mismo. La GUI vive en el thread de WPF y se comunica por cola.

**El delegate del callback se ancla con `GCHandle`.** Si el GC lo mueve, el hook muere con una violación de acceso difícil de diagnosticar.

**El retry es núcleo, no adorno.** Muchas apps (Electron y Qt especialmente) se reposicionan solas después de mostrarse. Una sola aplicación en `EVENT_OBJECT_SHOW` falla en silencio.

## 5. Modelo de configuración

Ubicación: `%APPDATA%\MonSelect\rules.yaml`

```yaml
version: 1

monitors:                       # autogenerado en el primer arranque; los alias los renombra el usuario
  benq:
    path: '\\?\DISPLAY#BNQ7820#7&1a2b3c4d&0&UID268#{e6f07b5f-...}'
    label: "BenQ G2220HDA (derecha)"
  vertical:
    path: '\\?\DISPLAY#OOO2223#7&1a2b3c4d&0&UID260#{e6f07b5f-...}'
    label: "MA2223J (vertical)"

defaults:
  if_missing: skip              # skip | primary | nearest
  retry_ms: [0, 150, 400, 800]

rules:
  - name: RustDesk EJEMPLO-01
    enabled: true
    match:                      # todos los campos opcionales; los presentes se combinan con AND
      exe: "C:/Program Files/RustDesk/rustdesk.exe"
      cmdline: "--connect 123456789"
      class: RustdeskMultiWindow
      title: "^WK-EJEMPLO-01.*"
      aumid: null
    place:
      monitor: benq             # alias, o lista de alias cuando apply: rotate
      state: borderless         # normal | maximized | minimized | borderless
      rect: null                # sólo para state: normal
    apply: all                  # all | first | rotate
    if_missing: skip            # opcional, pisa defaults
    retry_ms: null              # opcional, pisa defaults.retry_ms
```

Ejemplo de `rotate`, que exige lista:

```yaml
  - name: Chrome en dos pantallas
    match:
      exe: "C:/Program Files/Google/Chrome/Application/chrome.exe"
      class: Chrome_WidgetWin_1
    place:
      monitor: [benq, vertical]
      state: maximized
    apply: rotate
```

Semántica de campos de `match`:

| Campo | Comparación |
|---|---|
| `exe` | Path normalizado, case-insensitive |
| `cmdline` | Substring por defecto; regex si va envuelto entre barras, p. ej. `/--connect \d+/` |
| `class` | Igualdad exacta |
| `title` | Siempre regex |
| `aumid` | Igualdad exacta |

`apply` controla el comportamiento con múltiples ventanas:

| Valor | Comportamiento |
|---|---|
| `all` | Aplica a cada ventana que matchee. Por defecto. |
| `first` | Aplica sólo a la primera ventana que matchee mientras el proceso viva. El contador se reinicia cuando ese pid muere, no cuando reinicia MonSelect. |
| `rotate` | Recorre `place.monitor` como lista ordenada, un monitor por ventana, volviendo al principio al agotarla. Requiere que `place.monitor` sea una lista. |

## 6. Matching

**Precedencia: gana la primera regla que matchea, en el orden del archivo.** No hay scoring por especificidad. Un matcher que puntúa parece más inteligente y resulta imposible de depurar con veinte reglas: el usuario tiene que poder predecir el resultado leyendo el archivo, y la GUI muestra ese mismo orden.

**Command line sin WMI en el camino crítico.** Se obtiene con `NtQueryInformationProcess(ProcessBasicInformation)` y lectura del PEB del proceso. `Win32_Process` cuesta entre 50 y 200 ms por consulta; con ese retraso la aplicación ya se movió sola. WMI queda como fallback cuando la lectura del PEB falla.

**Degradación, no fallo.** Si el proceso pertenece a otro usuario o está elevado, la lectura del command line falla: la regla que dependa de ese campo simplemente no matchea, y se registra en el log.

**AppUserModelID** se obtiene con `SHGetPropertyStoreForWindow` y `PKEY_AppUserModel_ID`, para apps de Microsoft Store que no exponen un exe path útil.

**Cache.** `exePath` y `commandLine` se cachean por pid mientras el proceso viva. `WindowInfo` se cachea por hwnd con TTL corto.

## 7. Motor de posicionamiento

```
normal      SetWindowPlacement(SW_NORMAL, normalPosition = rect destino)
maximized   SetWindowPlacement(SW_MAXIMIZE, normalPosition dentro del monitor destino)
minimized   mover al monitor destino, después SW_MINIMIZE
borderless  guardar style -> quitar WS_CAPTION|WS_THICKFRAME -> SWP_FRAMECHANGED -> SW_MAXIMIZE
```

**No existe una operación "maximizar en el monitor X".** Windows maximiza en el monitor donde la ventana ya se encuentra. Por eso la llamada correcta es `SetWindowPlacement` y no `ShowWindow`: fija el estado y el rect de restauración en una sola operación, y la ventana nace maximizada en el monitor correcto en lugar de aparecer en el equivocado y saltar.

**Coordenadas de `WINDOWPLACEMENT`.** La documentación las describe como coordenadas de workspace, que difieren de las de pantalla cuando hay taskbar u otras appbars. El offset relevante es `rcWork - rcMonitor` del monitor de destino (32 px verticales en DISPLAY1 y DISPLAY4 de este equipo). La semántica exacta en un escritorio multi-monitor con orígenes negativos debe verificarse empíricamente durante F1: hay un test dedicado a esto, y su resultado determina si la corrección de offset se aplica o no.

**Borderless reversible.** El style original se guarda en memoria y se persiste en disco. Sin persistencia, un reinicio de MonSelect deja ventanas sin barra de título que el usuario no puede restaurar.

**Retry.** Intentos en t = 0, 150, 400 y 800 ms. Corta cuando dos lecturas consecutivas de `GetWindowRect` coinciden entre sí y con el objetivo. Si agota los intentos, registra "la aplicación resistió" junto con los rects observados en cada intento.

**Guard anti-loop.** Nuestro propio `SetWindowPos` dispara `EVENT_OBJECT_LOCATIONCHANGE`, que volvería a entrar al motor. El hwnd se marca "en tratamiento" durante la ventana de retry y sus eventos se ignoran en ese lapso.

**Elevación.** Si la ventana pertenece a un proceso elevado y MonSelect no lo está, las llamadas fallan — a veces devolviendo éxito. Se detecta comparando niveles de integridad de los tokens y se registra explícitamente, en lugar de reintentar en vano.

## 8. Identidad de monitor

Ni el serial EDID (duplicado o nulo en este hardware) ni `\\.\DISPLAYn` (se reasigna al reconectar) sirven como clave.

Se usa `QueryDisplayConfig` con `DisplayConfigGetDeviceInfo(DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME)`, que devuelve `monitorDevicePath`:

```
\\?\DISPLAY#BNQ7820#7&1a2b3c4d&0&UID268#{e6f07b5f-...}
```

Estable entre reinicios y reconexiones, único por puerto físico.

El usuario nunca escribe ese path: escribe un alias. El bloque `monitors:` lo genera MonSelect en el primer arranque y el usuario renombra los alias.

Si un monitor referenciado no está conectado, la política `if_missing` decide: `skip` (por defecto — la regla no se aplica y se loguea), `primary` (cae al monitor principal) o `nearest` (el más cercano geométricamente a la posición original).

## 9. GUI

Ventana WPF que se abre desde el icono de bandeja, con tres vistas:

**Ventanas abiertas.** Tabla en vivo: título, exe, class, command line, monitor y estado actuales. Botón *Crear regla desde esta ventana* que pre-llena todos los campos del matcher.

**Reglas.** Lista reordenable por drag — el orden es la prioridad —, editor por campo, toggle de enable/disable y botón *Probar ahora* que aplica únicamente esa regla a las ventanas que matcheen.

**Log.** Últimas aplicaciones con su resultado: qué regla matcheó, qué se aplicó, cuántos retries hicieron falta, si la aplicación resistió.

**Hotkeys globales** (`RegisterHotKey`): capturar la ventana bajo el cursor como regla nueva, y aplicar todas las reglas ahora.

## 10. Operación

**Hot-reload.** `FileSystemWatcher` con debounce de 300 ms. Si el YAML no parsea, se mantienen las reglas anteriores en memoria y el error se muestra en el tray: quedarse sin reglas por un `:` faltante sería peor que el error mismo.

**Triggers.**

| Evento | Acción |
|---|---|
| `EVENT_OBJECT_SHOW` | Camino principal: evaluar y aplicar |
| `EVENT_SYSTEM_FOREGROUND` | Red de seguridad para ventanas que nunca emiten `SHOW` visible; se descarta si el hwnd ya fue tratado |
| Hotkey "aplicar ahora" | Reevaluar todas las ventanas abiertas |
| `WM_DISPLAYCHANGE` | Reconstruir `MonitorRegistry` y reaplicar |
| Inicio de sesión | Arranque automático |

**Autostart:** tarea de Task Scheduler, at-logon, con highest privileges. No se usa la carpeta Startup ni la clave Run, porque sin elevación no se pueden manipular ventanas de aplicaciones elevadas.

**Logging:** archivo rotativo en `%APPDATA%\MonSelect\logs\`. Modo `--diagnose` que registra cada ventana que aparece con todos sus campos de matcheo — es la herramienta con la que el usuario escribe reglas para aplicaciones difíciles.

## 11. Testing

Sobre `Core`, con fakes de `IWindowSystem` e `IMonitorSystem`:

- Precedencia del matcher con reglas solapadas.
- Campos de match opcionales, presentes y ausentes, en todas las combinaciones.
- Regex de título válidos e inválidos.
- Command line ausente por falta de permisos.
- Cálculo del rect de destino para cada estado y cada monitor, incluida la corrección de offset de workspace y los monitores con origen negativo.
- Resolución de alias con monitor desconectado, para las tres políticas `if_missing`.
- Semántica de `apply`: `all` sobre varias ventanas, `first` con reinicio del contador al morir el pid, `rotate` agotando y reciclando la lista de monitores.
- Política de retry: corta al estabilizarse, agota el presupuesto, registra el resultado.
- Serialización YAML ida y vuelta, y comportamiento ante archivo inválido.

Lo no automatizable —el hook real contra aplicaciones concretas— se cubre con el modo `--diagnose` y una lista de verificación manual sobre RustDesk, un Electron (VS Code o Slack), una app Qt y una app de Store.

## 12. Fases

| Fase | Alcance | Criterio de terminado |
|---|---|---|
| **F1** | Core, motor, YAML, hook, tray mínimo. Sin GUI. | RustDesk con `--connect` abre en el monitor BenQ en borderless, de forma repetible tras reinicio |
| **F2** | GUI completa y hotkeys globales. | Una regla se crea de punta a punta sin editar YAML a mano |
| **F3** | Zonas custom por monitor. | Una zona se define en la GUI y una regla la referencia como destino |

La primitiva `rect` de F1 es la base sobre la que se construyen las zonas de F3; no hay rediseño intermedio.

## 13. Riesgos y desconocidos

| Riesgo | Mitigación |
|---|---|
| Semántica exacta de coordenadas de `WINDOWPLACEMENT` en multi-monitor con orígenes negativos | Test dedicado en F1 antes de construir sobre esa base |
| Aplicaciones que se reposicionan más allá de los 800 ms del presupuesto de retry | El log identifica el caso; el presupuesto es configurable por regla |
| Apps que rompen visualmente al quitarles `WS_CAPTION` | El style se persiste y hay comando de revertir; el estado `borderless` es opt-in por regla |
| RustDesk podría reutilizar el proceso al lanzar una segunda sesión, dejando el command line sin el `--connect` nuevo | El matching por regex de título cubre ese caso; verificar en F1 |
| DPI mixto si el usuario suma un monitor 4K | Todo el código de coordenadas asume per-monitor v2 desde el inicio, aunque hoy no se ejercite |
