# F1 — Verificación de aceptación de punta a punta

**Fecha:** 2026-09-01
**Rama:** `f1-implementation`
**Build:** `dotnet build MonSelect.slnx -c Debug` correcto, 0 errores.
**Tests unitarios:** `dotnet test MonSelect.slnx` → 98/98 correctas, 0 fallidas.

## Desviación respecto del brief — ordenada por el controller

El brief (`task-16-brief.md`) pide verificar contra RustDesk con `--connect`.
RustDesk está corriendo en esta máquina con una sesión remota activa hacia otro
host. El controller de esta tarea ordenó explícitamente **no cerrar, matar,
reiniciar ni tocar RustDesk de ninguna forma** — sólo lectura, si acaso.

En su lugar, la verificación de punta a punta se hizo contra aplicaciones
lanzadas y cerradas por este mismo proceso: **Notepad** (moderno, empaquetado,
Win32 puro), **VLC** (Qt5, `class: Qt5QWindowIcon`) y **Spotify** (Electron/CEF,
`class: Chrome_WidgetWin_1`). El camino de código que matchea y coloca estas
aplicaciones es exactamente el mismo que tomaría RustDesk — eso ya se verificó
por separado contra el binario real de RustDesk en una tarea anterior (matching
por command line). El check específico contra RustDesk queda diferido al
usuario; los pasos exactos están al final de este documento.

**Discord y WhatsApp estaban corriendo con sesión propia del usuario en esta
máquina** (varios procesos `Discord.exe`, una ventana `WhatsApp` vista en el log
de diagnóstico) — se los dejó intactos por la misma razón que RustDesk. VLC y
Spotify estaban instalados pero **no corriendo**, así que se usaron sin riesgo.

## Monitores de la máquina (confirmados con el probe del propio proyecto)

```
\\.\DISPLAY1  (0,0)-(1920,1080)        work→1048   PRIMARY   alias: primary
\\.\DISPLAY2  (0,-1080)-(1920,0)       work→-32              alias: display2
\\.\DISPLAY3  (1920,-842)-(3000,1078)  work→1046             alias: display3 (portrait)
\\.\DISPLAY4  (3000,0)-(4920,1080)     work→1048             alias: display4
```

Coincide exactamente con lo esperado. `rules.yaml` se generó solo al primer
arranque de la app con estos cuatro alias (`primary`, `display2`, `display3`,
`display4`).

## Resultado por punto

