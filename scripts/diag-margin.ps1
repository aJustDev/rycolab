# ============================================================================
# Repite N pasadas identicas de Prime95 en un nucleo a un margen dado.
#
# Mismo experimento que diag-relaunch.ps1, pero aplicando antes un margen. Con
# el relanzamiento ya demostrado fiable (3/3 a -5), cualquier diferencia frente
# a la base es atribuible al margen.
#
# Escribe en el SMU: necesita consola elevada. Restaura la base al terminar.
# ============================================================================
param(
    [int]$Core    = 11,
    [int]$Margin  = -8,
    [int]$Base    = -5,
    [int]$Seconds = 180,
    [int]$Veces   = 3
)

$ErrorActionPreference = 'Stop'
$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$exe  = "$repo\src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe"
$P95  = "$repo\tools\prime95"
$runs = "$repo\runs\fase0"
$log  = "$runs\diag-margin$Margin.log"

New-Item -ItemType Directory -Force $runs | Out-Null

function Say([string]$m) {
    $l = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $l
    Add-Content -Path $log -Value $l -Encoding utf8
}
function Count-Lines($path) {
    if (-not (Test-Path $path)) { return -1 }
    $t = Get-Content $path -Raw
    if (-not $t) { return 0 }
    @($t -split "`n" | Where-Object { $_.Trim() -and $_ -notmatch '^\[' }).Count
}
function Read-Margin([int]$c) {
    $tmp = Join-Path $runs 'probe.json'
    & $exe probe --no-compare --json $tmp | Out-Null
    ((Get-Content $tmp -Raw | ConvertFrom-Json).psm | Where-Object { $_.core -eq $c }).margin
}

