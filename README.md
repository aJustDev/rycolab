# legion-co-lab

Banco de pruebas de Curve Optimizer para Ryzen móvil, nacido en un
**Legion Pro 7 16AFR10H** (Ryzen 9 9955HX3D, 16 núcleos, dos CCD, uno con
caché apilada).

## Por qué existe

Legion Toolkit deja **escribir** márgenes de Curve Optimizer por núcleo, pero no
deja **comprobar** lo que quedó aplicado. Y no porque no pueda: en
`AmdOverclocking.xaml.cs` la función `LoadFromHardwareAsync` lee el margen real
de cada núcleo desde el SMU… y tres líneas después la interfaz se sobrescribe
con el contenido del JSON en disco. La lectura se calcula y se tira.

Sin esa lectura, afinar un undervolt es adivinar. Este repositorio la recupera
y construye alrededor un banco de pruebas.

### Lo que la sonda destapó el primer día

```
Con Legion Toolkit abierto     CCD1 -3   CCD2 -7     (el perfil)
Tras reiniciar, sin abrirlo    -5 en los dieciseis   (la BIOS)
```

La máquina tenía **dos configuraciones distintas** según si una aplicación
estaba abierta, y ninguna herramienta lo decía. Los dos escritores además no se
suman: se reemplazan, y manda el último.

## Principios

1. **Detectar errores de cálculo, no cuelgues.** Un cuelgue es un síntoma
   terminal y tardío. El riesgo real de un Curve Optimizer agresivo no es que
   el equipo se caiga, es que calcule mal en silencio.
2. **Nunca medir sin verificar antes.** Toda configuración se relee del SMU
   antes de tomar un solo dato. Si no coincide, se aborta.
3. **Los topes de seguridad no tienen bandera para saltárselos.**

## Uso

Requiere consola **elevada**.

```
colab probe                  margen PSM aplicado en cada nucleo
colab probe --sensors        anade reloj efectivo y potencia por nucleo
colab probe --json out.json  guarda la lectura con marca de tiempo
colab sensors                vuelca los sensores con su nombre exacto
```

`probe` compara por defecto contra el perfil de Legion Toolkit y **devuelve 2 si
no coinciden**, para poder encadenarlo en scripts.

## Notas de campo

Cosas medidas en esta máquina, no supuestas:

- `Core #N VID` de LibreHardwareMonitor **no es un voltaje por núcleo** en el
  9955HX3D. Los 16 devuelven el mismo valor y se mueven en bloque: 0,269 V en
  los dieciséis con un solo núcleo al 100 %, que es imposible. Descartado.
- Lo que **sí** discrimina por núcleo es `Core #N (SMU)` (potencia) y el reloj
  efectivo. Con carga clavada al núcleo 8: 14,10 W y 2.696 MHz frente a
  0,05–0,49 W del resto.
- Los CCD se numeran **desde 0**, igual que Legion Toolkit (`CCD {coreIndex / 8}`) y
  que la máscara SMU. HWiNFO y LibreHardwareMonitor numeran desde 1: nuestro
  CCD0 es su sensor `CCD1 (Tdie)`. La traducción vive en `Topology.CcdTempSensor`
  y en ningún otro sitio.
- Las temperaturas por CCD responden correctamente y sirven para confirmar que
  la carga cayó donde se pretendía.
- Cada núcleo físico ocupa dos procesadores lógicos: el núcleo N es el lógico 2N.

## Construir

Necesita el **SDK de .NET 9** (x64).

```
dotnet build -c Release src/LegionCoLab.Cli
```

`inpoutx64.dll` — la capa de acceso a puertos que necesita ZenStates.Core — se
copia en la compilación desde la instalación local de Legion Toolkit. No se
redistribuye. Si está en otra ruta:

```
dotnet build -c Release -p:InpOutSource=RUTA\inpoutx64.dll src/LegionCoLab.Cli
```

## Licencia

GPL-3.0. La codificación de la máscara de núcleo y la secuencia de acceso al
SMU derivan de Lenovo Legion Toolkit, también GPL-3.0. Ver `NOTICE`.

## Aviso

Esto escribe en el buzón SMU de tu procesador. Undervoltear mal produce
resultados incorrectos antes que fallos visibles. Úsalo sabiendo eso.