| # | Check | Comando / edición | Resultado medido | Pass/Fail |
|---|---|---|---|---|
| 1 | Monitor targeting a `display4` | Regla `exe`+`class` → `monitor: display4, state: borderless`; Notepad lanzado | `probe --windows notepad` → `bounds: (3000,0)-(4920,1080)`. Repetido con `maximized` → `(2992,-8)-(4928,1056)`, con `normal`+rect → `(3200,150)-(3800,750)`. Los tres dentro de `(3000,0)-(4920,1080)`, ninguno en el primario. | **PASS** |
| 2a | Estado `borderless` | `state: borderless` | `Applied … DISPLAY4 Borderless` (log). Bounds `(3000,0)-(4920,1080)` = rect completo del monitor, tapa la franja de taskbar (work area termina en y=1048). Style verificado con `GetWindowLong(GWL_STYLE)` = `0x150B0000`: sin `WS_CAPTION` (`0x00C00000`) ni `WS_THICKFRAME` (`0x00040000`). | **PASS** |
| 2b | Estado `maximized` | hot-reload a `state: maximized`, Notepad relanzado | Log: `Resisted … DISPLAY4 Maximized; último rect observado (2992,-8)-(4928,1056)` tras 4 intentos. Style verificado: `0x15CF0000` → `WS_CAPTION` y `WS_THICKFRAME` presentes, `WS_MAXIMIZE` presente. Ver nota debajo sobre por qué queda "Resisted" con clasificación equivocada. | **PASS** (visual/funcionalmente correcto; ver nota) |
| 2c | Estado `minimized` | hot-reload a `state: minimized`, Notepad relanzado | Log: `Applied … DISPLAY4 Minimized` en el primer intento. `probe` → `bounds: (-32000,-32000)-(-31840,-31972)`, `state: Minimized` — el rect off-screen estándar de Windows para ventanas minimizadas. | **PASS** |
| 2d | Estado `normal` con rect explícito | hot-reload a `state: normal, rect: [3200,150,3800,750]`, Notepad relanzado | Log: `Applied … DISPLAY4 Normal` en el primer intento. `probe` → `bounds: (3200,150)-(3800,750)` exacto. Style verificado: `WS_CAPTION`/`WS_THICKFRAME` presentes (ventana nunca había sido borderless). | **PASS** |
| 3 | Reversión de borderless | Notepad en `borderless`; `borderless.json` inspeccionado; MonSelect cerrado y reabierto con la ventana aún abierta; se intentó reabrir el frame vía el producto | `borderless.json` guarda `OriginalStyle: 349110272` (`0x14CF0000`) — decodificado, **sí** tiene `WS_CAPTION`+`WS_THICKFRAME`: el registro persistido es correcto. Sobrevive el reinicio de MonSelect (mismo contenido antes y después). **Pero no hay ningún camino en la app que invoque `WindowPlacer.Revert()`**: ni el menú de bandeja, ni cambiar `state` a `normal`/`maximized` en la regla y disparar `EVENT_SYSTEM_FOREGROUND`/reload. Confirmado con `grep -rn Revert src/MonSelect.App` → cero resultados; sólo aparece en `WindowPlacerTests.cs`. Se probó manualmente aplicar `SetWindowLong` + `SWP_FRAMECHANGED` con el `OriginalStyle` persistido: **sí restaura el frame** (`WS_CAPTION`/`WS_THICKFRAME` vuelven a `True`), probando que el dato guardado es utilizable — sólo falta el comando que lo invoque. | **FAIL** (persistencia: pass: mecanismo de reversión: no está conectado al producto) |
| 4 | Config rota no desarma el producto | Se introdujo un error de indentación real (una clave `state:` a menos indentación que sus hermanas) y se guardó | Un harness standalone contra `MonSelect.Core.dll` reprodujo exactamente `Bootstrap.ReloadConfig`: `RuleSetFormatException: rules.yaml no es YAML válido: While scanning a plain scalar value, found invalid mapping.` — la excepción específica que `Bootstrap` captura y expone como `LastConfigError` (tooltip/globo de bandeja; no se pudo capturar el globo por pantalla porque el monitor primario tenía una app en pantalla completa tapando la bandeja, ver nota). Funcionalmente: con el YAML roto en disco, se abrió Notepad de nuevo → **la regla anterior (válida) se siguió aplicando exactamente igual** (`Applied … DISPLAY4 Normal`, bounds exactos). Se arregló el archivo (`state: maximized`) y el siguiente Notepad **recuperó el hot-reload** (`bounds (2992,-8)-(4928,1056)`, maximizado). | **PASS** |
| 5 | Monitor desconectado → skip, no adivinar | Alias `phantom` con un `path` inventado que no matchea ningún monitor conectado; regla apuntando a `phantom`, sin `if_missing` explícito (usa el default `skip`) | Log: `Skipped Notepad test 0 Bloc de notas "el monitor 'phantom' no está conectado y la política es Skip"`. La ventana quedó exactamente donde Windows la abre por defecto (`bounds (0,0)-(1920,1048)`, el work area completo del monitor primario) — no se movió a ningún lado arbitrario. | **PASS** |
| 6 | App Electron/Qt | VLC (`class: Qt5QWindowIcon`) y Spotify (`class: Chrome_WidgetWin_1`, Electron/CEF) contra `state: normal, rect: [3200,150,3800,750]` | **VLC**: `Applied … DISPLAY4 Normal` en el primer intento, `bounds (3200,150)-(3800,750)` exacto — no resistió. **Spotify**: `Resisted … DISPLAY4 Normal; último rect observado (3200,150)-(4000,750) 800x600` tras 4 intentos — Spotify acepta la posición y el alto pero fuerza su propio ancho mínimo (800px en vez de los 600px pedidos). Es exactamente el caso que el spec anticipa para Electron: candidato a `retry_ms` ampliado en su propia regla, no en el default global. | **PASS** (comportamiento documentado; ver hallazgo del hang más abajo para la limitación real) |

## Hallazgo no solicitado, y el más importante de los seis: **el motor se cuelga en silencio**

