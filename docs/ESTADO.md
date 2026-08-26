# Estado y siguiente paso

Última sesión: **27/08/2026, 01:30**. Plan completo en revisión 2 (ver el
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
CPU        -5 en los 16 nucleos  (all-core de la BIOS, base elegida). Verificado 01:23.
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
scripts\diag-relaunch.ps1            N pasadas identicas SIN tocar el SMU; cuenta trabajadores; linea base
scripts\diag-margin.ps1 -Margin M    N pasadas a un margen (escribe SMU, consola elevada)
scripts\fase0.ps1                    control + escalera (escribe SMU, consola elevada)
scripts\abort.ps1                    corta todo y restaura -5
scripts\read-p95-window.ps1          lista las ventanas de Prime95
```

Consola elevada: `Start-Process pwsh -Verb RunAs -ArgumentList '-NoExit','-File',<guion>`.

## Hecho el 27/08

- Auditoría completa; plan replanificado (revisión 2).
- Causa raíz de las medidas malas: `TortureCores` por defecto = 16
  (`Prime95Doc.cpp:1164`). Receta corregida con `NumCores=1`
  (`commonc.c:487`), tomada de CoreCycler.
- **Fase 0a superada**: 1 trabajador, 3/3 con señal, primera línea 20 s,
  3,11 líneas/min. Línea base del detector para el núcleo 11 en −5.
- `ErrorCheck=1` verificado en fuente: no afecta a la tortura. No entra.
- Docs: `FUENTES.md` (anclas), `ENGINES.md` (reescrito), `RESULTADOS.md`.

## Siguiente paso: FASE 0b — validar el detector

**Escribe en el SMU. Pedir confirmación antes de cada nivel.** Consola elevada.

1. `scripts\diag-margin.ps1 -Margin -8 -Veces 3` (3 × 180 s, un trabajador).
   - Señal 3/3 con primera línea ≤ 60 s → el colapso de anoche fue de los 16
     trabajadores. Seguir con la escalera: −11, −14, −17, −20, −23, −25.
   - Mudo o primera línea > 60 s, reproducible 2/3 → positivo. Ir al punto 3.
2. Si −8 sigue mudo: `-Margin -6` y `-Margin -7` para discriminar «escalón
   real» de «cualquier desviación del valor de la BIOS rompe el motor».
3. **Puerta**: un positivo reproducible (2/3) por error declarado, WHEA o
   colapso (> 3× la base). Sin positivo a −25 → parar, el motor no detecta.

`diag-margin.ps1` aún no aplica el umbral 3× automáticamente ni vigila WHEA:
leer «primera» del resumen y comparar con 20 s a mano; comprobar WHEA con
`Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='Microsoft-Windows-WHEA-Logger'}`.

## Después

```
0c   contraste con CoreCycler (modo manual, mismo nucleo, mismos niveles)   ~1 h
3    Prime95Engine en C# + panel en vivo; los guiones quedan como diagnostico
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
