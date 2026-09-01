# ¿En qué coordenadas vive WINDOWPLACEMENT.rcNormalPosition?

**Fecha:** 2026-09-01
**Método:** `dotnet run --project tools/probe -- --placement`

Desviación respecto del brief original: el probe no depende de un humano moviendo
una ventana y dándole foco. Lanza una aplicación real como proceso hijo, espera a
que su ventana principal exista y sea visible (polling con timeout), la mueve con
`SetWindowPos` a un rect conocido bien adentro del monitor bajo prueba dejándola en
estado restaurado, lee `GetWindowRect` y `GetWindowPlacement`, y cierra el proceso
al terminar (`try`/`finally`, incluso si algo falla). Se repitió con dos
aplicaciones para no depender de las rarezas de un solo programa.

## Aplicaciones usadas

- **Notepad** (`notepad.exe`), la que pedía el brief.
- `mspaint.exe` **no está disponible en este equipo** (`Process.Start` falla con
  "el sistema no puede encontrar el archivo especificado"). El probe hace
  fallback automático a otro candidato y lo deja registrado en su salida.
- Antes de asentarse en el candidato final se descartaron dos:
  - `calc.exe`: su `rcNormalPosition` queda **congelado** en el tamaño/posición
    de lanzamiento (320x532) y no sigue a `SetWindowPos`, aunque `GetWindowRect`
    sí cambia. Es una rareza de la app empaquetada (Calculadora moderna), no una
    señal sobre el espacio de coordenadas — habría contaminado la conclusión.
  - `cmd.exe`: la ventana de consola pertenece al proceso `conhost.exe`, no a
    `cmd.exe`, lo que rompe la heurística de búsqueda de ventana por nombre de
    imagen.
  - **Segunda aplicación usada: `regedit.exe`** (Editor del Registro de Windows),
    una ventana Win32 clásica que es dueña directa de su propio HWND.

## Resultado

Monitores probados: `\\.\DISPLAY4` (no primario, con taskbar) y `\\.\DISPLAY2`
(vive enteramente en Y negativa, `bounds=(0,-1080)-(1920,0)`,
`work=(0,-1080)-(1920,-32)`). En ambos, el offset `WorkArea.Left - Bounds.Left` y
`WorkArea.Top - Bounds.Top` es `(0, 0)` — la taskbar en este equipo recorta el
borde inferior de cada monitor, no el superior ni el izquierdo.

| Aplicación | Monitor | GetWindowRect | rcNormalPosition | delta (left, top) |
|---|---|---|---|---|
| notepad.exe | `\\.\DISPLAY4` | (3100,100)-(3700,500) | (3100,100)-(3700,500) | (0, 0) |
| notepad.exe | `\\.\DISPLAY2` (origen negativo) | (100,-980)-(700,-580) | (100,-980)-(700,-580) | (0, 0) |
| regedit.exe | `\\.\DISPLAY4` | (3100,100)-(3700,500) | (3100,100)-(3700,500) | (0, 0) |
| regedit.exe | `\\.\DISPLAY2` (origen negativo) | (100,-980)-(700,-580) | (100,-980)-(700,-580) | (0, 0) |

Las cuatro mediciones son idénticas entre `GetWindowRect` y `rcNormalPosition`,
en las dos aplicaciones, en los dos monitores, incluido el de origen negativo.
El resultado se reprodujo de forma idéntica en dos corridas completas.

## Conclusión

- **Coordenadas de pantalla.** `PlacementCalculator` usa los rects tal cual,
  sin corrección. La constante `WorkspaceOffsetApplies` es `false`.

## Por qué importa

Un error acá desplaza cada ventana colocada por el alto de la taskbar (32 px en
este equipo) sin que nada falle de forma visible.

## Nota sobre el alcance de esta medición

En este equipo la taskbar recorta el borde **inferior** de cada monitor (todos
los `WorkArea` coinciden con `Bounds` en `Left`/`Top`; solo cambia `Bottom`). Un
desplazamiento de coordenadas de workspace que sólo se manifestara cuando la
taskbar está anclada arriba o a la izquierda de un monitor no habría sido
detectable con este layout, sin importar cuál fuera la verdad. Dicho esto, la
medición sí es concluyente para la pregunta que Task 8 necesita responder: con
`GetWindowRect` y `rcNormalPosition` coincidiendo exactamente en dos
aplicaciones distintas, en un monitor con taskbar y en el monitor de origen
negativo, no hay evidencia de que Windows aplique ninguna corrección de
coordenadas al escribir `rcNormalPosition` en este sistema operativo (Windows
11). Si se quisiera cerrar también el caso de taskbar anclada a la izquierda,
haría falta repetir la medición con esa configuración; no se consideró
necesario para esta task porque ninguno de los cuatro monitores reales del
equipo la usa.
