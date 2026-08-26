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

## Reparto de papeles

| | Motor | Por qué |
|---|---|---|
| **Por núcleo** | Prime95, FFT pequeña | Respeta la afinidad del proceso y tiene número de trabajadores explícito en `local.txt`. Es lo que usa CoreCycler, y ahora se ve por qué |
| **Chip completo** | y-cruncher `stress` | Que use los 16 núcleos es justo lo que se quiere en la validación final. Matemática distinta a Prime95, así que caza fallos distintos |

No es el reparto que se planteó al principio: y-cruncher parecía el fácil por
ser cómodo de invocar, pero el que se deja clavar a un núcleo es Prime95.
