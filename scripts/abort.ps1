# Corta en seco una campana en curso y devuelve los 16 nucleos a la base.
# El bloque finally de fase0.ps1 no se ejecuta si se mata el proceso, asi que
# el reset lo hacemos aqui explicitamente.
param([int]$Base = -5)

$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$exe  = "$repo\src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe"
$out  = "$repo\runs\fase0\abort.log"

function Say([string]$m) {
    $l = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $l
    Add-Content -Path $out -Value $l -Encoding utf8
}

Say "Abortando la campana en curso."

Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" |
    Where-Object { $_.CommandLine -like '*fase0.ps1*' } |
    ForEach-Object {
        Say "  matando el corredor pid $($_.ProcessId)"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }

Get-Process prime95 -ErrorAction SilentlyContinue | ForEach-Object {
    Say "  matando prime95 pid $($_.Id)"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Seconds 1

Say "Devolviendo los 16 nucleos a $Base..."
& $exe reset --to $Base | Out-Null
Say "  reset devolvio $LASTEXITCODE"

$tmp = "$repo\runs\fase0\abort-probe.json"
& $exe probe --no-compare --json $tmp | Out-Null
$j = Get-Content $tmp -Raw | ConvertFrom-Json
$m = $j.psm | ForEach-Object { $_.margin } | Sort-Object -Unique
Say "Margenes en el hardware ahora: $($m -join ', ')"
if ($m.Count -eq 1 -and $m[0] -eq $Base) { Say "OK, estado limpio en $Base." }
else { Say "ATENCION: no todos los nucleos estan en $Base. Reinicia para volver al valor de la BIOS." }
