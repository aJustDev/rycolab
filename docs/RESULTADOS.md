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

Núcleo 0 (CCD0, V-Cache), misma prueba:

| Hora | Núcleo | Binario | Iteraciones | Resultado | GHz p50 | V p50 / max | W núcleo | T |
|---|---|---|---|---|---|---|---|---|
| 12:56 | 0 | `04-P4P` | 2 | todos `Passed`, vivo | 5,150 | 1,065 / 1,073 | 7,3 | 60,0 |

WHEA 0; −5 verificado después. El CCD con V-Cache tiene fMax más bajo
(5,15 frente a 5,45) y va a 1,065 V / 7,3 W: más margen aún.

### 27/08/2026 — tope subido a −40 (decisión del usuario, commit `5bd41ac`); núcleo 11, `04-P4P`, 360 s, suspensión

| Hora | Margen | Iteraciones | Resultado | GHz p50 | V p50 / max | W núcleo | T |
|---|---|---|---|---|---|---|---|
| 13:05 | −35 | 2 | todos `Passed`, vivo | 5,450 | 1,133 / 1,148 | 8,7 | 64,1 |
| 13:11 | −40 | 2 | todos `Passed`, vivo | 5,450 | 1,119 / 1,133 | 9,7 | 68,5 |

WHEA 0; −5 verificado después de cada uno. Tensión a fMax por margen:
−30 → 1,153 V, −35 → 1,133 V, −40 → 1,119 V (≈ −3,4 mV por cuenta, lineal:
el margen sigue aplicándose). **Sin positivo hasta −40.**

### 27/08/2026 — tope a −50 (commit `205697a`); núcleo 11, `04-P4P`, 360 s, suspensión

| Hora | Margen | Resultado | GHz p50 | V p50 / max | W núcleo | T |
|---|---|---|---|---|---|---|
| 13:46 | −45 | todos `Passed`, vivo | 5,450 | 1,095 / 1,113 | 8,2 | 61,6 |
| 13:53 | −50 | todos `Passed`, vivo | 5,450 | 1,076 / 1,087 | 7,7 | 60,7 |

WHEA 0; −5 verificado después. Velocidad de SFTv4 constante en todo el
rango (8,3-8,5 × 10⁸ bits/s): sin clock stretching. **Sin positivo en todo
el rango del SMU (−5 … −50).**

### 27/08/2026 — Fase 0c: contraste con CoreCycler (v0.11.0.4, modo manual)

`colab apply --core 11 --margin -45` → CoreCycler con `config.ini` del clon
(`coreTestOrder = 11`, y-cruncher `04-P4P`, SFTv4/FFTv4/N63, 6 min,
`suspendPeriodically = 1`, 1 hilo) → `colab reset`. Sonda cada 30 s durante
la prueba: −45 las 15 veces. Requirió instalar .NET Runtime 8 (winget
`Microsoft.DotNet.Runtime.8`, 8.0.30).

Resultado de CoreCycler (`corecycler/logs/CoreCycler_2026-08-27_14-01-44_YCRUNCHER_04-P4P.log`):
`Test completed in 00h 06m 01s` · `No core has thrown an error` ·
`No WHEA errors were observed during the test`.

**CoreCycler coincide con nuestro arnés**: el núcleo 11 pasa −45 en 6 min.

### 27/08/2026 — duración: núcleo 11, −50, `04-P4P`, **30 min**, suspensión

| Hora | Duración | Iteraciones | Tests `Passed` | Errores | Susp. | GHz p50 | V p50 / max | W núcleo | T / max |
|---|---|---|---|---|---|---|---|---|---|
| 14:12-14:42 | 1800 s | 8 | 24 | 0 | 180 | 5,450 | 1,079 / 1,094 | 7,9 | 63,0 / 82,6 |

WHEA 0; −5 verificado después. **Tampoco por tiempo: 30 min al mínimo del
SMU en fMax, limpio.**

### Resumen del núcleo 11 (27/08/2026)

