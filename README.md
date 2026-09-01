# MonSelect

Reglas por aplicación para decidir **en qué monitor se abre cada ventana y con qué estado**, en Windows 11.

Windows no permite configurar esto: sólo tiene una heurística de "recordar la última posición" que es poco fiable y no cubre el estado maximizado. MonSelect lo reemplaza por reglas declarativas.

## Qué hace

Cuando aparece una ventana, MonSelect la identifica y la coloca según la primera regla que matchee.

**Identificación** — por exe path, command line, window class, título (regex) o AppUserModelID, combinables.

**Colocación** — monitor de destino, más uno de cuatro estados:

| Estado | Resultado |
|---|---|
| `normal` | Rect exacto que vos definís |
| `maximized` | Maximizada respetando la taskbar |
| `minimized` | Arranca minimizada |
| `borderless` | Sin bordes, cubriendo el monitor completo, tapando la taskbar |

El command line como criterio permite, por ejemplo, mandar cada sesión de RustDesk a un monitor distinto según a qué máquina se conecte.

## Ejemplo

```yaml
rules:
  - name: RustDesk EJEMPLO-01
    match:
      exe: "C:/Program Files/RustDesk/rustdesk.exe"
      cmdline: "--connect 123456789"
    place:
      monitor: benq
      state: borderless
```

## Estado

En diseño. El documento completo está en
[`docs/superpowers/specs/2026-09-01-monselect-design.md`](docs/superpowers/specs/2026-09-01-monselect-design.md).

| Fase | Alcance | Estado |
|---|---|---|
| F1 | Motor, reglas YAML, hook, tray | pendiente |
| F2 | GUI y hotkeys globales | pendiente |
| F3 | Zonas custom por monitor | pendiente |

## Alcance

*Exclusive fullscreen* real no es forzable desde un proceso externo — requiere que la propia aplicación pida ownership del display al driver. Lo que MonSelect entrega es *borderless fullscreen*, que es lo que la mayoría de las apps llaman "pantalla completa".

## Stack

C# / .NET 10, WPF, app de bandeja.

## Licencia

MIT
