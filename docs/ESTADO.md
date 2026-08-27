# Estado y siguiente paso

Última sesión: **27/08/2026, 22:50**. Plan completo en revisión 3
(`~/.claude/plans/serialized-sparking-bengio.md`; resumen de fases abajo).
**Empezar cada sesión leyendo este fichero y `FUENTES.md`.**

## Rutas

```
repo        C:\Users\ajustino\Proyectos\legion-co-lab      (NO ~/src)
binario     src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe
prime95     tools\prime95\                                   (ignorado por git)
recetas     scripts\prime95-recipe.txt (pesada)  scripts\prime95-recipe-sse-huge.txt (ligera)
y-cruncher  C:\Users\ajustino\Proyectos\corecycler\test_programs\y-cruncher\Binaries\<modo>.exe
guiones     scripts\                                         diagnostico, PowerShell
datos       runs\                                            ignorado; resumen en RESULTADOS.md
CoreCycler  C:\Users\ajustino\Proyectos\corecycler           clon de consulta (trae y-cruncher)
```

## Cómo está la máquina

```
CPU        -5 en los 16 nucleos  (all-core de la BIOS, base elegida). Verificado 22:45 (al terminar la Fase 1).
BIOS       Legion Optimization = Enabled · CPU Overclocking = Enabled
           All Core Curve Optimizer: signo −, magnitud 5 · PBO Scalar 1X
LLT        perfil en disco -3/-7, NO aplicado. No arranca solo.
           Si esta abierto, cerrarlo desde la bandeja (la X solo minimiza).
Procesos   ninguna herramienta de estres viva
```

Un reinicio devuelve siempre a −5 en los dieciséis. Esa es la red de seguridad.

## Lo que funciona

```
colab probe [--sensors] [--json f]   lee el margen PSM del hardware
colab apply --core N --margin M      escribe, camina el paso, verifica
colab reset --to -5                  devuelve los 16 a la base
colab watch --core N [--seconds S]   1 Hz: reloj, efectivo, V, GHz, W, T del nucleo (tabla PM)
scripts\diag-relaunch.ps1            N pasadas identicas SIN tocar el SMU; cuenta trabajadores; linea base
scripts\diag-margin.ps1 -Margin M [-Receta f] [-Suspender] [-Seconds S]
                                     Prime95 a un margen con watch en paralelo (escribe SMU, elevada)
scripts\diag-ycruncher.ps1 -Margin M -Modo '04-P4P'|'"24-ZN5 ~ Komari"' [-Suspender]
                                     y-cruncher clavado al nucleo, cfg generado, watch en paralelo (elevada)
scripts\fase1.ps1 [-Nucleos 0..15]   barrido: por nucleo 04-P4P + 24-ZN5 desde -50, +5 si canta; JSON por nucleo
scripts\fase0c.ps1 -Margin M         colab pone el margen, CoreCycler (config.ini del clon) prueba, colab restaura
scripts\pm-diff.ps1 -A m1 -B m2      compara tablas PM crudas de dos margenes
scripts\fase0.ps1                    control + escalera (escribe SMU, elevada)  [sin watch, receta pesada]
scripts\abort.ps1                    corta todo y restaura -5
scripts\read-p95-window.ps1          lista las ventanas de Prime95
```

Consola elevada: `Start-Process pwsh -Verb RunAs -ArgumentList '-NoExit','-File',<guion>,...`.
Un argumento con espacios (`24-ZN5 ~ Komari`) va con comillas dobles dentro
de las simples: `'-Modo','"24-ZN5 ~ Komari"'`; si no, pwsh lo parte.

## Hecho el 27/08

Mañana:
- Fase 0b (small FFT, 1 trabajador): −8 … −25, 3 × 180 s, sin positivo.
- `colab watch` + `PmTable.cs` + `pm-diff.ps1`: V/GHz/W/T por núcleo desde
  la tabla PM del SMU (v0x621202, `301/317/333/349 + N`).
- Contraste físico −5/−25: +160 MHz, −16 mV, 14 W constantes. El margen actúa.

