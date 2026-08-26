# Motores de estrés

Notas de campo medidas en esta máquina, no leídas en un foro.

## y-cruncher — `stress` NO se puede clavar a un núcleo

Versión probada: **v0.8.7.9547b**, 26/08/2026.

```
cmd /c start /affinity 400000 /b /wait y-cruncher.exe stress -D:40 -M:64M SVT
```

`start /affinity` fija la máscara en la creación del proceso, sin carrera. Aun
así y-cruncher **reservó memoria para los 32 procesadores lógicos**:

```
Allocating Memory...
  Core   0:  32.0 MiB  node 0 (100%)
  Core   1:  32.0 MiB  node 0 (100%)
  ...
  Core  31:  32.0 MiB  node 0 (100%)
```

Enumera las CPU del sistema por su cuenta —vía API de grupos de procesador— y
se fija su propia afinidad, ignorando la máscara heredada.

El escape obvio no existe:

```
y-cruncher stress -PF:none ...
  -> Invalid Parameter: -PF
```

`-PF` y `-TD` solo los aceptan `bench`, `benchio`, `bbp` y `custom`. **`stress`
no lleva ninguna opción de hilos.**

Queda la vía de `y-cruncher config fichero.cfg`, pero ese formato está sin
documentar y hay que generarlo desde la interfaz interactiva. No compensa.

### PELIGRO — se queda esperando una tecla

Ante un parámetro inválido:

```
Invalid Parameter: -PF
Presione una tecla para continuar . . .
```

En un arnés desatendido eso es un cuelgue silencioso que parece una prueba en
curso. **Todo motor se lanza con la entrada estándar redirigida a NUL y con un
plazo máximo por encima de la duración pedida.** Sin excepción.

## Prime95 — sí se deja clavar, pero no satura

Versión probada: **30.19 build 20**, 26/08/2026. Seis tentativas hasta dar con
la combinación que clava la carga a un núcleo:

| Qué se probó | Resultado |
|---|---|
| `Affinity=22` en `[Worker #1]` de `prime.txt` | **Ignorado.** Esa sección es para el trabajo normal de GIMPS, no para la tortura. Se cargaron los 16 núcleos, 172 W, 100 °C |
| `NumCores=1` | Limita a un núcleo, pero **redefine la topología**: Prime95 pasa a creer que la máquina tiene un solo núcleo, la numeración lógica queda en 0-1 y `Affinity=22` cae fuera de rango. Carga el núcleo 0 |
| `TortureCores=1` sin `NumCores` | Limita a un núcleo, sigue cayendo en el 0 |
| `TortureCores=1` + `EnableSetAffinity=0` + máscara del proceso a **un** lógico | Cae en el núcleo correcto, pero `results.txt` queda vacío: apenas trabaja |
| ...con máscara a **los dos** lógicos del núcleo | **Funciona.** `Self-test 4608 passed!` en `results.txt` y el núcleo correcto cargado |

Configuración mínima que funciona, en `prime.txt` del directorio del núcleo:

```
StressTester=1
V24OptionsConverted=1     <- sin esto sale el dialogo de bienvenida y SE QUEDA ESPERANDO
UsePrimenet=0
MinTortureFFT=4
MaxTortureFFT=32
TortureMem=0
TortureTime=1
TortureHyperthreading=0
TortureCores=1
EnableSetAffinity=0       <- que no se fije el su propia afinidad
```

Se lanza con `prime95.exe -t -W<directorio del nucleo>` y se le pone al proceso
una máscara de afinidad de **los dos lógicos del núcleo físico** (`3 << 2N`).
Con un solo lógico los hilos auxiliares compiten con el de cálculo y no avanza.

`-W` da aislamiento limpio: cada núcleo con su carpeta, su `prime.txt` y su
`results.txt`.

### Confirmado en el código fuente

Leído de `commonb.c` (repositorio `shafferjohn/Prime95`), lo que zanja las
tentativas de arriba:

```c
case SET_PRIORITY_TORTURE:
    bind_type = 0;                          // afinidad a UN nucleo concreto
    core = get_ranked_core (info->torture_core_num);
    break;

case SET_PRIORITY_NORMAL_WORK:
    sprintf (section_name, "Worker #%d", info->worker_num+1);
    p = IniSectionGetStringRaw (INI_FILE, section_name, "Affinity");   // <- solo aqui
```

**La tortura nunca lee `Affinity=`.** Es exclusivo del trabajo normal de GIMPS.
Y en `tortureTest()`:

```c
sp_info.torture_core_num = thread_num;      // el trabajador N prueba el nucleo N
```

O sea que la asignación de núcleo es el índice del trabajador, sin forma de
redirigirla por configuración. Para llegar al núcleo 11 harían falta doce
trabajadores.

`TortureCores` y `TortureThreads` **no existen**: las claves reales son
`MinTortureFFT`, `MaxTortureFFT`, `TortureMem`, `TortureTime`,
`TortureHyperthreading`, `TortureWeak`, `TortureAlternateInPlace` y
`TortureMultiThreadedFFTs`.

Queda una sola vía limpia, y es la que ya funciona:

```c
if (! IniGetInt (INI_FILE, "EnableSetAffinity", 1)) return;   // sale sin tocar nada
```

Con `EnableSetAffinity=0`, Prime95 no fija afinidad alguna y manda la máscara
del proceso. Verificado: cae en el núcleo pedido.

### Sobre medir la carga — corrección

Se llegó a concluir que Prime95 «no saturaba» comparando 2,98 W y 1.442 MHz
efectivos contra los 14,10 W del bucle de PowerShell. **Esa conclusión no se
sostiene**, por dos motivos:

1. El reloj efectivo de LibreHardwareMonitor se deriva de dos muestras
   consecutivas. `colab probe` las toma con milisegundos de separación, así que
   en ventana corta el valor no es fiable — de ahí cifras absurdas en los
   dieciséis núcleos a la vez.
2. Comparar vatios entre cargas distintas no mide estrés. El bucle de PowerShell
   es escalar a ~5,4 GHz; Prime95 usa AVX-512, que baja frecuencia y consume
   distinto haciendo mucho más trabajo por ciclo.

El indicador que sí aguanta: **ritmo de líneas en `results.txt`**. Un trabajador
solo escribió una línea en 45 s; la pasada de 16 trabajadores escribió 19 en
55 s. Mismo ritmo por trabajador — estaba trabajando con normalidad.

La pregunta «¿es buen detector?» no se contesta con vatios. Se contesta con la
Fase 0: bajar un núcleo hasta que cante un error.

## Reparto de papeles

| | Motor | Por qué |
|---|---|---|
| **Por núcleo** | Prime95, FFT pequeña | Respeta la afinidad del proceso y tiene número de trabajadores explícito en `local.txt`. Es lo que usa CoreCycler, y ahora se ve por qué |
| **Chip completo** | y-cruncher `stress` | Que use los 16 núcleos es justo lo que se quiere en la validación final. Matemática distinta a Prime95, así que caza fallos distintos |

No es el reparto que se planteó al principio: y-cruncher parecía el fácil por
ser cómodo de invocar, pero el que se deja clavar a un núcleo es Prime95.