| Régimen | GHz | V | W | Margen máx. probado | Positivo |
|---|---|---|---|---|---|
| Prime95 small FFT (AVX-512), 180 s | 5,00-5,21 | 1,07-1,08 | 14 | −25 | no |
| Prime95 SSE Huge + suspensión, 360 s | 5,02-5,21 | 1,07-1,09 | 13,6 | −30 | no |
| y-cruncher 24-ZN5 + suspensión, 360 s | 5,29 | 1,11 | 10,5 | −30 | no |
| y-cruncher 04-P4P + suspensión, 360 s | 5,45 | 1,15 → 1,08 | 9,1 → 7,7 | **−50** (mínimo del SMU) | no |
| CoreCycler 0.11.0.4 manual, 04-P4P, 6 min | — | — | — | −45 | no |

Tensión a fMax (04-P4P) por margen: −30 1,153 · −35 1,133 · −40 1,119 ·
−45 1,095 · −50 1,076 V. Lineal, −3,8 mV por cuenta de media.

## Fase 1 — barrido por núcleos (27/08/2026, `fase1.ps1`, desde −50 de 5 en 5)

Por núcleo y margen: `04-P4P` (SSE3, fMax) y luego `24-ZN5` (AVX-512),
360 s cada uno, suspensión 1 s/10 s, 1 hilo, 1 GiB. Límite = primer margen
limpio en ambos. Cada prueba restaura −5. JSON por núcleo en `runs/fase1/`.

### Positivos (los primeros del proyecto)

| Hora | Núcleo | Margen | Motor | Señal | Cuándo | V / GHz en el nivel |
|---|---|---|---|---|---|---|
| 14:50:51 | 0 | −50 | `04-P4P` | **crash** `0xc0000005` (APPCRASH, mini-dump) | 294 s, justo tras una reanudación | 1,003 / 5,150 |
| 15:01:30 | 0 | −50 | `24-ZN5` | `SFTv4 Failed`, `Bottom word mismatch` | 29 s | — |
| 15:09:40 | 0 | −45 | `24-ZN5` | ídem | 79 s | — |
| 15:29:44 | 1 | −50 | `24-ZN5` | ídem | 39 s | — |
| 15:38:05 | 1 | −45 | `24-ZN5` | ídem | 89 s | — |
| 15:57:38 | 2 | −50 | `24-ZN5` | ídem | 9 s | — |
| 16:06:08 | 2 | −45 | `24-ZN5` | ídem | 99 s | — |
| 16:26:06 | 3 | −50 | `24-ZN5` | ídem | 29 s | — |
| 16:46:15 | 4 | −50 | `24-ZN5` | **reinicio en frío** (Kernel-Power 41, 16:46:32) | ~45 s | — |
| 20:33:37 | 8 | −50 | `24-ZN5` | **WHEA 47** (corregido, componente memoria, dir. física `0x100b3d5207`); la prueba pasó | 35 s | 1,063 / 5,385 |
| 20:45:42 | 9 | −50 | `24-ZN5` | `SFTv4 Failed`, `Checksum Mismatch` | 9 s | — |
| 20:53:32 | 9 | −45 | `24-ZN5` | ídem | 59 s | — |
| 21:25:35 | 11 | −50 | `24-ZN5` | `Bottom word mismatch` | 9 s | — |
| 21:48:37 | 12 | −50 | `24-ZN5` | `Checksum Mismatch` | 19 s | — |

El crash del núcleo 0 a −50 con `04-P4P` no se reprodujo en la repetición
(1/2). Evidencia en `runs/fase1/positivos/core0-m50-04P4P/` (salida, log,
telemetría, `.dmp`). WHEA: 0 en todo el día, incluido el reinicio.

### Límites (CCD0, V-Cache)

