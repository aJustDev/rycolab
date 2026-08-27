# Estado y siguiente paso

Última sesión: **27/08/2026, 11:30**. Plan completo en revisión 2 (ver el
resumen de fases abajo). **Empezar cada sesión leyendo este fichero y
`FUENTES.md`.**

## Rutas

```
repo        C:\Users\ajustino\Proyectos\legion-co-lab      (NO ~/src)
binario     src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe
prime95     tools\prime95\                                   (ignorado por git)
receta      scripts\prime95-recipe.txt                       UNICA copia
guiones     scripts\                                         diagnostico, PowerShell
datos       runs\                                            ignorado; resumen en RESULTADOS.md
CoreCycler  C:\Users\ajustino\Proyectos\corecycler           clon de consulta
```

## Cómo está la máquina

```
CPU        -5 en los 16 nucleos  (all-core de la BIOS, base elegida). Verificado 11:02.
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
scripts\diag-margin.ps1 -Margin M    N pasadas a un margen con watch en paralelo (escribe SMU, elevada)
scripts\pm-diff.ps1 -A m1 -B m2      compara tablas PM crudas de dos margenes (localiza posiciones)
scripts\fase0.ps1                    control + escalera (escribe SMU, elevada)  [sin watch]
scripts\abort.ps1                    corta todo y restaura -5
scripts\read-p95-window.ps1          lista las ventanas de Prime95
```

Consola elevada: `Start-Process pwsh -Verb RunAs -ArgumentList '-NoExit','-File',<guion>`.

## Hecho el 27/08 (mañana)

- **Fase 0b ejecutada**: −8, −11, −14, −17, −20, −23, −25, 3 × 180 s cada
  uno, un trabajador. **Sin positivo**: todas 10 líneas / primera 20 s, sin
  error, WHEA 0. El mudo de −8 de la noche era de los 16 trabajadores.
- `colab watch` + `PmTable.cs`: tensión, GHz, W y T **por núcleo** desde la
  tabla PM del SMU (v0x621202, índices `301/317/333/349 + N`, localizados
  por diferencia −5/−25 con `pm-diff.ps1`).
- **Contraste físico −5/−25**: +160 MHz, −15,7 mV, misma potencia (14 W).
  El margen actúa; el núcleo está limitado por potencia y el CO se convierte
  en reloj. Datos en `RESULTADOS.md`.

## Lectura de la Fase 0b

La puerta decía: «sin positivo a −25 → el motor no detecta». Lo que se ha
visto es distinto: el margen actúa (medido) y el núcleo aguanta −25 **bajo
tortura a plena carga**. Ese régimen no es donde el CO suele romper; la
inestabilidad clásica aparece en reposo y carga ligera (reloj y corriente
bajos), que es la Fase 1b, aún sin medir.

Hasta −25 no hay evidencia de que **Prime95 small FFT detecte nada** en
este chip. Que el detector funcione (cante un error) sigue sin demostrarse.

## Siguiente paso: FASE 0b' — buscar el positivo donde puede estar

Escribe en el SMU. Consola elevada. Pedir confirmación antes de cada nivel.

1. **Bajar más, en el mismo régimen**: −28 y −30 (tope de `Safety`), 3 × 180 s
   con `diag-margin.ps1`. Si canta error → detector validado; anotar V/GHz.
2. **Cambiar de régimen** (lo que hace CoreCycler para esto): Prime95 con
   `suspendPeriodically` (SuspendThread ~1 s por tick, `script-corecycler.ps1:3343-3475`)
   y perfil `low-load-scenario` (SSE, FFT Huge). Requiere adaptar la receta
   y añadir la suspensión periódica a `diag-margin.ps1` o hacerlo ya en el
   `Prime95Engine` (Paso 3). Probar en −25 (donde ya sabemos que la tortura
   pasa) y bajar.
3. **Contraste con y-cruncher** (Fase 3 adelantada, un núcleo): binario Zen 5
   `24-ZN5 ~ Komari`, pruebas SFTv4/FFTv4/N63, `stressTest.cfg` generado +
   afinidad (CoreCycler 1230-1290, 8418-8440).
4. Si nada de lo anterior da positivo a −30: el gate se cierra con «este
   núcleo no falla en ningún régimen medido hasta el tope de seguridad», y
   la Fase 1 barre con detector no validado pero con telemetría física por
   nivel. Decisión del usuario.

Sin cambios en el detector hasta tener un positivo real que lo calibre.

## Después

```
0c   contraste con CoreCycler (modo manual, mismo nucleo, mismos niveles)   ~1 h
3    Prime95Engine en C# + panel en vivo (watch ya es la base de telemetria)
5    JSONL + SQLite + colab report (+ tabla baselines)
6    corredor: plan.json, latido, Hang, --auto-resume (tarea programada, .automode)
1    barrido por CCD                                    ~4-8 h de maquina
1b   regimen de reposo: suspendPeriodically + SSE Huge + soak 30 min   OBLIGATORIO
2    refinamiento por nucleo (opcional)
3    validacion larga: y-cruncher SFTv4/FFTv4/N63 + WHEA + uso real   8-24 h
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
