# Motores de estrés

Notas de campo medidas en esta máquina y contrastadas con la fuente. Las
anclas `fichero:línea` están en `FUENTES.md`.

## Prime95 — receta que clava UN trabajador a UN núcleo

Versión: **30.19 build 20**. La receta vive en **un solo sitio**,
`scripts/prime95-recipe.txt`; todos los guiones la copian a
`tools/prime95/work/coreN/prime.txt`.

```
StressTester=1
V24OptionsConverted=1     <- sin esto sale el dialogo de bienvenida y SE QUEDA ESPERANDO
V30OptionsConverted=1
UsePrimenet=0
NumCores=1                <- LA CLAVE: fija HW_NUM_CORES=1 (commonc.c:487),
                             y -t arranca TortureCores=HW_NUM_CORES trabajadores
                             (Prime95Doc.cpp:1164). Sin ella: 16 trabajadores.
NumThreads=1
NumWorkers=1
CoresPerTest=1
EnableSetAffinity=0       <- que Prime95 no se fije afinidad; manda la del proceso
TortureHyperthreading=0
MinTortureFFT=4
MaxTortureFFT=32          <- FFT pequena, cabe en L2: castiga el nucleo, no la memoria
TortureMem=0
TortureTime=1             <- un solo autotest por longitud de FFT (commonb.c:7776)
AffinityVerbosityTorture=1
```

Lanzar `prime95.exe -t -W<directorio del nucleo>` y fijar al proceso la
máscara de afinidad `3 << 2N` (los dos lógicos del núcleo físico N).

**Comprobación obligatoria en cada arranque: contar las ventanas `Worker #N`
del proceso. Tiene que ser exactamente una.** Si no, la receta no manda y toda
medida posterior es de otra carga. `scripts/read-p95-window.ps1` las lista;
`diag-relaunch.ps1` aborta si no es una.

Esta receta es la de CoreCycler (`script-corecycler.ps1:7542-7634`), que
existe precisamente para hacer esto y la tiene depurada desde 2021.

### Lo que se probó antes y por qué falló

| Qué | Resultado | Por qué (fuente) |
|---|---|---|
| `Affinity=22` en `[Worker #1]` | Ignorado; 16 núcleos cargados | La tortura nunca lee `Affinity=`; solo `SET_PRIORITY_NORMAL_WORK` |
| `NumCores=1` con `Affinity=22` | Cae en el núcleo 0 | `Affinity=` seguía sin leerse; faltaba `EnableSetAffinity=0` + máscara del proceso |
| `TortureCores=1` + `EnableSetAffinity=0` + máscara | **Funcionaba** (26/08, `p95-pin4.ps1`) | `TortureCores` es la clave que `-t` lee directamente |
| Receta sin `TortureCores` ni `NumCores` (27/08, 00:07-00:46) | 16 trabajadores contra 2 lógicos | Se retiró `TortureCores` creyendo, por leer solo `commonb.c`, que no existía. Existe en `Prime95Doc.cpp:1164` |
| `NumWorkers=1` solo | Sin efecto sobre la tortura | `commonc.c:1797-1800` lo lee y lo reescribe, pero `-t` no lo usa |
| `ErrorCheck=1`, `SumInputsErrorCheck=1` (último commit de CoreCycler) | No entran | `ERRCHK` solo se usa en LL/PRP (`commonb.c:6564, 8859, 11781`); la tortura ya comprueba redondeo sin condición (`7713`) |

**Lección de proceso:** el 26/08 se leyó un solo fichero de la fuente y se
documentó una ausencia como hecho. Un guion que funcionaba se «corrigió» a
partir de ese documento. Ante una discrepancia entre un documento nuestro y un
guion que funcionó, se investiga; no se toca el guion.

### Qué escribe y cuándo

`results.txt` en el directorio del núcleo. Solo recibe el veredicto de cada
autotest, nunca progreso intermedio:

```
[Thu Aug 27 00:47:29 2026]
Self-test 4608 passed!          <- commonb.c:7202
Self-test 5K passed!
```

Fallos (`commonb.c:7194-7200`), siempre al mismo fichero vía `OutputBoth`:

```
TORTURE TEST FAILED on worker #1.
FATAL ERROR: Final result was XXXXXXXX, expected: YYYYYYYY.
Hardware failure detected running 4K FFT size, consult stress.txt file.
FATAL ERROR: Rounding was 0.5, expected less than 0.4
ERROR: ILLEGAL SUMOUT                      <- unico caso con reintento (goto restart_test)
Possible hardware failure, consult readme.txt file, restarting test.
```

Criterio de CoreCycler, que adoptamos: **línea nueva que contenga `error`**.

### Determinismo

La secuencia de longitudes de FFT no usa `rand()` (`commonb.c:8118-8135`) y con
`TortureTime=1` cada longitud ejecuta un único autotest. Con el directorio
borrado en cada arranque, **cada pasada ejecuta exactamente el mismo trabajo**.
Medido: primera línea siempre a los 40 s, 5 de 5 pasadas, con 16 trabajadores.
Por eso el tiempo hasta el primer autotest sirve como señal: si el trabajo es
el mismo y tarda más de 3× la línea base, algo va mal en el núcleo.

### Un lógico o los dos

