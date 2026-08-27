# Estado y siguiente paso

Última sesión: **27/08/2026, 13:00**. Plan completo en revisión 3
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
CPU        -5 en los 16 nucleos  (all-core de la BIOS, base elegida). Verificado 12:50.
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

## Dónde estamos

El núcleo 11 pasa **−30** (tope de seguridad del arnés) en los cuatro
regímenes medidos, incluido el de fMax. No hay positivo con el que calibrar
el detector. Todo está en `RESULTADOS.md` («Resumen del núcleo 11»).

## Siguiente paso: PASO 6 — decisión del usuario (pendiente)

Opciones sobre la mesa:

a) Subir el tope de `Safety` a −35 y luego −40, **solo** con `04-P4P`
   (régimen de fMax) y con `24-ZN5`, 360 s, 3 pasadas, telemetría. Buscar el
   positivo donde la física dice que está. CoreCycler admite −50 en Ryzen
   7000+; usuarios de 7945HX reportan hasta −49 por núcleo.
b) Cerrar la puerta como «el núcleo 11 no falla hasta −30 en ningún régimen
   medido» y pasar a Fase 1 (barrido por CCD) con tope −30, detector no
   calibrado pero telemetría física por nivel; validar después con y-cruncher
   largo, soak en reposo y uso real.
c) Probar otro núcleo antes de decidir: el detector puede calibrarse en un
   núcleo peor. CoreCycler y g-helper muestran dispersión de 20 cuentas
   entre núcleos del mismo chip. Candidato: los que LLT tenía a −3 (CCD0,
   V-Cache), p.ej. núcleo 0, con `04-P4P` a −25/−30.

Recomendación escrita en la sesión: **c) primero** (barato, 15 min, no toca
el tope), y según salga, a) o b).

## Después

```
0c   contraste con CoreCycler (modo manual, mismo nucleo, mismos niveles)   ~1 h
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
