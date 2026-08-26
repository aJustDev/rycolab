# Estado y siguiente paso

Última sesión: **26/08/2026, ~20:15**

## Cómo está la máquina ahora

```
CPU        -5 en los 16 nucleos  (el all-core de la BIOS, base elegida)
BIOS       Legion Optimization = Enabled · CPU Overclocking = Enabled
           All Core Curve Optimizer: signo −, magnitud 5
           Precision Boost Overdrive Scalar = 1X
LLT        perfil en disco a -3/-7, NO aplicado (la aplicacion no esta abierta
           y no arranca sola)
Procesos   ninguna herramienta de estres viva
```

Un reinicio devuelve siempre a −5 en los dieciséis. Esa es la red de seguridad.

## Lo que funciona

```
colab probe    lee el margen PSM aplicado y lo contrasta con el perfil en disco
colab apply    escribe, camina el paso, verifica cada parada
colab reset    vuelve a la base
colab sensors  vuelca los sensores con su nombre exacto
```

Binario en `src/LegionCoLab.Cli/bin/Release/net9.0-windows/win-x64/colab.exe`.
Requiere consola elevada.

## Siguiente paso: FASE 0

Es una **puerta**, no un hito. Bajar un solo núcleo escalón a escalón hasta que
Prime95 cante un error de cálculo.

- **Si aparece un error** → hay detector, y la campaña se apoya en terreno firme.
- **Si se llega a −25 sin un solo error** → el motor está mal configurado. Se
  para y se arregla ANTES de escribir el corredor, la base de datos y lo demás.

Duración estimada: 20–30 min de máquina ocupada.

Puede colgar el equipo: es el resultado esperado si no salta antes un error de
cálculo. No se corrompe nada — la escritura es al SMU, no a disco.

### Receta del motor, ya verificada

`prime.txt` en el directorio propio de cada núcleo:

```
StressTester=1
V24OptionsConverted=1     <- sin esto sale el dialogo y el proceso SE QUEDA ESPERANDO
UsePrimenet=0
EnableSetAffinity=0       <- que Prime95 no se fije su propia afinidad
MinTortureFFT=4
MaxTortureFFT=32
TortureMem=0
TortureTime=1
TortureHyperthreading=0
```

Lanzar con `prime95.exe -t -W<directorio del nucleo>` y poner al proceso una
máscara de afinidad de **los dos lógicos** del núcleo físico: `3 << 2N`.

El porqué de cada pieza está en `ENGINES.md`, leído del fuente, no adivinado.

## Después de la Fase 0

```
5  Almacen SQLite e ingesta desde JSONL, colab report
6  Corredor de campanas: plan.json, latido, deteccion de cuelgue, --auto-resume
7  FASE 1  barrido por CCD          ~4-8 h de maquina
8  YCruncherEngine (chip completo)
9  FASE 3  validacion larga         8-24 h
```

Los pasos 7 y 9 son horas de máquina desatendida. Encajan bien en jornada
laboral si se trabaja desde otro equipo.

## Cabos sueltos que no son de este repositorio

- Validación larga del ganador de GPU (+300 / tope 875 mV): OCCT 60 min, OCCT
  Switch 30 min, 3× Steel Nomad y ≥2 h de juego real. **Jugar cuenta como
  validación**, pero la curva no se aplica sola: hay que relanzar Afterburner
  con `-Profile1`.
- Falta la línea base de OCCT Steady de serie para la GPU.
- Verificar qué CCD apaga realmente el «modo X3D» de LLT antes de usarlo.
- El PR a Legion Toolkit: exponer la lectura de hardware que
  `LoadFromHardwareAsync` ya calcula y descarta.
