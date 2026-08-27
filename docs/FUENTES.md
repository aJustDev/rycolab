# Fuentes

Regla: **la fuente manda sobre la memoria y sobre nuestros propios documentos.**
Antes de afirmar cómo se comporta un motor o una herramienta, leer el
repositorio y citar `fichero:línea`. Un guion que funcionó pesa más que un
documento que dice lo contrario: se investiga la discrepancia, no se «corrige»
el guion.

## Repositorios

| Proyecto | Repositorio | Copia local |
|---|---|---|
| Prime95 | `shafferjohn/Prime95` | — (bajar con `gh api`) |
| CoreCycler | `sp00n/corecycler` (rama `master`) | `C:\Users\ajustino\Proyectos\corecycler` |
| y-cruncher | `Mysticial/y-cruncher` | — (formato de `.cfg`, lista de pruebas, binarios por arquitectura) |
| Legion Toolkit | `BartoszCichecki/LenovoLegionToolkit` | — |
| ZenStates.Core | `irusanov/ZenStates-Core` | paquete NuGet 1.0.1 |
| LibreHardwareMonitor | `LibreHardwareMonitor/LibreHardwareMonitor` | paquete NuGet 0.9.7-pre689 |

Bajar un fichero concreto sin clonar:

```
gh api -H "Accept: application/vnd.github.raw" repos/<owner>/<repo>/contents/<ruta> > fichero
```

Si se va a consultar más de dos veces, clonar a `~/Proyectos/`.

## Anclas verificadas

### Prime95 (30.19 b20, fuente de `master` a 27/08/2026)

| Qué | Dónde |
|---|---|
| `-t` arranca la tortura con `TortureCores` trabajadores, por defecto `HW_NUM_CORES` | `prime95/Prime95Doc.cpp:1162-1168` (`OnUsrTorture`) |
| `NumCores` en `prime.txt` fija `HW_NUM_CORES` | `commonc.c:487` |
| Desde 30.10b5 `local.txt` se fusiona en `prime.txt` | `commonc.c:1377`, `1409` |
| `NumWorkers` se lee y se **reescribe** en `prime.txt` al arrancar | `commonc.c:1797-1800` |
| `ErrorCheck` solo afecta al trabajo LL/PRP, **no** a la tortura | `commonc.c:1795`; usos en `commonb.c:6564, 8859, 11781`; ausente en `selfTestInternal` |
| Texto de aprobado `Self-test %i%s%s passed!` | `commonb.c:7202` |
| Textos de fallo (`FATAL ERROR`, `ILLEGAL SUMOUT`, `Hardware failure`, `Rounding was`, `TORTURE TEST FAILED`) | `commonb.c:7194-7200` |
| Comprobación de redondeo incondicional en la tortura (`> 0.45` → `STOP_FATAL_ERROR`) | `commonb.c:7713-7726` |
| Reintento solo tras `ILLEGAL SUMOUT`, y escribe dos líneas antes | `commonb.c:7695-7709` (`goto restart_test`) |
| Residuo final comparado con tabla precomputada | `commonb.c:7747-7762` |
| `TortureTime=1` → un solo autotest por longitud de FFT | `commonb.c:7776` |
| Elección de FFT **determinista** (sin `rand()`) | `commonb.c:8118-8135` |
| `EnableSetAffinity=0` → no fija afinidad | `commonb.c` (`SET_PRIORITY_TORTURE` / `IniGetInt "EnableSetAffinity"`) |
| La tortura nunca lee `Affinity=`; eso es solo `SET_PRIORITY_NORMAL_WORK` | `commonb.c` (`case SET_PRIORITY_NORMAL_WORK`) |
| `torture_core_num = thread_num` (trabajador N → núcleo N) | `commonb.c` (`tortureTest`) |
| Opciones documentadas de afinidad y `EnableSetAffinity` | `tools/prime95/undoc.txt:760-790` |

