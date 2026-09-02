# MonSelect

Decide **en qué monitor se abre cada aplicación y con qué estado**, en Windows 11.

Windows no permite configurar esto. Sólo tiene una heurística de "recordar la última posición" que es poco fiable y no cubre el estado maximizado. MonSelect lo reemplaza por reglas que definís vos.

## Instalación

Descargá `MonSelect.App.exe` de la [última release](https://github.com/knupson/MonSelect/releases), dejalo donde quieras y ejecutalo. No necesita instalador ni tener .NET instalado.

Para que arranque con Windows:

```
MonSelect.App.exe --install-autostart
```

Registra una tarea programada al iniciar sesión, con privilegios elevados — sin eso no puede mover ventanas de aplicaciones que corren como administrador. Se revierte con `--uninstall-autostart`.

## Uso

Al arrancar aparece un icono en la bandeja. Desde ahí se abre la ventana de gestión, o directamente:

```
MonSelect.App.exe --gui
```

**Para crear una regla:** en *Ventanas abiertas*, elegí la aplicación, acomodá su ventana donde la querés, y confirmá. La regla se escribe desde donde quedó.

La pestaña *Reglas* muestra cuántas ventanas abiertas matchea cada regla. Un cero significa que esa regla no le aplica a nada — es la forma rápida de ver que algo quedó mal escrito.

### Comandos

| Comando | Qué hace |
|---|---|
| `--gui` | Abre la ventana de gestión al arrancar |
| `--apply-now` | Reubica las ventanas abiertas y termina |
| `--diagnose` | Imprime exe, command line, clase y estado de cada ventana que aparece |
| `--install-autostart` | Arranca con Windows |
| `--uninstall-autostart` | Deja de arrancar con Windows |

## Cómo se identifica una ventana

Cada regla combina los criterios que definas. Todos son opcionales y se aplican en conjunto:

| Criterio | Qué distingue |
|---|---|
| `exe` | Qué programa |
| `class` | Qué tipo de ventana dentro del programa — separa la ventana real de las auxiliares |
| `cmdline` | Qué instancia — permite mandar dos sesiones del mismo programa a monitores distintos |
| `title` | Qué documento o sesión, cuando lo anterior no alcanza |

Los tres primeros no cambian mientras el proceso viva. El título sí cambia — notificaciones, nombres de archivo, banners — así que la captura no lo incluye salvo que se lo pidas.

Cuando varias reglas matchean, gana la primera del archivo. Sin puntajes ni desempates: el orden lo decidís vos y se ve en la pestaña *Reglas*.

## Estados

| Estado | Resultado |
|---|---|
| `normal` | Rect exacto, en coordenadas visibles |
| `maximized` | Maximizada respetando la taskbar |
| `minimized` | Arranca minimizada |
| `borderless` | Sin bordes, cubriendo el monitor completo, tapando la taskbar |

*Exclusive fullscreen* real no está: requiere que la propia aplicación se lo pida al driver, y ningún programa externo puede forzarlo. `borderless` es lo que la mayoría de las aplicaciones llaman "pantalla completa".

## Configuración

`%APPDATA%\MonSelect\rules.yaml`. Se puede editar a mano y se recarga sola al guardar. Si tiene un error de sintaxis, las reglas anteriores siguen en vigor y el error aparece en la ventana.

```yaml
rules:
  - name: "Discord"
    match:
      exe: "C:/Users/vos/AppData/Local/Discord/app-1.0.9255/Discord.exe"
      title: "Discord$"
    place:
      monitor: display3
      state: normal
      rect: [1920, -842, 3000, 102]
    bleed: 1
```

Los monitores se referencian por alias. El bloque `monitors:` se genera solo en el primer arranque, con un identificador estable por pantalla — no el índice, que Windows reasigna al reconectar.

### `bleed`

Algunas aplicaciones dibujan su propio borde de 1px dentro de su ventana, y eso deja ver el escritorio entre dos ventanas pegadas. `bleed` lo compensa: `auto` lo mide solo, `0` no compensa, o un número fijo. Así la coordenada del rect sigue diciendo la verdad y la compensación queda declarada aparte.

## Compilar

Requiere el SDK de .NET 10.

```
dotnet test
dotnet publish src/MonSelect.App/MonSelect.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

## Diseño

El documento de diseño está en [`docs/superpowers/specs/`](docs/superpowers/specs/), con las mediciones sobre las que se apoya y las correcciones de lo que resultó estar mal. Los hallazgos empíricos — en qué coordenadas vive `WINDOWPLACEMENT`, por qué el serial EDID no sirve como identificador de monitor — están en [`docs/superpowers/findings/`](docs/superpowers/findings/).

## Qué hace en tu sistema

Conviene saberlo antes de ejecutarlo, porque no es una aplicación corriente:

- **Instala un hook global de Windows** (`SetWinEventHook`) para enterarse cuando aparece una ventana. Recibe eventos de todas las aplicaciones, no sólo de las que tienen regla.
- **Lee la línea de comandos de otros procesos**, leyendo su PEB. Es lo que permite mandar dos sesiones del mismo programa a monitores distintos. Los procesos elevados o de otro usuario no se pueden leer, y sus reglas por `cmdline` simplemente no matchean.
- **Mueve y redimensiona ventanas ajenas**, y en estado `borderless` les modifica el estilo. El estilo original se guarda para poder revertirlo.
- **Escribe** en `%APPDATA%\MonSelect\` — reglas, log y estilos originales. Nada sale de la máquina: no hay telemetría ni conexiones de red.
- Con `--install-autostart` registra una tarea programada con privilegios elevados, necesaria para tocar ventanas de aplicaciones que corren como administrador.

El ejecutable **no está firmado**, así que Windows SmartScreen lo va a marcar como desconocido la primera vez: *Más información → Ejecutar de todas formas*. Si preferís no confiar en un binario sin firmar, compilalo vos con las instrucciones de arriba.

## Licencia

MIT — ver [`LICENSE`](LICENSE).

El ejecutable publicado es autocontenido e incluye el runtime de .NET y YamlDotNet, ambos MIT. Sus avisos de copyright están en [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

Sin garantía de ningún tipo, como dice la licencia. Es una herramienta que manipula ventanas de otros programas; probala con algo que no te importe antes de confiarle tu escritorio de trabajo.