Mediodía (plan revisión 3):
- Receta ligera SSE/Huge + suspensión periódica en `diag-margin.ps1`.
  Línea base −5 y escalera −25/−28/−30, 3 × 360 s: sin positivo. El régimen
  ligero **no sube el reloj aquí** (tope ~14 W por núcleo).
- `diag-ycruncher.ps1`: `04-P4P` y `24-ZN5` a −30, 360 s: sin positivo.
  **`04-P4P` es el único motor que lleva el núcleo a fMax** (5,45 GHz,
  1,15 V, 9 W).

Tarde (paso 6, decisiones del usuario):
- Núcleo 0 (CCD0) a −30 con `04-P4P`: limpio (5,15 GHz, 1,065 V, 7,3 W).
- Tope de `Safety` subido a −40 (`5bd41ac`) y luego a **−50** (`205697a`),
  mínimo del SMU. Núcleo 11 con `04-P4P`: −35, −40, −45, −50 **todos
  limpios**, tensión lineal hasta 1,076 V a 5,45 GHz, sin clock stretching.
- **Fase 0c hecha**: CoreCycler 0.11.0.4 en modo manual (`fase0c.ps1`,
  `config.ini` en el clon) sobre el núcleo 11 a −45: sin error, sin WHEA.
  Coincide con nuestro arnés. Requirió .NET Runtime 8 (instalado).

## Dónde estamos

El núcleo 11 pasa **todo el rango del SMU (−5 … −50)** en 6 min con el motor
que lo lleva a fMax, y CoreCycler dice lo mismo. Ningún positivo en 27/08.
El arnés mide bien (margen releído, tensión lineal, velocidad constante,
CoreCycler de acuerdo); lo que no hay es un fallo que cazar en 6 minutos.

Decisión del usuario (14:10): tiempo hoy, barrido mañana. Hecho el tiempo:
**30 min a −50 con `04-P4P`, limpio** (8 iteraciones, 24 `Passed`, WHEA 0).

Definición de «límite» que queda: **el primer margen (de −50 hacia arriba,
de 5 en 5) que pasa 6 min limpio con `04-P4P` y con `24-ZN5`**. Para el
núcleo 11 es −50. La validación de verdad es la Fase 3 (reposo, uso real,
WHEA), que es donde la literatura sitúa los fallos de CO.

## FASE 1 COMPLETA (14:45-16:46, 19:29-20:19, 20:26-22:39)

**Hay positivos.** Con `24-ZN5` (AVX-512) los núcleos del CCD0 fallan a −50
en 9-39 s y a −45 en 79-99 s (`Bottom word mismatch`); con `04-P4P` el
núcleo 0 se estrelló una vez a −50 (1/2). El núcleo 4 a −50 con `24-ZN5`
**reinició la máquina en frío** (16:46, Kernel-Power 41). WHEA 0.

```
CCD0   0:-40  1:-40  2:-40  3:-45  4:-45  5:-45  6:-45  7:-45
CCD1   8:-50* 9:-40 10:-50 11:-45 12:-45 13:-50 14:-50 15:-50     * WHEA 47
```

Tercera tanda (CCD1, desde −50): 9, 11 y 12 fallan a −50 con `24-ZN5` en
9-19 s (el 11 **no** pasa −50 con ambos motores; solo lo había pasado con
`04-P4P`). **Primer WHEA del proyecto**: id 47, corregido, componente
memoria, 20:33:37, durante el núcleo 8 a −50 con `24-ZN5` (prueba que pasó).
`fase1.ps1` no vigila WHEA; el −50 del 8 queda marcado. Sin más WHEA.
Un parón de 3 min a las 21:32 por la consola en modo selección (clic en la
ventana elevada): Esc y siguió.

Detalle en `RESULTADOS.md` («Fase 1»). `fase1.ps1` corregido dos veces
durante el barrido: (1) un crash del hijo ya no tumba el guion (hijo en
`pwsh` aparte); (2) `en-curso.json`: si al arrancar hay una prueba en curso,
fue un cuelgue → positivo y sigue un margen arriba.