### CoreCycler (`script-corecycler.ps1`, `a95b523`)

| Qué | Dónde |
|---|---|
| Receta de `prime.txt`: `NumCores`, `NumThreads`, `NumWorkers`, `CoresPerTest`, `EnableSetAffinity=0`, `TortureHyperthreading=0` | `7542-7634` |
| Afinidad la fija el propio guion (`SetProcessAffinityMask` / afinidad de hilo) | `1793`, `10854` |
| Detección de error en Prime95: línea nueva que contenga `error` | `9952` |
| Atasco: uso de CPU del proceso por debajo de lo esperado, 3 comprobaciones | `9611-9614`, `9815-9859` |
| WHEA: evento 19, contrastar APIC ID con el núcleo probado | `427-440`, `11432` |
| `suspendPeriodically`: `SuspendThread` ~1 s por tick, fuerza transiciones de carga | `3343-3475`, `198`, `6117` |
| Modo automático Ryzen: escritor `ryzen-smu-cli` (requiere PawnIO) | `179`, `728`, `5358` |
| CO mínimo −50 en Ryzen 7000+; ~3-5 mV por cuenta | `default.config.ini:760-761` |
| Estado `.automode` + tarea programada al inicio de sesión para reanudar | `4347-4517`, `helpers/automode-startup-script.ps1` |
| y-cruncher clavado a un núcleo: `stressTest.cfg` generado + afinidad | `1230`, `1279`, `8418-8440` |
| Binario Zen 5: `24-ZN5 ~ Komari` (AVX-512) | `510`, `527`, `1261` |
| Perfil de carga ligera: SSE, FFT Huge, `suspendPeriodically=1` | `configs/low-load-scenario.Prime95.config.ini` |
| Modo automático de ejemplo: y-cruncher SFTv4/FFTv4/N63, 1 hilo, +1 por error | `configs/Ryzen.AutomaticTestMode.Start.ini` |
| Valores por defecto: 6 min/núcleo, 15 s entre núcleos, `numberOfThreads=1` | `default.config.ini:79, 136, 197` |
| Receta SSE de Prime95: `CpuSupportsAVX=0`, `AVX2=0`, `FMA3=0`, `AVX512=0` (SSE/SSE2 = 1) | `script-corecycler.ps1:7105-7110` |
| «Huge» = 8960K a MAX (32768K en SSE); `TortureMem=0`, `TortureTime=1` también | `default.config.ini:256`; `script-corecycler.ps1:285, 469, 7616-7617` |
| Por qué SSE y no AVX: la carga ligera deja subir el boost y encuentra fallos que AVX «simply cannot» | `readme.txt:132-140` |
| Suspensión: `SuspendThread`/`ResumeThread` en todos los hilos, 1000 ms cada `tickInterval` = 10 s | `script-corecycler.ps1:1813-1818, 3473-3475, 3628`; `default.config.ini:838, 884, 896` |
| y-cruncher: binarios en `test_programs/y-cruncher/Binaries/<modo>.exe`; `04-P4P` ligero, `19-ZN2`/`24-ZN5` pesado | `default.config.ini:274-326` |
| Plantilla de `stressTest.cfg` (`Action StressTest`, `LogicalCores`, `TotalMemory`, `SecondsPerTest`, `StopOnError`, `Tests`) | `script-corecycler.ps1:8568-8603` |
| Línea de comandos de y-cruncher: `priority:-1 config <cfg>`; `pause:-2 colors:0` para que no espere tecla | `script-corecycler.ps1:1237, 8421` |
| Valores de referencia 7945HX por núcleo (−24 … −49) | github.com/seerge/g-helper/discussions/736 |

### Legion Toolkit

