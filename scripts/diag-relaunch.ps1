# ============================================================================
# Diagnostico: separar "el margen es inestable" de "el relanzamiento falla".
#
# Lanza Prime95 clavado al mismo nucleo N veces seguidas SIN tocar el Curve
# Optimizer. Todas las pasadas son identicas, asi que cualquier diferencia
# entre ellas es de nuestro arranque, no del silicio.
#
# No escribe en el SMU. No necesita consola elevada.
# ============================================================================
param(
    [int]$Core    = 11,
    [int]$Seconds = 180,
    [int]$Veces   = 3
)

$ErrorActionPreference = 'Stop'
$repo   = "$env:USERPROFILE\Proyectos\legion-co-lab"
$P95    = "$repo\tools\prime95"
$runs   = "$repo\runs\fase0"
$log    = "$runs\diag-relaunch.log"
$recipe = "$PSScriptRoot\prime95-recipe.txt"   # UNICA copia de la receta

New-Item -ItemType Directory -Force $runs | Out-Null

# Cuenta las ventanas "Worker #N" del proceso. La tortura abre una por
# trabajador; con NumCores=1 tiene que ser exactamente una.
$sig = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class W2 {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    static string Text(IntPtr h) {
        int len = (int)SendMessageW(h, 0x000E, IntPtr.Zero, IntPtr.Zero);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 2);
        SendMessageW(h, 0x000D, (IntPtr)(len + 1), sb);
        return sb.ToString();
    }
    public static int Workers(uint pid) {
        int n = 0;
        EnumWindows((h, p) => {
            uint q; GetWindowThreadProcessId(h, out q);
            if (q != pid) return true;
            // Con varios trabajadores: "Worker #N - Torture Test". Con uno solo: "Worker - Torture Test".
            EnumChildWindows(h, (c, r) => { if (Text(c).StartsWith("Worker")) n++; return true; }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return n;
    }
}
'@
Add-Type -TypeDefinition $sig -Language CSharp

function Say([string]$m) {
    $l = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $l
    Add-Content -Path $log -Value $l -Encoding utf8
}

function Count-Lines($path) {
    if (-not (Test-Path $path)) { return -1 }   # -1 = el fichero ni existe
    $t = Get-Content $path -Raw
    if (-not $t) { return 0 }
    @($t -split "`n" | Where-Object { $_.Trim() -and $_ -notmatch '^\[' }).Count
}

Say "Diagnostico de relanzamiento: $Veces pasadas identicas de $Seconds s en el nucleo $Core"
Say "Sin tocar el Curve Optimizer. Cualquier diferencia entre pasadas es nuestra."

$resumen = @()
for ($i = 1; $i -le $Veces; $i++) {
    Say ""
    Say "--- pasada $i de $Veces -------------------------------------------"

    $work = Join-Path $P95 "work\core$Core"
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $work | Out-Null
    Copy-Item $recipe (Join-Path $work 'prime.txt')
    $res = Join-Path $work 'results.txt'

    $p = Start-Process -FilePath (Join-Path $P95 'prime95.exe') `
                       -ArgumentList '-t', "-W$work" `
                       -WorkingDirectory $P95 -PassThru -WindowStyle Minimized
    Start-Sleep -Milliseconds 900
    $p.ProcessorAffinity = [IntPtr]([int64]3 -shl (2 * $Core))
    $cpu0 = $p.CPU
    # La ventana del trabajador tarda unos segundos en existir; esperar hasta 15 s.
    $workers = 0
    for ($k = 0; $k -lt 15 -and $workers -eq 0; $k++) { Start-Sleep -Seconds 1; $workers = [W2]::Workers([uint32]$p.Id) }
    Say ("  pid $($p.Id)  afinidad 0x{0:X}   trabajadores: {1}" -f [int64]$p.ProcessorAffinity, $workers)
    if ($workers -ne 1) {
        Say "  ABORTADO: la tortura abrio $workers trabajadores, tenia que ser 1. La receta no manda."
        Get-Process prime95 -ErrorAction SilentlyContinue | Stop-Process -Force
        exit 6
    }

    $t0 = Get-Date
    $primera = $null
    while (((Get-Date) - $t0).TotalSeconds -lt $Seconds) {
        Start-Sleep -Seconds 5
        $el = [int]((Get-Date) - $t0).TotalSeconds
        $n  = Count-Lines $res
        if ($n -gt 0 -and -not $primera) { $primera = $el }
        $p.Refresh()
        $tasa = if ($el -gt 0) { [math]::Round(($p.CPU - $cpu0) / $el, 2) } else { 0 }
        $existe = if (Test-Path $res) { 'si' } else { 'NO' }
        Say ("  {0,3}s   results.txt existe: {1}   lineas: {2,2}   CPU: {3} logicos" -f $el, $existe, $n, $tasa)
    }

    $n = Count-Lines $res
    $p.Refresh()
    $tasa = [math]::Round(($p.CPU - $cpu0) / $Seconds, 2)
    Get-Process prime95 -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2

    $resumen += [pscustomobject]@{
        Pasada = $i; Lineas = $n; PrimeraLinea = $primera; CpuLogicos = $tasa; Workers = $workers
    }
    Say "  pasada ${i}: $n lineas, primera a los $(if($primera){"${primera}s"}else{'nunca'}), $tasa logicos de CPU"
    Start-Sleep -Seconds 8
}

Say ""
Say "=== RESUMEN ==="
foreach ($r in $resumen) {
    Say ("  pasada {0}: {1,2} lineas   primera {2,-7}   CPU {3} logicos   trabajadores {4}" -f `
         $r.Pasada, $r.Lineas, $(if($r.PrimeraLinea){"$($r.PrimeraLinea)s"}else{'nunca'}), $r.CpuLogicos, $r.Workers)
}
$conPrimera = @($resumen | Where-Object { $_.PrimeraLinea }) | ForEach-Object { $_.PrimeraLinea } | Sort-Object
if ($conPrimera.Count -gt 0) {
    $mediana = $conPrimera[[int][math]::Floor(($conPrimera.Count - 1) / 2)]
    $lpm = [math]::Round((($resumen | Measure-Object -Property Lineas -Average).Average) / ($Seconds / 60.0), 2)
    Say "LINEA BASE: primera linea mediana ${mediana}s   ritmo medio $lpm lineas/min"
}
$conLineas = @($resumen | Where-Object { $_.Lineas -gt 0 }).Count
if ($conLineas -eq $Veces) {
    Say "TODAS dieron senal. El relanzamiento es fiable -> el 0 de -8 fue del margen."
} elseif ($conLineas -eq 0) {
    Say "NINGUNA dio senal. El motor no mide de forma reproducible."
} else {
    Say "SOLO $conLineas de $Veces dieron senal con configuracion IDENTICA."
    Say "El relanzamiento NO es fiable. El 0 de -8 no prueba nada del silicio."
}
Say "Registro: $log"
