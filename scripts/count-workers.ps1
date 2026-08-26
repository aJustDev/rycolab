# Cuenta las ventanas de trabajador que abre la tortura de Prime95 y vigila
# results.txt, sin tocar el Curve Optimizer.
#
# La tortura ignora NumWorkers y arranca un trabajador por nucleo. Con la
# mascara del proceso todos caen en el mismo nucleo fisico, asi que el numero
# de trabajadores decide cuanto trabajo toca a cada uno. Si ese numero varia
# entre pasadas, varia el tiempo de cada autotest, y entonces "no escribio
# nada" no dice nada del silicio.
param(
    [int]$Core    = 11,
    [int]$Seconds = 90
)

$ErrorActionPreference = 'Stop'
$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$P95  = "$repo\tools\prime95"

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
    public static List<string> Workers(uint pid) {
        var o = new List<string>();
        EnumWindows((h, p) => {
            uint q; GetWindowThreadProcessId(h, out q);
            if (q != pid) return true;
            EnumChildWindows(h, (c, r) => {
                string t = Text(c);
                if (t.StartsWith("Worker #")) o.Add(t);
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return o;
    }
}
'@
Add-Type -TypeDefinition $sig -Language CSharp

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
Write-Host ("pid $($p.Id)  afinidad 0x{0:X}" -f [int64]$p.ProcessorAffinity)

$t0 = Get-Date
while (((Get-Date) - $t0).TotalSeconds -lt $Seconds) {
    Start-Sleep -Seconds 10
    $el = [int]((Get-Date) - $t0).TotalSeconds
    $w  = [W2]::Workers([uint32]$p.Id)
    $n  = if (Test-Path $res) { @(Get-Content $res | Where-Object { $_.Trim() -and $_ -notmatch '^\[' }).Count } else { -1 }
    Write-Host ("  {0,3}s   trabajadores: {1,2}   results.txt lineas: {2}" -f $el, $w.Count, $n)
}
if (Test-Path $res) { Write-Host "=== results.txt ==="; Get-Content $res }
Get-Process prime95 -ErrorAction SilentlyContinue | Stop-Process -Force
