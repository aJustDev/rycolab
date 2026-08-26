# ============================================================================
# FASE 0 - Validar el detector.
#
# Baja UN solo nucleo escalon a escalon hasta que Prime95 cante un error de
# calculo. Sin un positivo conocido, todos los negativos posteriores no valen
# nada.
#
#   error antes de -25  ->  hay detector. La campana se apoya en terreno firme.
#   -25 sin un error    ->  el motor esta mal configurado. Se para y se arregla.
#
# Puede colgar el equipo: es el desenlace esperado si no salta antes un error.
# No se corrompe nada, la escritura es al SMU. Un reinicio devuelve al -5 de
# la BIOS.
#
# REGLA: un nivel sin evidencia NO es un aprobado. Si results.txt no crece, el
# motor no esta midiendo, y tampoco cantaria un error. Se aborta la campana.
# ============================================================================
param(
    [int]$Core     = 11,
    [int]$Seconds  = 180,
    [int]$Base     = -5,
    [int]$MinLines = 2,
    [int[]]$Ladder = @(-8, -11, -14, -17, -20, -23, -25)
)

$ErrorActionPreference = 'Stop'
$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$exe  = "$repo\src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe"
$P95  = "$repo\tools\prime95"
$runs = "$repo\runs\fase0"
$log  = "$runs\fase0.log"
$jsnl = "$runs\fase0.jsonl"

New-Item -ItemType Directory -Force $runs | Out-Null

function Say([string]$m, [string]$color = 'Gray') {
    $line = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $line -ForegroundColor $color
    Add-Content -Path $log -Value $line -Encoding utf8
}
function Rec([hashtable]$o) {
    $o['ts'] = (Get-Date -Format 'o')
    Add-Content -Path $jsnl -Value ($o | ConvertTo-Json -Compress) -Encoding utf8
}

# ---- lectura del hardware --------------------------------------------------
function Read-Margin([int]$c) {
    $tmp = Join-Path $runs 'probe.json'
    & $exe probe --no-compare --json $tmp | Out-Null
    $j = Get-Content $tmp -Raw | ConvertFrom-Json
    ($j.psm | Where-Object { $_.core -eq $c }).margin
}