Segunda tanda (19:29-20:19): núcleos 4-7 con `-Inicio -45`; los cuatro
limpios a la primera con ambos motores. En 5-7 **no se probó −50** (límite
por definición, sin positivo propio). Un arranque en falso antes: con
`-File`, `-Nucleos 4,5,6,7` entra como el entero 4567 (falla sin tocar el
SMU); hay que lanzar con `-Command`.

## Siguiente paso: decidir el perfil candidato y Fase 1b

Candidato propuesto = límite + 5 (las guías: "una o dos paradas por encima
del primer fallo"); el 8 tratado como −45 por el WHEA:

```
CCD0   0:-35  1:-35  2:-35  3:-40  4:-40  5:-40  6:-40  7:-40
CCD1   8:-40  9:-35 10:-45 11:-40 12:-40 13:-45 14:-45 15:-45
```

Decisión del usuario pendiente. Después, Fase 1b: aplicar el perfil a los
16 (`colab apply` por núcleo, `probe` de verificación), 30 min en reposo
+ WHEA, y la validación de Fase 3 (uso real ≥ 2 h, despertar de suspensión,
WHEA). Todo lo que se sabe fuera dice que el CO falla **en reposo y carga
ligera**, no bajo estrés (Kernel-Power 41 / `CLOCK_WATCHDOG_TIMEOUT` en
escritorio, al despertar, al arrancar un juego); CoreCycler no lo caza.

Pendientes de la Fase 1:
- −50 en 5, 6 y 7 (no probado; tabla del CCD0 no homogénea).
- Repetir el límite con los 8 tests de y-cruncher (`BKT, BBP, SFTv4, SNT,
  SVT, FFTv4, N63, VT3`, CoreCycler `default.config.ini:345`); usamos 3.
  `BKT` es entero escalar (sin AVX), la carga más ligera: en una máquina
  tapada a 14 W es la que más sube el reloj.
- Vigilar WHEA (ids 17-20, 46, 47) dentro de `fase1.ps1`/el corredor, no
  solo a mano al final.
- BIOS SMCN20WW (capturas `Desktopbios`, 25-26/08): Performance Tuning solo
  expone CPU Overclocking, PBO Scalar, Max CPU Boost Clock Override, All Core
  Curve Optimizer Sign/Magnitude (tope -5), Smart Power, Silent Performance
  Mode, CTCL Control. **Ni Curve Shaper ni CO por núcleo**: todo por SMU.

## Después

```
3    Prime95Engine + YCruncherEngine en C# + panel en vivo (watch es la base)
5    JSONL + SQLite + colab report (+ baselines por nucleo y receta)
6    corredor: plan.json, latido, Hang, --auto-resume (tarea programada, .automode)
1    barrido por CCD: regimen primario 04-P4P + suspension, 6 min/nucleo/nivel;
     secundario 24-ZN5 y small FFT sobre el candidato        ~4-8 h de maquina
1b   soak 30 min en reposo + WHEA sobre el candidato   OBLIGATORIO
2    refinamiento por nucleo (opcional)
3    validacion larga: y-cruncher chip completo + uso real >= 2 h + WHEA
```

Pendiente de re-medir con un trabajador: un lógico frente a dos
(`assignBothVirtualCoresForSingleThread` en CoreCycler).

## Cabos sueltos fuera de este repositorio

- Validación larga del ganador de GPU (+300 / tope 875 mV): OCCT 60 min, OCCT
  Switch 30 min, 3× Steel Nomad y ≥2 h de juego. Afterburner con `-Profile1`.
- Línea base OCCT Steady de serie para la GPU.
- Verificar qué CCD apaga el «modo X3D» de LLT.
- El preset «Min» de God Mode en LLT sigue siendo un peligro si se entra en
  modo Custom.
- PR a Legion Toolkit: exponer la lectura de hardware que
  `LoadFromHardwareAsync` descarta.