Con 16 trabajadores, un solo lógico no progresaba. Eso ya no vale como dato:
hay que **re-medir con un trabajador**. CoreCycler ofrece ambos modos
(`assignBothVirtualCoresForSingleThread`, por defecto un lógico).

### Sobre medir la carga — corrección

Comparar vatios entre cargas distintas no mide estrés. El bucle escalar de
PowerShell a 5,4 GHz y Prime95 en AVX-512 consumen distinto haciendo trabajo
distinto. El indicador fiable es el **ritmo de `results.txt`** y, mejor aún,
el **tiempo hasta el primer autotest** frente a la línea base del mismo núcleo.

### Dos recetas de Prime95 y lo que cada una hace en esta máquina

| Receta | Fichero | Claves que cambian | Núcleo 11 a −5 |
|---|---|---|---|
| Pesada: small FFT, AVX-512 | `scripts/prime95-recipe.txt` | `MinTortureFFT=4`, `MaxTortureFFT=32` | 5,0 GHz, 1,08 V, 14 W |
| Ligera: SSE, FFT Huge | `scripts/prime95-recipe-sse-huge.txt` | `CpuSupportsAVX/AVX2/FMA3/AVX512=0`, `MinTortureFFT=8960`, `MaxTortureFFT=32768` | 5,0 GHz, 1,09 V, 13,6 W |

La ligera es la que CoreCycler usa por defecto (`readme.txt:132-140`) porque
en escritorio deja subir el boost. **Aquí no**: un tope de potencia por núcleo
(~14 W) clava ambas al mismo reloj. La suspensión periódica
(`diag-margin.ps1 -Suspender`, 1 s cada 10 s) añade transitorios hasta
5,24 GHz / 1,18 V, nada más.

El único motor que lleva el núcleo a fMax (5,45 GHz, 1,15 V, 9 W) es
**y-cruncher `04-P4P`** (SSE3, `diag-ycruncher.ps1`). `24-ZN5` (AVX-512)
se queda en 5,29 GHz / 10,5 W. Para probar el extremo alto de la curva V/F
en este chip, el motor ligero es `04-P4P`, no Prime95.

### Telemetría por núcleo — de dónde sale cada número

| Magnitud | Fuente | Nota |
|---|---|---|
| Reloj | LHM `Core #N` | reloj objetivo, no el real |
| Reloj efectivo | LHM `Core #N (Effective)` | promedia los **dos lógicos**: con un hilo cargado sale ~la mitad. Comparable entre márgenes, no con el reloj |
| Tensión | **tabla PM del SMU**, posición `317+N` | LHM no la da: `Core #N VID` es un único valor para los 16 |
| Frecuencia | tabla PM, `349+N` | GHz; coincide con el reloj de LHM |
| Potencia | tabla PM, `301+N` (= LHM `Core #N (SMU)`) | W |
| Temperatura | tabla PM, `333+N` | C |

Índices verificados el 27/08/2026 con tabla versión `0x621202` (613 floats),
por diferencia −5/−25 con un solo núcleo cargado (`scripts/pm-diff.ps1`).
`colab watch` los usa; `PmTable.cs` los documenta. Otra versión de tabla →
repetir la localización.

Una muestra aislada de reloj efectivo no vale (la ventana APERF/MPERF es la
que haya entre dos lecturas): `watch` descarta la primera y usa medianas.

## y-cruncher — SÍ se puede clavar a un núcleo (corrección; hecho en `diag-ycruncher.ps1`)

Versión probada: **v0.8.7.9547b**.

Lo medido el 26/08 sigue siendo cierto: con `start /affinity` y `stress`
reserva memoria para los 32 lógicos, y `-PF`/`-TD` solo existen en `bench`,
`benchio`, `bbp` y `custom`.

**Lo que se concluyó de ahí era falso.** CoreCycler lo clava: genera un
`stressTest.cfg` (`script-corecycler.ps1:1230, 1279`) y fija la afinidad del
proceso con `SetProcessAffinityMask` (`1793`). El binario para Zen 5 es
`24-ZN5 ~ Komari` (AVX-512; `510`, `527`, `1261`), elegido en
`Test-WhichYCruncherBinary` (`8418`). Las pruebas que CoreCycler considera más
duras: **SFTv4, FFTv4, N63**.

Pendiente: leer cómo construye el `.cfg` antes de escribir `YCruncherEngine`.

### PELIGRO — se queda esperando una tecla

Ante un parámetro inválido:

```
Invalid Parameter: -PF
Presione una tecla para continuar . . .
```

En un arnés desatendido eso es un cuelgue silencioso que parece una prueba en
curso. **Todo motor se lanza con la entrada estándar redirigida a NUL y con un
plazo máximo por encima de la duración pedida.** Sin excepción.

## Reparto de papeles

| | Motor | Por qué |
|---|---|---|
| **Por núcleo, tortura** | Prime95, FFT 4-32K, un trabajador | Estándar de la comunidad (CoreCycler). Determinista. Receta verificada en fuente |
| **Por núcleo, carga ligera** | Prime95 SSE, FFT Huge, con suspensión periódica | Perfil `low-load-scenario` de CoreCycler. La inestabilidad de CO clásica aparece a reloj bajo |
| **Chip completo, validación** | y-cruncher SFTv4/FFTv4/N63 | Matemática distinta, caza fallos distintos. Clavable si hiciera falta |