| Núcleo | Límite | −50 `04-P4P` / `24-ZN5` | −45 `04-P4P` / `24-ZN5` | −40 `04-P4P` / `24-ZN5` | V a fMax en el límite |
|---|---|---|---|---|---|
| 0 | **−40** | limpio¹ / falla 29 s | limpio / falla 79 s | limpio / limpio | 1,033 |
| 1 | **−40** | limpio / falla 39 s | limpio / falla 89 s | limpio / limpio | 1,035 |
| 2 | **−40** | limpio / falla 9 s | limpio / falla 99 s | limpio / limpio | 1,034 |
| 3 | **−45** | limpio / falla 29 s | limpio / limpio | — | 1,027 |
| 4 | **−45** | limpio / **reinicio** | limpio / limpio | — | 1,024 |
| 5 | **−45**² | no probado | limpio / limpio | — | 1,033 |
| 6 | **−45**² | no probado | limpio / limpio | — | 1,041 |
| 7 | **−45**² | no probado | limpio / limpio | — | 1,042 |

### Límites (CCD1, sin V-Cache; 20:26-22:39, desde −50)

| Núcleo | Límite | −50 `04-P4P` / `24-ZN5` | −45 `04-P4P` / `24-ZN5` | −40 `04-P4P` / `24-ZN5` | V a fMax en el límite |
|---|---|---|---|---|---|
| 8 | **−50**³ | limpio / limpio (WHEA 47 a los 35 s) | — | — | 1,078 |
| 9 | **−40** | limpio / falla 9 s | limpio / falla 59 s | limpio / limpio | 1,096 |
| 10 | **−50** | limpio / limpio | — | — | 1,090 |
| 11 | **−45** | limpio / falla 9 s | limpio / limpio | — | 1,108 |
| 12 | **−45** | limpio / falla 19 s | limpio / limpio | — | 1,079 |
| 13 | **−50** | limpio / limpio | — | — | 1,092 |
| 14 | **−50** | limpio / limpio | — | — | 1,109 |
| 15 | **−50** | limpio / limpio | — | — | 1,107 |

¹ crash 1/2 en la repetición.
³ Único WHEA del proyecto (id 47, corregido, memoria) durante esa prueba.
El −50 del núcleo 8 queda marcado: no es un límite limpio.
² Segunda tanda (19:29-20:19, `-Nucleos 4,5,6,7 -Inicio -45`): −50 no se
probó en 5-7 (4/4 anteriores habían fallado y el 4 reinició la máquina).
Límite según definición, pero sin positivo propio. Todos limpios a la
primera con ambos motores; WHEA 0; −5 × 16 al terminar.

Patrón: `04-P4P` (SSE3) pasa −50 en todos; el que discrimina en CCD0 es
`24-ZN5` (AVX-512), y el tiempo hasta el error crece al subir el margen
(−50: 9-39 s; −45: 79-99 s). En CCD1 el mismo motor discrimina (9, 11, 12
fallan a −50 en 9-19 s); 8, 10, 13, 14, 15 pasan −50 con ambos. El 11 solo
había pasado −50 con `04-P4P` (12:43-14:10); con `24-ZN5` falla a −50.

Tabla completa (16 núcleos), límite en 6 min con ambos motores:

```
CCD0   0:-40  1:-40  2:-40  3:-45  4:-45  5:-45  6:-45  7:-45
CCD1   8:-50* 9:-40 10:-50 11:-45 12:-45 13:-50 14:-50 15:-50     * WHEA 47
```

CCD1 llega a fMax 5,45 GHz con `04-P4P`; CCD0 se queda en 5,15 GHz.

## WHEA

`Microsoft-Windows-WHEA-Logger`: **0 eventos** en todo el histórico del
registro del sistema hasta el 27/08/2026 20:33. Primer evento: **27/08/2026
20:33:37, id 47, Advertencia**, "Error de hardware corregido. Componente:
memoria. Origen: Corrected Machine Check", `PhysicalAddress = 0x100b3d5207`,
`ErrorSource = 1`, `ValidBits = 0x2`, Node/Bank/Row/Column = 0. Durante el
núcleo 8 a −50 con `24-ZN5` (20:33:03-20:39:14), prueba que pasó. Sin más
eventos hasta las 22:45.

