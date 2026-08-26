# Lee el texto de las ventanas de Prime95. La tortura escribe su estado en la
# ventana del trabajador; results.txt solo recibe el veredicto final de cada
# autotest. Si el fichero no aparece, la ventana es el unico sitio donde mirar.
$sig = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class Win {
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, IntPtr l);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    const uint WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E;

    static string Text(IntPtr h) {
        int len = (int)SendMessageW(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 2);
        SendMessageW(h, WM_GETTEXT, (IntPtr)(len + 1), sb);
        return sb.ToString();
    }

    public static List<string> Dump(uint targetPid) {
        var outp = new List<string>();
        EnumWindows((h, p) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != targetPid) return true;
            var cn = new StringBuilder(256); GetClassName(h, cn, 256);
            outp.Add("[ventana] clase=" + cn + " texto=" + Text(h));
            EnumChildWindows(h, (c, q) => {
                var cc = new StringBuilder(256); GetClassName(c, cc, 256);
                string t = Text(c);
                if (t.Trim().Length > 0) outp.Add("  [hijo " + cc + "] " + t);
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return outp;
    }
}
'@
Add-Type -TypeDefinition $sig -Language CSharp

$p = Get-Process prime95 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host "prime95 no esta corriendo"; exit 1 }
Write-Host "=== prime95 pid $($p.Id) ==="
[Win]::Dump([uint32]$p.Id) | ForEach-Object { Write-Host $_ }