try {
    Say "Margen $Margin en el nucleo $Core, $Veces pasadas de $Seconds s."
    Say "Base $Base en los otros 15. El relanzamiento ya esta probado fiable."

    & $exe apply --core $Core --margin $Margin | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "apply devolvio $LASTEXITCODE" }
    $leido = Read-Margin $Core
    if ($leido -ne $Margin) { throw "VERIFICACION FALLIDA: pedido $Margin, hardware $leido" }
    Say "Hardware verificado en $leido"

    $resumen = @()
    for ($i = 1; $i -le $Veces; $i++) {
        Say ""
        Say "--- pasada $i de $Veces (margen $Margin) ---------------------------"

        $work = Join-Path $P95 "work\core$Core"
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force $work | Out-Null
        Copy-Item "$PSScriptRoot\prime95-recipe.txt" (Join-Path $work 'prime.txt')   # UNICA copia de la receta
        $res = Join-Path $work 'results.txt'

        $p = Start-Process -FilePath (Join-Path $P95 'prime95.exe') `
                           -ArgumentList '-t', "-W$work" `
                           -WorkingDirectory $P95 -PassThru -WindowStyle Minimized
        Start-Sleep -Milliseconds 900
        $p.ProcessorAffinity = [IntPtr]([int64]3 -shl (2 * $Core))
        $cpu0 = $p.CPU
        Say ("  pid $($p.Id)  afinidad 0x{0:X}" -f [int64]$p.ProcessorAffinity)

        # Telemetria a 1 Hz del nucleo bajo carga (reloj, efectivo, W, Tctl, tabla PM cruda)
        $wj = Join-Path $runs "watch-m$Margin-p$i.jsonl"
        $ws = Join-Path $runs "watch-m$Margin-p$i.json"
        $wp = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $runs "watch-m$Margin-p$i.txt") `
                -ArgumentList 'watch','--core',$Core,'--seconds',$Seconds,'--interval','1000','--raw','--jsonl',$wj,'--summary',$ws

        $t0 = Get-Date
        $primera = $null
        $error95 = $null
        while (((Get-Date) - $t0).TotalSeconds -lt $Seconds) {
            Start-Sleep -Seconds 10
            $el = [int]((Get-Date) - $t0).TotalSeconds
            $n  = Count-Lines $res
            if ($n -gt 0 -and -not $primera) { $primera = $el }

            $txt = if (Test-Path $res) { Get-Content $res -Raw } else { '' }
            if ($txt -match 'FATAL ERROR|Hardware failure|Rounding was' -and -not $error95) {
                $error95 = @($txt -split "`n" | Where-Object { $_ -match 'FATAL ERROR|Hardware failure|Rounding was' }) -join ' | '
                Say "  ERROR DE CALCULO a los ${el}s: $error95"
            }

            $vivo = -not $p.HasExited
            if ($vivo) { $p.Refresh() }
            $tasa = if ($vivo -and $el -gt 0) { [math]::Round(($p.CPU - $cpu0) / $el, 2) } else { 0 }
            $titulo = if ($vivo) { $p.MainWindowTitle } else { '(muerto)' }
            $existe = if (Test-Path $res) { 'si' } else { 'NO' }
            Say ("  {0,3}s   results.txt: {1}   lineas: {2,2}   CPU: {3} logicos   ventana: {4}" -f `
                 $el, $existe, $n, $tasa, $titulo)
            if (-not $vivo) { Say "  el proceso MURIO a los ${el}s"; break }
        }

        $wp.WaitForExit(20000) | Out-Null
        $n = Count-Lines $res
        Get-Process prime95 -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
        $tele = if (Test-Path $ws) { Get-Content $ws -Raw | ConvertFrom-Json } else { $null }
        $resumen += [pscustomobject]@{ Pasada = $i; Lineas = $n; Primera = $primera; Error = $error95; Tele = $tele }
        Say "  pasada ${i}: $n lineas, primera $(if($primera){"${primera}s"}else{'nunca'})"
        if ($tele) {
            Say ("  telemetria: {0} muestras  reloj {1:F0}  efectivo {2:F0} (p10 {3:F0})  V {4:F4} (max {5:F4})  GHz {6:F3}  W nucleo {7:F2}  W paquete {8:F1}  T {9:F1} (max {10:F1})" -f `
                 $tele.samples, $tele.clockMedian, $tele.clockEffectiveMedian, $tele.clockEffectiveP10, $tele.voltMedian, $tele.voltMax, $tele.freqMedian, $tele.powerMedian, $tele.packagePowerMedian, $tele.tempMedian, $tele.tempMax)
        } else { Say "  telemetria: SIN RESUMEN (watch no termino?)" }
        Start-Sleep -Seconds 8
    }

    Say ""
    Say "=== RESUMEN  margen $Margin  nucleo $Core ==="
    foreach ($r in $resumen) {
        $t = $r.Tele
        $tt = if ($t) { "  V {0:F4}  GHz {1:F3}  W nucleo {2:F2}  T {3:F1}" -f $t.voltMedian, $t.freqMedian, $t.powerMedian, $t.tempMedian } else { '  sin telemetria' }
        Say ("  pasada {0}: {1,2} lineas   primera {2,-7}   {3}{4}" -f `
             $r.Pasada, $r.Lineas, $(if($r.Primera){"$($r.Primera)s"}else{'nunca'}), $(if($r.Error){"ERROR: $($r.Error)"}else{'sin error cantado'}), $tt)
    }
    $mudas = @($resumen | Where-Object { $_.Lineas -le 0 }).Count
    $errs  = @($resumen | Where-Object { $_.Error }).Count
    Say ""
    if ($errs -gt 0) {
        Say "POSITIVO: $errs de $Veces pasadas cantaron error de calculo."
    } elseif ($mudas -eq $Veces) {
        Say "MUDO EN LAS ${Veces}: rendimiento a cero sin error cantado."
        Say "Frente a 4 de 4 pasadas con senal en la base, la diferencia es del margen."
    } elseif ($mudas -gt 0) {
        Say "INTERMITENTE: $mudas de $Veces mudas. Reproducible a medias."
    } else {
        Say "TODAS con senal: a $Margin el nucleo se comporta como en la base."
        Say "El cero de la pasada anterior fue casualidad. Hay que seguir bajando."
    }
}
finally {
    Say ""
    Say "Restaurando a $Base..."
    Get-Process prime95 -ErrorAction SilentlyContinue | Stop-Process -Force
    & $exe reset --to $Base | Out-Null
    $f = Read-Margin $Core
    Say "Nucleo $Core queda en $f"
    Say "Registro: $log"
}
