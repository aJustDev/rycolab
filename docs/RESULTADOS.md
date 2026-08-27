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

### 27/08/2026 — Fase 0b' (receta ligera: SSE, FFT Huge 8960K-32768K, suspensión 1 s/10 s, 360 s por pasada)

Línea base del régimen ligero, núcleo 11, −5:

| Hora | Pasada | Líneas | Primera | Susp. | GHz p50 / p99 / max | V p50 / max | W núcleo p50 | T |
|---|---|---|---|---|---|---|---|---|
| 11:25 | 1 | 56 | 10 s | 36 | 5,016 / 5,238 / 5,242 | 1,088 / 1,180 | 13,64 | 73,3 |
| 11:32 | 2 | 56 | 10 s | 36 | 5,016 / — / — | 1,088 / 1,181 | 13,69 | 72,9 |

Umbral de colapso ligero: primera línea > 30 s o < 19 líneas en 360 s.

**El régimen ligero no sube el reloj en esta máquina**: mismo ~14 W y
~5,02 GHz que small FFT; solo los transitorios tras cada reanudación llegan
a 5,24 GHz / 1,18 V. fMax (5,45) no se alcanza en ninguna tortura sostenida.
Manda un tope de potencia por núcleo, no el tipo de instrucción.

Escalera ligera, núcleo 11, 3 × 360 s por margen (medianas por pasada):

| Hora | Margen | Líneas | Primera | Error | GHz | V | V max | W núcleo | Veredicto |
|---|---|---|---|---|---|---|---|---|---|
| 11:38-11:57 | −25 | 57, 58, 58 | 10 s | ninguno | 5,174 / 5,178 / 5,181 | 1,070 / 1,072 / 1,073 | 1,139 | 13,6 | limpia 3/3 |
| 11:57-12:15 | −28 | 57, 58, 58 | 10 s | ninguno | 5,197 / 5,200 / 5,199 | 1,069 / 1,070 / 1,070 | 1,137 | 13,6 | limpia 3/3 |
| 12:15-12:34 | −30 | 58, 58, 58 | 10 s | ninguno | 5,203 / 5,210 / 5,212 | 1,066 / 1,068 / 1,068 | 1,123 | 13,6 | limpia 3/3 |

WHEA 0 tras cada margen; −5 verificado tras cada uno. **Sin positivo hasta el
tope de seguridad (−30) tampoco en régimen ligero.** De −28 a −30 la curva
apenas responde (+10 MHz, −2 mV): tope de potencia.

### 27/08/2026 — y-cruncher clavado al núcleo 11, −30, 360 s, suspensión 1 s/10 s, 1 hilo, 1 GiB

Binarios del clon de CoreCycler; `stressTest.cfg` generado (`diag-ycruncher.ps1`).

| Hora | Binario | Tests | Iteraciones | Resultado | GHz p50 | V p50 / max | W núcleo | T |
|---|---|---|---|---|---|---|---|---|
| 12:34 | `04-P4P` (SSE3) | SFTv4, FFTv4, N63 | 2 | todos `Passed`, vivo | **5,450** | 1,153 / 1,165 | 9,1 | 64,3 |
| 12:43 | `24-ZN5 ~ Komari` (AVX-512) | SFTv4, FFTv4, N63 | 2 | todos `Passed`, vivo | 5,289 | 1,113 / 1,178 | 10,5 | 70,8 |

WHEA 0. −5 verificado después de cada uno.

**`04-P4P` es el único motor que lleva el núcleo a fMax (5,45 GHz, 1,15 V,
9 W)**: es el régimen ligero real, el extremo alto de la curva V/F. El núcleo
11 lo aguanta a −30, tope de seguridad del arnés.

### Resumen del núcleo 11 (27/08/2026)

| Régimen | GHz | V | W | Margen máx. probado | Positivo |
|---|---|---|---|---|---|
| Prime95 small FFT (AVX-512), 180 s | 5,00-5,21 | 1,07-1,08 | 14 | −25 | no |
| Prime95 SSE Huge + suspensión, 360 s | 5,02-5,21 | 1,07-1,09 | 13,6 | −30 | no |
| y-cruncher 24-ZN5 + suspensión, 360 s | 5,29 | 1,11 | 10,5 | −30 | no |
| y-cruncher 04-P4P + suspensión, 360 s | 5,45 | 1,15 | 9,1 | −30 | no |

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
