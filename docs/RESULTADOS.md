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

### 27/08/2026 — Fase 0b (1 trabajador, 3 × 180 s por margen, núcleo 11)

| Hora | Margen | Trab. | Líneas | Primera | Error | Veredicto |
|---|---|---|---|---|---|---|
| 09:41-09:51 | −8 | 1 | 9, 10, 10 | 20 s ×3 | ninguno | señal 3/3 |
| 09:51-10:01 | −11 | 1 | 10, 10, 10 | 20 s ×3 | ninguno | señal 3/3 |
| 10:01-10:11 | −14 | 1 | 10, 10, 10 | 20 s ×3 | ninguno | señal 3/3 |
| 10:11-10:21 | −17 | 1 | 10, 10, 10 | 20 s ×3 | ninguno | señal 3/3 |
| 10:21-10:31 | −20 | 1 | 10, 10, 10 | 20 s ×3 | ninguno | señal 3/3 |
| 10:31-10:41 | −23 | 1 | 10, 10, 10 | 20 s ×3 | ninguno | señal 3/3 |
| 10:42-10:52 | −5 | 1 | 10, 10, 10 | 20 s ×3 | ninguno | señal 3/3 (referencia con telemetría) |
| 10:52-11:02 | −25 | 1 | 10, 10, 10 | 20 s ×3 | ninguno | señal 3/3 |

Hardware verificado por sonda antes de cada margen; restaurado a −5 y
verificado después de cada uno. WHEA: 0 en todo momento.

**Sin positivo hasta −25** bajo Prime95 small FFT, un trabajador. El mudo de
−8 con 16 trabajadores (arriba) no se reproduce con uno: era de la carga, no
del silicio.

#### Contraste físico −5 / −25 (mismo núcleo, misma carga, 3 pasadas, 176 muestras/pasada)

Medianas de `colab watch` (LHM + tabla PM del SMU v0x621202):

| Margen | Reloj LHM | Efectivo LHM | V núcleo (PM) | GHz (PM) | W núcleo | T núcleo | Tctl |
|---|---|---|---|---|---|---|---|
| −5 | 5005 / 5010 / 5010 | 2551 / 2550 / 2547 | 1,0832 | 5,005 | 13,96 / 13,98 / 13,99 | 72,8 | 73,6 / 72,9 / 73,1 |
| −25 | 5165 / 5165 / 5170 | 2628 / 2632 / 2629 | 1,0675 | 5,167 | 13,96 / 13,92 / 13,96 | 72,7 | 72,9 / 73,0 / 73,4 |
| Δ | **+160 MHz** | +3,1 % | **−15,7 mV** | +3,2 % | 0 | 0 | 0 |

Comprobación: (5,167/5,005) × (1,0675/1,0832)² = 1,003 → potencia constante.
El margen está actuando: el núcleo está limitado a ~14 W y el CO se convierte
en reloj. Telemetría cruda en `runs/fase0/watch-m{-5,-25}-p{1,2,3}.jsonl`
(ignorado por git).

Efectivo ≈ mitad del reloj porque LHM promedia los dos lógicos y Prime95 usa
uno. En reposo el núcleo 11 lee 0,64 V / 2,0 GHz / 0,1 W / 39 C.

## WHEA

`Microsoft-Windows-WHEA-Logger`: **0 eventos** en todo el histórico del
registro del sistema (comprobado 27/08/2026 00:41 y tras cada margen de la
Fase 0b, última 11:02).

## Histórico anterior a este repositorio (de `UNDERVOLT.md`)

| Config | Resultado |
|---|---|
| LLT −3 CCD0 / −7 CCD1, sesiones de juego y Cinebench | estable |
| LLT −15 all-core | Cinebench murió a los 10,5 min |
| BIOS all-core −5 (base actual) | estable, arranque limpio |