> **Actualización 2026-09-01 — este hallazgo se dio por cerrado dos veces, y la
> primera vez estaba mal.** Después de esta verificación, el commit `e2df175`
> ("fix: stop the window-mutation thread from blocking the hook pump",
> `.superpowers/sdd/2026-09-01-monselect-f1/hang-fix-report.md`) separó
> `WindowWatcher` en dos hilos —uno sólo bombea el hook, otro ejecuta todo el
> placement— y ese trabajo se registró como el defecto 1 resuelto. **No lo
> estaba.** El fix movió la llamada bloqueante de hilo (del hook al de
> colocación) pero no la acotó: como el motor entero —matching, placement,
> retries y las escrituras a `ApplyLog`, incluidos los `NoMatch`— corre en ese
> único hilo de colocación, una sola `SetWindowPos`/`SetWindowPlacement` contra
> una ventana ajena ocupada seguía bloqueando todo lo que venía detrás en la
> cola, sin límite y sin ninguna señal — exactamente el síntoma de acá abajo,
> reproducido de nuevo en la sesión real del usuario a los cuatro minutos de
> uso normal (Chrome abierto), con el log parado en la misma línea durante más
> de diez minutos mientras el proceso seguía "Responding: True". La corrección
> real —acotar con timeout cada llamada mutante contra Win32, no sólo
> cambiarla de hilo— está en
> `.superpowers/sdd/2026-09-01-monselect-f1/hang-fix-2-report.md` y en
> `BoundedWindowSystem` (`src/MonSelect.Core/Windows/BoundedWindowSystem.cs`).
> El resto de esta sección se deja tal cual se escribió originalmente, como
> evidencia histórica del síntoma — sigue siendo una descripción correcta del
> comportamiento observado, sólo que la causa raíz completa y el fix
> verdadero son los del reporte nuevo, no los de `hang-fix-report.md`.

Durante las pruebas 2–3, el proceso `MonSelect.App.exe` **dejó de procesar
eventos de ventana sin morir ni lanzar ninguna excepción visible**, dos veces,
después de un puñado de operaciones exitosas:

- El proceso seguía listado por `Get-Process` con `Responding: True`.
- El archivo de log (`%APPDATA%\MonSelect\logs\monselect-2026-09-01.log`) dejó
  de crecer por completo — ni una sola línea nueva, ni siquiera los `NoMatch`
  de las docenas de ventanas auxiliares que Notepad genera en cada apertura
  (esas normalmente inundan el log).
- Se confirmó con un polling de 8 segundos, 1 muestra por segundo, sin ningún
  byte nuevo, mientras se abría una ventana de Notepad completamente nueva.
- stderr capturado (`nohup ... > app_stderr*.log`) no mostró ninguna traza:
  no hubo excepción no manejada. `WindowWatcher.RunSafely` atrapa excepciones
  del callback y las imprime a `Console.Error` — el silencio total en stderr,
  junto con el proceso vivo y "responding", apunta a un **deadlock** en el
  hilo dueño del hook/placement (`WindowWatcher.Pump`), no a un crash.
- Cada vez que ocurrió, un `Stop-Process -Force` + relanzamiento del proceso
  restauró el comportamiento normal de inmediato (confirmado con crecimiento
  de log en el primer segundo).

**No se identificó la causa exacta** — habría requerido adjuntar un debugger o
instrumentar el código, fuera del alcance de esta tarea de verificación. Es
reproducible (ocurrió dos veces de forma independiente en la misma sesión) y
grave: un usuario real no tendría ninguna señal de que MonSelect dejó de
funcionar — el ícono de bandeja sigue ahí, el proceso sigue "vivo", pero
ninguna regla se vuelve a aplicar hasta que alguien lo reinicia manualmente.
Todas las filas de la tabla de arriba están respaldadas por evidencia tomada
con una instancia **recién reiniciada y con crecimiento de log confirmado
antes de cada acción individual** — es decir, ninguna fila se apoya en un
resultado producido durante uno de estos cuelgues.

**Recomendación:** antes de dar F1 por cerrado, instrumentar
`WindowWatcher.Pump` / `RetryScheduler` con logging de entrada/salida de cada
`Post()` y reproducir bajo un debugger adjunto, para encontrar el lock o el
`await` que nunca se libera.

