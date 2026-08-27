# ============================================================================
# y-cruncher clavado a un nucleo, a un margen dado, N pasadas. Calco de
# diag-margin.ps1 con otro motor.
#
# Binarios y plantilla del cfg: CoreCycler (clon en ~/Proyectos/corecycler),
#   test_programs/y-cruncher/Binaries/<modo>.exe          (04-P4P ligero, 24-ZN5 pesado)
#   script-corecycler.ps1:8568-8603  (Action StressTest, LogicalCores, TotalMemory,
#                                     SecondsPerTest, StopOnError, Tests)
#   script-corecycler.ps1:1237       (priority:-1 config <cfg>)
#   script-corecycler.ps1:8421       (pause:-2 colors:0 para que no espere tecla)
#
# Escribe en el SMU: consola elevada. Restaura la base al terminar.
# ============================================================================
param(
    [int]$Core     = 11,
    [int]$Margin   = -25,
    [int]$Base     = -5,
    [int]$Seconds  = 360,
    [int]$Veces    = 1,
    [string]$Modo  = '04-P4P',              # o '24-ZN5 ~ Komari'
    [string]$Tests = 'SFTv4,FFTv4,N63',
    [long]$MemoriaBytes = 1GB,
    [switch]$Suspender
)

$ErrorActionPreference = 'Stop'
$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$exe  = "$repo\src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe"
$ycr  = "$env:USERPROFILE\Proyectos\corecycler\test_programs\y-cruncher\Binaries\$Modo.exe"
$runs = "$repo\runs\fase0"
$tag  = '-ycr-' + ($Modo -replace '[^0-9A-Za-z]', '')
if ($Suspender) { $tag += '-susp' }
if ($Core -ne 11) { $tag += "-c$Core" }   # el 11 fue el primero y sus ficheros no llevan sufijo
$log  = "$runs\diag-margin$Margin$tag.log"

if (-not (Test-Path $ycr)) { throw "no existe $ycr" }
New-Item -ItemType Directory -Force $runs | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
public static class Hilos {
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint tid);
    [DllImport("kernel32.dll")] static extern int SuspendThread(IntPtr h);
    [DllImport("kernel32.dll")] static extern int ResumeThread(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    const uint THREAD_SUSPEND_RESUME = 0x0002;
    public static int Suspend(int pid) { return Apply(pid, true); }
    public static int Resume(int pid)  { return Apply(pid, false); }
    static int Apply(int pid, bool suspend) {
        int n = 0;
        foreach (ProcessThread t in Process.GetProcessById(pid).Threads) {
            IntPtr h = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)t.Id);
            if (h == IntPtr.Zero) continue;
            int r = suspend ? SuspendThread(h) : ResumeThread(h);
            if (r >= 0) n++;
            CloseHandle(h);
        }
        return n;
    }
}
'@

function Say([string]$m) {
    $l = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $l
    Add-Content -Path $log -Value $l -Encoding utf8
}
function Read-Margin([int]$c) {
    $tmp = Join-Path $runs 'probe.json'
    & $exe probe --no-compare --json $tmp | Out-Null
    ((Get-Content $tmp -Raw | ConvertFrom-Json).psm | Where-Object { $_.core -eq $c }).margin
}
# Lineas de la salida de y-cruncher que huelen a error. Las de "0 errors"/"Passed" no cuentan.
function Errores($path) {
    if (-not (Test-Path $path)) { return @() }
    @(Get-Content $path | Where-Object { $_ -match '(?i)error|fail|mismatch|invalid|exception' -and $_ -notmatch '(?i)0 errors|no errors|passed|Stop on Error' })
}
function Lineas($path) {
    if (-not (Test-Path $path)) { return -1 }
    @(Get-Content $path | Where-Object { $_.Trim() }).Count
}