# Confirma con los sensores que el nucleo cargado es el que creemos.
function Show-Load([int]$c) {
    try {
        $tmp = Join-Path $runs 'sensors.json'
        & $exe probe --no-compare --sensors --json $tmp | Out-Null
        $j = Get-Content $tmp -Raw | ConvertFrom-Json
        $mine = $j.perCore | Where-Object { $_.Core -eq $c }
        $top  = $j.perCore | Sort-Object -Property Power -Descending | Select-Object -First 1
        Say ("    nucleo {0}: {1:N2} W   |   mas cargado: nucleo {2} con {3:N2} W   |   Tctl {4:N1} C" -f `
             $c, $mine.Power, $top.Core, $top.Power, $j.telemetry.Tctl)
        if ($top.Core -ne $c) {
            Say "    AVISO: el nucleo mas cargado no es el $c. La afinidad no manda." 'Yellow'
        }
        return @{ Watts = $mine.Power; TopCore = $top.Core; Tctl = $j.telemetry.Tctl }
    } catch {
        Say "    (sin telemetria: $($_.Exception.Message))" 'DarkGray'
        return $null
    }
}

# ---- Prime95 clavado a un nucleo -------------------------------------------
# Claves reales de la 30.x. Si NumWorkers y CoresPerTest no se dan, Prime95
# reescribe prime.txt con sus valores por defecto (4 y 4) y acaban cuatro
# trabajadores apretujados en el nucleo. Medido el 26/08/2026.
function Start-P95([int]$c) {
    $work = Join-Path $P95 "work\core$c"
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $work | Out-Null
    Copy-Item "$PSScriptRoot\prime95-recipe.txt" (Join-Path $work 'prime.txt')   # UNICA copia de la receta

    $p = Start-Process -FilePath (Join-Path $P95 'prime95.exe') `
                       -ArgumentList '-t', "-W$work" `
                       -WorkingDirectory $P95 -PassThru -WindowStyle Minimized
    Start-Sleep -Milliseconds 900
    $p.ProcessorAffinity = [IntPtr]([int64]3 -shl (2 * $c))
    $leida = '{0:X}' -f [int64]$p.ProcessorAffinity
    Say "    afinidad releida del proceso: 0x$leida  (esperada 0x$('{0:X}' -f ([int64]3 -shl (2*$c))))"
    @{ Proc = $p; Work = $work; Results = (Join-Path $work 'results.txt'); Mask = $leida }
}

function Stop-P95 {
    Get-Process prime95 -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
}

$BAD = 'FATAL ERROR|Hardware failure|Rounding was|ERROR:|error occurred'

function Count-Lines($path) {
    if (-not (Test-Path $path)) { return 0 }
    $t = Get-Content $path -Raw
    if (-not $t) { return 0 }
    @($t -split "`n" | Where-Object { $_.Trim() -and $_ -notmatch '^\[' }).Count
}

# ---- vigila una pasada -----------------------------------------------------
function Watch-Run($h, [int]$secs, [int]$c) {
    $t0 = Get-Date
    $last = 0
    $telem = $null
    while (((Get-Date) - $t0).TotalSeconds -lt $secs) {
        Start-Sleep -Seconds 5
        $el = [int]((Get-Date) - $t0).TotalSeconds

        $muerto = $h.Proc.HasExited
        $txt = if (Test-Path $h.Results) { Get-Content $h.Results -Raw } else { '' }
        $n   = Count-Lines $h.Results

        if ($txt -match $BAD) {
            $hit = @($txt -split "`n" | Where-Object { $_ -match $BAD }) -join ' | '
            Say "    ERROR DE CALCULO a los ${el}s -> $hit" 'Red'
            return @{ Verdict = 'Error'; Elapsed = $el; Detail = $hit; Lines = $n; Telem = $telem }
        }
        if ($muerto) {
            Say "    el worker MURIO a los ${el}s (sin linea de error)" 'Red'
            return @{ Verdict = 'WorkerDied'; Elapsed = $el; Detail = ''; Lines = $n; Telem = $telem }
        }

        $nuevas = if ($n -gt $last) { " (+$($n - $last))" } else { '' }
        $last = $n
        $pct = [int](100 * $el / $secs)
        Say ("    {0,3}s / {1}s  {2,3}%   results.txt: {3} lineas{4}" -f $el, $secs, $pct, $n, $nuevas)

        if ($el -ge 40 -and -not $telem) { $telem = Show-Load $c }
    }
    @{ Verdict = 'Ok'; Elapsed = $secs; Detail = ''; Lines = (Count-Lines $h.Results); Telem = $telem }
}

# ---- una parada de la escalera --------------------------------------------
function Invoke-Level([int]$lvl, [string]$etiqueta) {
    Say ""
    Say "--- $etiqueta $lvl ------------------------------------------------" 'Yellow'

    & $exe apply --core $Core --margin $lvl | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "apply $lvl devolvio $LASTEXITCODE" }
    $leido = Read-Margin $Core
    if ($leido -ne $lvl) { throw "VERIFICACION FALLIDA: pedido $lvl, el hardware dice $leido" }
    Say "  hardware verificado en $leido  (los otros 15 siguen en $Base)" 'Green'

    $h = Start-P95 $Core
    $v = Watch-Run $h $Seconds $Core
    Stop-P95

    Rec @{ core = $Core; level = $lvl; kind = $etiqueta; verdict = $v.Verdict
           elapsed = $v.Elapsed; detail = $v.Detail; lines = $v.Lines
           seconds = $Seconds; mask = $h.Mask; watts = $v.Telem.Watts
           topCore = $v.Telem.TopCore; tctl = $v.Telem.Tctl }
    $v
}

# ============================================================================
try {
    Say "=====================================================================" 'Cyan'
    Say " FASE 0 - validar el detector    nucleo $Core (logicos $(2*$Core)/$(2*$Core+1))" 'Cyan'
    Say " control en $Base, luego $($Ladder -join ' -> ')" 'Cyan'
    Say " $Seconds s por nivel, minimo $MinLines lineas para dar un nivel por valido" 'Cyan'
    Say "=====================================================================" 'Cyan'

    $espera = 0
    while (Get-Process -Name 'Lenovo Legion Toolkit' -ErrorAction SilentlyContinue) {
        if ($espera -eq 0) {
            Say "Legion Toolkit esta abierto. Cierralo del todo (icono de la" 'Yellow'
            Say "bandeja -> Salir; la X solo lo minimiza). Esperando..." 'Yellow'
        }
        Start-Sleep -Seconds 3
        $espera += 3
        if ($espera -ge 180) { Say "ABORTADO: LLT sigue abierto tras 3 min." 'Red'; exit 4 }
    }
    if ($espera -gt 0) { Say "LLT cerrado. Seguimos." 'Green' }

    Say "Llevando los 16 nucleos a la base $Base..."
    & $exe reset --to $Base | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "el reset devolvio $LASTEXITCODE" }
    $b = Read-Margin $Core
    if ($b -ne $Base) { throw "la base leida ($b) no es la pedida ($Base)" }
    Say "Base verificada en el nucleo ${Core}: $b" 'Green'

    # ---- CONTROL: el motor tiene que dar senal en un nivel que sabemos sano --
    $ctl = Invoke-Level $Base 'control'
    if ($ctl.Verdict -ne 'Ok' -or $ctl.Lines -lt $MinLines) {
        Say ""
        Say "CONTROL FALLIDO en la base ${Base}: veredicto $($ctl.Verdict), $($ctl.Lines) lineas." 'Red'
        Say "El motor no esta midiendo. Nada de lo que viniera despues valdria." 'Red'
        Say "Se para aqui." 'Red'
        exit 5
    }
    Say "  control OK: $($ctl.Lines) lineas en $Seconds s. El motor mide." 'Green'
    $ritmo = [math]::Round($ctl.Lines / ($Seconds / 60.0), 2)
    Say "  ritmo de referencia: $ritmo lineas/min" 'Green'

    # ---- ESCALERA ----------------------------------------------------------
    $positivo = $null
    foreach ($lvl in $Ladder) {
        $v = Invoke-Level $lvl 'nivel'

        if ($v.Verdict -eq 'Ok' -and $v.Lines -lt $MinLines) {
            Say "  $lvl SIN EVIDENCIA ($($v.Lines) lineas, esperabamos >= $MinLines)." 'Red'
            Say "  Un nivel sin evidencia no es un aprobado. Se aborta." 'Red'
            $positivo = $null
            break
        }
        if ($v.Verdict -eq 'Ok') {
            Say "  $lvl PASA  ($($v.Lines) lineas, 0 errores)" 'Green'
        } else {
            Say "  $lvl FALLA  -> $($v.Verdict) a los $($v.Elapsed)s" 'Red'
            $positivo = @{ Level = $lvl; Verdict = $v.Verdict; Elapsed = $v.Elapsed; Detail = $v.Detail }
            break
        }
        Start-Sleep -Seconds 8
    }

    Say ""
    Say "=====================================================================" 'Cyan'
    if ($positivo) {
        Say " PUERTA SUPERADA. El detector funciona." 'Green'
        Say " Primer positivo en $($positivo.Level), a los $($positivo.Elapsed)s: $($positivo.Verdict)" 'Green'
        if ($positivo.Detail) { Say " $($positivo.Detail)" 'Green' }
    } else {
        Say " SIN POSITIVO. Se para aqui." 'Red'
    }
    Say "=====================================================================" 'Cyan'
}
finally {
    Say ""
    Say "Restaurando: matando Prime95 y devolviendo los 16 nucleos a $Base..."
    Stop-P95
    & $exe reset --to $Base | Out-Null
    $f = Read-Margin $Core
    if ($f -eq $Base) { Say "Nucleo $Core queda en $f" 'Green' }
    else { Say "ATENCION: nucleo $Core queda en $f, no en $Base. Reinicia." 'Red' }
    Say "Registro: $log"
    Say "Datos:    $jsnl"
}