## Nota sobre el estado `maximized` y la clasificación `Resisted`

En este equipo (Windows 11, build 26220), una ventana maximizada por
`SetWindowPlacement(SW_MAXIMIZE)` recibe un borde invisible de ~8px que DWM
agrega alrededor del rect lógico del work area — comportamiento estándar del
sistema, no un defecto de MonSelect. `GetWindowRect` después de maximizar
devuelve `(2992,-8)-(4928,1056)` en vez del work area exacto
`(3000,0)-(4920,1048)`. El `RetryScheduler` compara contra el rect esperado
exacto y, como nunca coincide, agota los 4 intentos y clasifica el resultado
como `Resisted` — aunque visualmente la ventana está perfectamente maximizada,
con barra de título, respetando la taskbar. **Esto va a afectar a *cualquier*
regla con `state: maximized` en este equipo**, no sólo a apps difíciles: el log
va a mostrar `Resisted` de forma sistemática para el estado maximizado. Vale la
pena que el cálculo de "asentado" tolere el margen del borde invisible de DWM
(un pequeño epsilon, o comparar contra `GetWindowPlacement` en vez de
`GetWindowRect`) antes de usar el conteo de `Resisted` como señal de apps
difíciles.

## Verbatim: comandos y salidas clave

Probe de monitores:

```
=== monitores ===
\\.\DISPLAY1   bounds=(0,0)-(1920,1080) 1920x1080
               work  =(0,0)-(1920,1048) 1920x1048  primary=True
\\.\DISPLAY2   bounds=(0,-1080)-(1920,0) 1920x1080
               work  =(0,-1080)-(1920,-32) 1920x1048  primary=False
\\.\DISPLAY3   bounds=(1920,-842)-(3000,1078) 1080x1920
               work  =(1920,-842)-(3000,1046) 1080x1888  primary=False
\\.\DISPLAY4   bounds=(3000,0)-(4920,1080) 1920x1080
               work  =(3000,0)-(4920,1048) 1920x1048  primary=False
```

Borderless (check 2a):

```
$ tools/probe --windows notepad
bounds   : (3000,0)-(4920,1080) 1920x1080
state    : Borderless

$ log
2026-09-01T13:46:34.1715082-03:00  Applied  Notepad test  1  Bloc de notas  \\.\DISPLAY4 Borderless
```

Maximized (check 2b):

```
$ tools/probe --windows notepad
bounds   : (2992,-8)-(4928,1056) 1936x1064
state    : Maximized

$ log
2026-09-01T13:43:38.1849064-03:00  Resisted  Notepad test  4  Bloc de notas  \\.\DISPLAY4 Maximized; último rect observado (2992,-8)-(4928,1056) 1936x1064

$ GetWindowLong(GWL_STYLE) = 0x15CF0000
WS_CAPTION present: True
WS_THICKFRAME present: True
WS_MAXIMIZE present: True
```

Minimized (check 2c):

```
$ tools/probe --windows notepad
bounds   : (-32000,-32000)-(-31840,-31972) 160x28
state    : Minimized

$ log
2026-09-01T13:45:28.1647240-03:00  Applied  Notepad test  1  Bloc de notas  \\.\DISPLAY4 Minimized
```

Normal + rect (check 2d):

```
$ tools/probe --windows notepad
bounds   : (3200,150)-(3800,750) 600x600
state    : Normal

$ log
2026-09-01T13:45:54.7705064-03:00  Applied  Notepad test  1  Bloc de notas  \\.\DISPLAY4 Normal
```

Reversión de borderless (check 3):

```
$ cat borderless.json
[{"Handle":3410696,"ProcessId":17440,"ProcessStartTicks":639238671939007720,"OriginalStyle":349110272}]

$ python-ish: 349110272 = 0x14CF0000
WS_CAPTION in original: True
WS_THICKFRAME in original: True

# Tras reiniciar MonSelect.App con la ventana aún abierta:
$ cat borderless.json     # idéntico, el registro sobrevivió
$ GetWindowLong(GWL_STYLE) = 0x150B0000   # sigue sin WS_CAPTION: nadie lo revirtió

# Prueba manual del mecanismo (fuera del producto, usando el mismo primitivo
# que WindowPlacer.Revert() usaría):
SetWindowLong(hwnd, GWL_STYLE, 349110272)
SetWindowPos(hwnd, 0,0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_NOZORDER|SWP_FRAMECHANGED)
$ GetWindowLong(GWL_STYLE) → WS_CAPTION present: True, WS_THICKFRAME present: True
```