| Qué | Dónde |
|---|---|
| Lee el margen del hardware y lo descarta tres líneas después | `LoadFromHardwareAsync` |
| Los CCD se numeran desde 0 en la interfaz | `HeaderTitle = $"CCD {currentCcdIndex}"` |
| Interruptor silencioso `DoNotApply` tras 3 apagados anómalos | `THRESHOLD = 3` |
| Comprueba CA antes de aplicar | `Power.IsPowerAdapterConnectedAsync` |

### ZenStates.Core

| Qué | Dónde |
|---|---|
| Lectura/escritura del margen PSM por núcleo | `GetPsmMarginSingleCore(uint)`, `SetPsmMarginSingleCore(uint,int)` |
| Máscara de núcleo `((ccd << 8) \| core) << 20` (APU: índice plano) | `CoreMask` en `Topology.cs`, copiado de LLT |
| Tabla de potencia del SMU: `Cpu.RefreshPowerTable()`, floats crudos en `Cpu.powerTable.Table`, versión en `Cpu.smu.TableVersion` (NuGet 1.0.1; en `master` es `RyzenSmu.PmTableVersion`) | `Cpu.cs:1147`, `PowerTable.cs:505` |
| `PowerTable` solo interpreta FCLK/MCLK/UCLK/VDDCR_SOC/CLDO_*; **nada por núcleo** | `PowerTable.cs:500-560` |

### Tabla PM del 9955HX3D (versión `0x621202`, 613 floats) — localizado aquí, no en ninguna fuente

| Posición | Qué | Cómo se verificó (27/08/2026) |
|---|---|---|
| `301+N` | potencia del núcleo N (W) | igual a LHM `Core #N+1 (SMU)` |
| `317+N` | **tensión** del núcleo N (V) | 1,0832 → 1,0675 al pasar de −5 a −25 solo en N=11 |
| `333+N` | temperatura del núcleo N (C) | igual a Tctl con un núcleo cargado |
| `349+N` | frecuencia del núcleo N (GHz) | igual a LHM `Core #N+1` |

Método: `colab watch --raw` en dos márgenes, `scripts/pm-diff.ps1`. Con otra
versión de tabla, repetir.

### Referencias externas leídas el 27/08/2026 (investigación durante la Fase 1)

| Qué | Dónde |
|---|---|
| CoreCycler corre 8 tests de y-cruncher por defecto: `BKT, BBP, SFTv4, SNT, SVT, FFTv4, N63, VT3`; tabla de carga CPU/Mem por test (`BKT` entero escalar, `BBP`/`SVT`/`SNT` en caché) | clon `configs/default.config.ini:330-345` |
| "Use 04-P4P for low load testing and 19-ZN2 for higher/AVX2"; "It is unclear yet how Zen 5 / Ryzen 9000 CPUs will turn out" | `configs/default.config.ini:314-318` |
| 9950X3D: CO por CCD −25 (V-Cache) / −20; Curve Shaper Low/Med −30, High −25, Max −10; fMax 5550 (V-Cache) vs 5750; cuatro cargas: OCCT memoria (ligera), y-cruncher BKT (ligera), y-cruncher bench (AVX), OCCT AVX (pesada) | skatterbencher.com/2025/03/11/skatterbencher-85-ryzen-9-9950x3d-overclocked-to-5900-mhz/ |
| "an unstable undervolt usually crashes at idle or light load, not under an all-core stress test"; `CLOCK_WATCHDOG_TIMEOUT`; ~1,13 V a 5,1 GHz all-core frente a ~1,40 V VID en boost ligero | techfuelhq.com/articles/9800x3d-undervolt-guide-2026/ |
| "Stop one or two steps above your first instability, not at it" | msi.com/blog/how-to-use-curve-optimizer-to-lower-ryzen-9-9950x3d-temperatures-and-boost-performance |
| Curve Shaper: no bajar el punto Min Frequency (afecta a la tensión de reposo) | SkatterBencher #85 (arriba) |
| Sin datos publicados del 9955HX3D ni del 16AFR10H | búsquedas 27/08/2026 |
