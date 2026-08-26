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

### Pendiente: no satura el núcleo

```
Prime95 clavado al nucleo 11        2,98 W   1.442 MHz efectivos
un bucle trivial de PowerShell     14,10 W   2.696 MHz efectivos
```

Un bucle tonto carga cinco veces más. El núcleo va a ~30 % de ocupación, así
que como detector de inestabilidad todavía no sirve: un margen malo puede pasar
desapercibido simplemente porque no se le está exigiendo.

Antes de seguir derivando a ciegas la configuración de tortura —que está sin
documentar— lo sensato es leer cómo la genera **CoreCycler** (sp00n), que es el
proyecto de referencia para esto y lleva años con ello resuelto.

## Reparto de papeles

| | Motor | Por qué |
|---|---|---|
| **Por núcleo** | Prime95, FFT pequeña | Respeta la afinidad del proceso y tiene número de trabajadores explícito en `local.txt`. Es lo que usa CoreCycler, y ahora se ve por qué |
| **Chip completo** | y-cruncher `stress` | Que use los 16 núcleos es justo lo que se quiere en la validación final. Matemática distinta a Prime95, así que caza fallos distintos |

No es el reparto que se planteó al principio: y-cruncher parecía el fácil por
ser cómodo de invocar, pero el que se deja clavar a un núcleo es Prime95.