## Histórico anterior a este repositorio (de `UNDERVOLT.md`)

| Config | Resultado |
|---|---|
| LLT −3 CCD0 / −7 CCD1, sesiones de juego y Cinebench | estable |
| LLT −15 all-core | Cinebench murió a los 10,5 min |
| BIOS all-core −5 (base actual) | estable, arranque limpio |

## Fase 1b — soak en reposo con el perfil candidato (27/08/2026, `fase1b.ps1`)

Perfil = límite + 5 (el 8 tratado como −45 por el WHEA 47):

```
CCD0   0:-35  1:-35  2:-35  3:-40  4:-40  5:-40  6:-40  7:-40
CCD1   8:-40  9:-35 10:-45 11:-40 12:-40 13:-45 14:-45 15:-45
```

| Hora | Qué | Resultado |
|---|---|---|
| 22:43:38 | `colab apply` a los 16, `probe` | hardware = perfil, verificado |
| 22:43:48-23:14:40 | 31 min en reposo (escritorio + vídeo), muestra cada 60 s | margen intacto en 31/31; WHEA 0; CPU 0-7 % |
| 23:14:41 | `reset --to -5`, `probe` | −5 × 16 |

`runs/fase1b/resultado.json`, código 0.

## Fase 3 — uso real con el candidato, primera sesión (27-28/08/2026, `fase1b.ps1 -Minutos 180`)

| Hora | Qué | Resultado |
|---|---|---|
| 23:41:26 | perfil aplicado y verificado en los 16 | ok |
| 23:41-01:10 | uso real (escritorio, vídeo), muestra cada 60 s | margen intacto 89/89; WHEA 0; CPU 1-10 % |
| 01:10:13 | suspensión (Kernel-Power 42) | — |
| 08:32:28 | reanudación (Power-Troubleshooter 1) | **hardware en −5 × 16**: la suspensión devuelve los márgenes a la base de la BIOS |
| 08:33:11 | vigilante: "margen CAMBIADO", `reset`, `probe` | −5 × 16; código 1 |

Sin WHEA (0 el 28/08), sin Kernel-Power 41, sin reinicio. Registro en
`runs/fase1b/fase3-noche1.log`. **89 min de uso real limpios**; el despertar
con el perfil puesto no se probó (al despertar ya estaba en −5).

## Verificacion del barrido en C# (28/08/2026, `colab sweep`, campana `runs/verif-b`)

Nucleo 13 con los 3 tests de la Fase 1 (SFTv4, FFTv4, N63), 360 s, suspension 1 s/10 s, desde -50:

| Hora | Margen | Motor | Veredicto | GHz | V | W | Suspensiones |
|---|---|---|---|---|---|---|---|
| 09:14 | -50 | `04-P4P` | limpio, 362 s | 5,450 | 1,085 | 7,4 | 32 |
| 09:21 | -50 | `24-ZN5` | limpio, 362 s | 5,441 | 1,077 | - | 32 |

Limite **-50**, igual que con `fase1.ps1` el 27/08 (20:26-22:39). Base restaurada al acabar; perfil candidato reaplicado despues.

## Reanudacion con guard vivo (28/08/2026)

| Hora | Evento | Resultado |
|---|---|---|
| 10:11:04 | 1a prueba: `suspend` (guard `ec39f56^`) | al despertar (10:14:38) la muestra salio antes que el `resume` de Windows (10:14:41); reaplicacion inmediata; **SMU rechazo la escritura del nucleo 12**; guard salio con codigo 1, base -5 |
| 10:42:53 | 2a prueba: `suspend` (guard `ec39f56`) | 10:45:12 `resume` deducida del suspend; 10:45:14 `resume` de Windows; 10:45:22 **perfil reaplicado y verificado** en los 16 |

La suspension devuelve -5 siempre; guard lo repone en ~10 s tras despertar.