try {
    Say "y-cruncher $Modo  tests $Tests  margen $Margin  nucleo $Core  $Veces x $Seconds s$(if($Suspender){'  con suspension'})"

    & $exe apply --core $Core --margin $Margin | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "apply devolvio $LASTEXITCODE" }
    $leido = Read-Margin $Core
    if ($leido -ne $Margin) { throw "VERIFICACION FALLIDA: pedido $Margin, hardware $leido" }
    Say "Hardware verificado en $leido"

    $work = Join-Path $runs "ycr-core$Core"
    New-Item -ItemType Directory -Force $work | Out-Null
    $cfg = Join-Path $work 'stressTest.cfg'
    $testsCfg = ($Tests -split ',' | ForEach-Object { '            "' + $_.Trim() + '"' }) -join "`n"
    @"
{
    Action : "StressTest"
    StressTest : {
        AllocateLocally : "true"
        LogicalCores : [$(2 * $Core)]
        TotalMemory : $MemoriaBytes
        SecondsPerTest : 60
        SecondsTotal : 0
        StopOnError : "true"
        Tests : [
$testsCfg
        ]
    }
}
"@ | Set-Content -Path $cfg -Encoding ascii
    $nul = Join-Path $work 'nul.txt'
    Set-Content -Path $nul -Value '' -Encoding ascii

    $resumen = @()
    for ($i = 1; $i -le $Veces; $i++) {
        Say ""
        Say "--- pasada $i de $Veces (margen $Margin, $Modo) ---------------------------"
        $out = Join-Path $work "salida-p$i.txt"
        Remove-Item $out -ErrorAction SilentlyContinue

        $p = Start-Process -FilePath $ycr -ArgumentList 'pause:-2','colors:0','priority:-1','config',"`"$cfg`"" `
                           -WorkingDirectory (Split-Path $ycr) -PassThru -WindowStyle Minimized `
                           -RedirectStandardOutput $out -RedirectStandardInput $nul
        Start-Sleep -Milliseconds 900
        $p.ProcessorAffinity = [IntPtr]([int64]3 -shl (2 * $Core))
        $cpu0 = $p.CPU
        Say ("  pid $($p.Id)  afinidad 0x{0:X}" -f [int64]$p.ProcessorAffinity)

        $wj = Join-Path $runs "watch-m$Margin$tag-p$i.jsonl"
        $ws = Join-Path $runs "watch-m$Margin$tag-p$i.json"
        $wp = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $runs "watch-m$Margin$tag-p$i.txt") `
                -ArgumentList 'watch','--core',$Core,'--seconds',$Seconds,'--interval','1000','--jsonl',$wj,'--summary',$ws

        $t0 = Get-Date
        $errs = $null
        $suspensiones = 0
        while (((Get-Date) - $t0).TotalSeconds -lt $Seconds) {
            if ($Suspender -and -not $p.HasExited) {
                Start-Sleep -Seconds 9
                [Hilos]::Suspend($p.Id) | Out-Null; Start-Sleep -Milliseconds 1000; [Hilos]::Resume($p.Id) | Out-Null
                $suspensiones++
            } else {
                Start-Sleep -Seconds 10
            }
            $el = [int]((Get-Date) - $t0).TotalSeconds
            $n  = Lineas $out
            $e  = Errores $out
            if ($e.Count -gt 0 -and -not $errs) { $errs = $e -join ' | '; Say "  ERROR a los ${el}s: $errs" }
            $vivo = -not $p.HasExited
            if ($vivo) { $p.Refresh() }
            $tasa = if ($vivo -and $el -gt 0) { [math]::Round(($p.CPU - $cpu0) / $el, 2) } else { 0 }
            Say ("  {0,3}s   salida: {1,3} lineas   CPU: {2} logicos   {3}" -f $el, $n, $tasa, $(if($vivo){'vivo'}else{"MUERTO (exit $($p.ExitCode))"}))
            if (-not $vivo) { break }
        }

        $wp.WaitForExit(20000) | Out-Null
        $murio = $p.HasExited
        $codigo = if ($murio) { $p.ExitCode } else { $null }
        if (-not $murio) { Stop-Process -Id $p.Id -Force }
        Start-Sleep -Seconds 2
        $tele = if (Test-Path $ws) { Get-Content $ws -Raw | ConvertFrom-Json } else { $null }
        $resumen += [pscustomobject]@{ Pasada = $i; Lineas = (Lineas $out); Error = $errs; Murio = $murio; Codigo = $codigo; Tele = $tele }
        Say "  pasada ${i}: $(Lineas $out) lineas de salida$(if($murio){"  proceso terminado, exit $codigo"})$(if($Suspender){"  suspensiones $suspensiones"})"
        if ($tele) {
            Say ("  telemetria: {0} muestras  reloj {1:F0}  efectivo {2:F0} (p10 {3:F0})  V {4:F4} (max {5:F4})  GHz {6:F3}  W nucleo {7:F2}  W paquete {8:F1}  T {9:F1} (max {10:F1})" -f `
                 $tele.samples, $tele.clockMedian, $tele.clockEffectiveMedian, $tele.clockEffectiveP10, $tele.voltMedian, $tele.voltMax, $tele.freqMedian, $tele.powerMedian, $tele.packagePowerMedian, $tele.tempMedian, $tele.tempMax)
        }
        Start-Sleep -Seconds 8
    }

    Say ""
    Say "=== RESUMEN  y-cruncher $Modo  margen $Margin  nucleo $Core ==="
    foreach ($r in $resumen) {
        $t = $r.Tele
        $tt = if ($t) { "  V {0:F4}  GHz {1:F3}  W nucleo {2:F2}  T {3:F1}" -f $t.voltMedian, $t.freqMedian, $t.powerMedian, $t.tempMedian } else { '  sin telemetria' }
        Say ("  pasada {0}: {1,3} lineas   {2}{3}{4}" -f $r.Pasada, $r.Lineas, $(if($r.Error){"ERROR: $($r.Error)"}else{'sin error'}), $(if($r.Murio){"  exit $($r.Codigo)"}), $tt)
    }
    $pos = @($resumen | Where-Object { $_.Error -or $_.Murio }).Count
    Say ""
    Say $(if ($pos -gt 0) { "POSITIVO: $pos de $Veces pasadas con error o proceso terminado." } else { "TODAS limpias a $Margin con $Modo." })
}
finally {
    Say ""
    Say "Restaurando a $Base..."
    Get-Process -Name ($Modo -replace '\.exe$','') -ErrorAction SilentlyContinue | Stop-Process -Force
    & $exe reset --to $Base | Out-Null
    $f = Read-Margin $Core
    Say "Nucleo $Core queda en $f"
    Say "Registro: $log"
}
