# Resultados

Todas las pasadas, en orden. `runs/` está ignorado por git; lo que importa se
copia aquí. Solo datos.

Convenciones: núcleo 0-based; CCD0 = núcleos 0-7 (V-Cache), CCD1 = 8-15.
«Primera» = segundos hasta la primera línea de `results.txt`. «Trab.» =
ventanas de trabajador de Prime95. Duración 180 s salvo indicación.

## Línea base del detector (por núcleo, margen base, 1 trabajador)

| Núcleo | Margen | Primera (mediana) | Líneas/min | Fecha | Pasadas |
|---|---|---|---|---|---|
| 11 | −5 | **20 s** | **3,11** | 27/08/2026 01:13-01:23 | 3 |

Umbral de colapso: primera línea > 60 s (3×) o cero líneas en el paso.

## Pasadas

### 27/08/2026 — Fase 0 (receta sin `NumCores`: 16 trabajadores, **no válida como medida de silicio**)

| Hora | Núcleo | Margen | Trab. | Líneas | Primera | W núcleo | Tctl | Veredicto |
|---|---|---|---|---|---|---|---|---|
| 23:49 | 11 | −8 | 4 (`NumWorkers` por defecto) | 0 | nunca | — | — | sin evidencia; abortado por mí |
| 23:52 | 11 | −11 | 4 | 2 | ~40 s | — | — | abortado (guion corregido) |
| 00:07 | 11 | −5 control | 16 | 4 | ~40 s | 18,40 | 88,2 | señal |
| 00:10 | 11 | −8 | 16 | 0 | nunca | 16,02 | 81,1 | mudo; `results.txt` no creado; 4904 MHz ef. |
| 00:22 | 11 | −5 | 16 | 3 | 40 s | — | — | señal |
| 00:25 | 11 | −5 | 16 | 3 | 40 s | — | — | señal |
| 00:28 | 11 | −5 | 16 | 4 | 40 s | — | — | señal |
| 00:36 | 11 | −8 | 16 | −1 (no creado) | nunca | — | — | mudo, proceso vivo, 2,0 lógicos |
| 00:40 | 11 | −8 | 16 | −1 | nunca | — | — | mudo |
| 00:43 | 11 | −8 | 16 | −1 | nunca | — | — | mudo |
| 00:47 | 11 | −5 | 16 | 2 (90 s) | 40 s | — | — | señal; confirma 16 trab. también a −5 |

Observación abierta: bajo 16 trabajadores, −5 da señal 5/5 y −8 es mudo 4/4
con el núcleo a 100 %, ~16 W y 4,9 GHz efectivos. Sin `FATAL ERROR`, sin WHEA,
sin proceso muerto. **No se atribuye al silicio** hasta repetir con 1
trabajador (Fase 0b).

### 27/08/2026 — Fase 0a (receta corregida, `NumCores=1`)

| Hora | Núcleo | Margen | Trab. | Líneas | Primera | CPU lóg. | Veredicto |
|---|---|---|---|---|---|---|---|
| 01:12 | 11 | −5 | 0 detectados | — | — | — | falso abortado: la ventana se llama `Worker - Torture Test` sin `#`; guardia corregida |
| 01:13 | 11 | −5 | 1 | 9 | 20 s | 1,01 | señal |
| 01:16 | 11 | −5 | 1 | 9 | 20 s | 1,01 | señal |
| 01:20 | 11 | −5 | 1 | 10 | 20 s | 1,01 | señal |

Secuencia de autotests completada en 180 s (determinista):
4608, 5K, 6K, 7K, 7680, 8K, 9K, 10K, 10752, 12K.

## WHEA

`Microsoft-Windows-WHEA-Logger`: **0 eventos** en todo el histórico del
registro del sistema (comprobado 27/08/2026 00:41).

## Histórico anterior a este repositorio (de `UNDERVOLT.md`)

| Config | Resultado |
|---|---|
| LLT −3 CCD0 / −7 CCD1, sesiones de juego y Cinebench | estable |
| LLT −15 all-core | Cinebench murió a los 10,5 min |
| BIOS all-core −5 (base actual) | estable, arranque limpio |