Config rota (check 4):

```
$ (harness standalone contra MonSelect.Core.dll)
RuleSetFormatException: rules.yaml no es YAML válido: While scanning a plain
scalar value, found invalid mapping.

# Con el YAML roto en disco, Notepad se sigue colocando con la última regla
# válida:
$ tools/probe --windows notepad
bounds   : (3100,100)-(3700,700) 600x600
state    : Normal

# Arreglado el archivo, hot-reload confirmado:
$ tools/probe --windows notepad
bounds   : (2992,-8)-(4928,1056) 1936x1064
state    : Maximized
```

Monitor desconectado (check 5):

```
$ log
2026-09-01T13:42:40.4439347-03:00  Skipped  Notepad test  0  Bloc de notas  el monitor 'phantom' no está conectado y la política es Skip

$ tools/probe --windows notepad
bounds   : (0,0)-(1920,1048) 1920x1048     # work area completo del primario, sin mover
state    : Normal
```

Electron/Qt (check 6):

```
# VLC (Qt5QWindowIcon) — no resistió
2026-09-01T13:48:33.3830604-03:00  Applied  VLC test  1  vlc  \\.\DISPLAY4 Normal
$ tools/probe --windows vlc
bounds   : (3200,150)-(3800,750) 600x600
state    : Normal

# Spotify (Chrome_WidgetWin_1, Electron/CEF) — resistió
2026-09-01T13:49:24.0521057-03:00  Resisted  Spotify test  4      \\.\DISPLAY4 Normal; último rect observado (3200,150)-(4000,750) 800x600
$ tools/probe --windows spotify
bounds   : (3200,150)-(4000,750) 800x600   # ancho forzado por Spotify, alto y posición sí obedecidos
state    : Normal
```

## Diferido al usuario: el check literal de RustDesk

No se tocó RustDesk en esta sesión. Para cerrar el criterio de F1 tal como está
escrito en el spec (§12: *"RustDesk con `--connect` abre en el monitor BenQ en
borderless, de forma repetible tras reinicio"*), el usuario debería, cuando le
convenga cortar la sesión remota actual:

1. `dotnet run --project src/MonSelect.App -- --diagnose`, conectar con
   RustDesk (`--connect <id>`), y copiar `exe`, `cmdline` y `class` exactos que
   imprime.
2. En `%APPDATA%\MonSelect\rules.yaml`, agregar una regla:
   ```yaml
   rules:
     - name: RustDesk
       match:
         exe: "<el exe impreso por --diagnose>"
         cmdline: "--connect"
       place:
         monitor: display4   # o el alias que corresponda al monitor BenQ real
         state: borderless
   ```
3. Arrancar `dotnet run --project src/MonSelect.App` (la app normal, sin
   `--diagnose`), cerrar RustDesk y reabrirlo con `--connect`.
4. Confirmar: aparece en el monitor correcto, sin barra de título, cubriendo
   la pantalla completa incluida la taskbar. Repetir 2–3 veces seguidas —
   dado el hallazgo del cuelgue silencioso de arriba, **vale la pena dejar
   MonSelect corriendo un rato y repetir la prueba después de 10–15 minutos**,
   no sólo inmediatamente después de arrancarlo, para confirmar que no se
   colgó en el medio.
5. Antes de cerrar la sesión: restaurar `rules.yaml` quitando la regla de
   prueba si no se la quiere dejar permanente.

## Estado final de la máquina

- Todas las apps lanzadas por esta verificación (Notepad, VLC, Spotify) están
  cerradas.
- `MonSelect.App.exe` no quedó corriendo.
- `rules.yaml` restaurado a los cuatro alias generados automáticamente, sin
  reglas de prueba ni el alias `phantom`.
- `borderless.json` de prueba eliminado (no quedaban ventanas vivas que
  referenciara).
- No se instaló la tarea de autostart.
- RustDesk sigue corriendo exactamente como estaba al empezar, sin tocar.
