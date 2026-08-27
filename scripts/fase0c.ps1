# ============================================================================
# Fase 0c: contraste con CoreCycler. colab pone el margen, CoreCycler (modo
# manual, config.ini del clon) prueba el nucleo, colab restaura.
# CoreCycler no lee el margen del hardware: la sonda antes y despues es la
# unica evidencia de lo que habia en el silicio mientras media.
#
# Escribe en el SMU: consola elevada.
# ============================================================================
param(
    [int]$Core   = 11,
    [int]$Margin = -45,
    [int]$Base   = -5,
    [int]$MaxMin = 9          # tope de espera a CoreCycler (runtimePerCore 6m + arranque)
)

$ErrorActionPreference = 'Stop'
$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$exe  = "$repo\src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe"
$cc   = "$env:USERPROFILE\Proyectos\corecycler"
$runs = "$repo\runs\fase0"
$log  = "$runs\fase0c-m$Margin-c$Core.log"

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

try {
    Say "Fase 0c: CoreCycler manual sobre el nucleo $Core a $Margin (config.ini del clon)"
    Get-Content "$cc\config.ini" | Where-Object { $_ -match '^\w' } | ForEach-Object { Say "  cfg: $_" }

    & $exe apply --core $Core --margin $Margin | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "apply devolvio $LASTEXITCODE" }
    $leido = Read-Margin $Core
    if ($leido -ne $Margin) { throw "VERIFICACION FALLIDA: pedido $Margin, hardware $leido" }
    Say "Hardware verificado en $leido"

    $antes = Get-ChildItem "$cc\logs" -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
    $p = Start-Process -FilePath 'powershell.exe' -PassThru -WorkingDirectory $cc `
            -ArgumentList '-ExecutionPolicy','Bypass','-File',"$cc\script-corecycler.ps1"
    Say "CoreCycler pid $($p.Id); espera maxima $MaxMin min"
    $t0 = Get-Date
    while (-not $p.HasExited -and ((Get-Date) - $t0).TotalMinutes -lt $MaxMin) {
        Start-Sleep -Seconds 30
        $m = Read-Margin $Core
        Say ("  {0,3}s   CoreCycler vivo   hardware {1}" -f [int]((Get-Date) - $t0).TotalSeconds, $m)
        if ($m -ne $Margin) { Say "  EL MARGEN HA CAMBIADO BAJO COREYCLER: $m"; }
    }
    if (-not $p.HasExited) { Say "CoreCycler sigue vivo al tope de espera; se cierra"; Stop-Process -Id $p.Id -Force }
    else { Say "CoreCycler termino solo (exit $($p.ExitCode))" }
    Get-Process -Name '04-P4P','y-cruncher' -ErrorAction SilentlyContinue | Stop-Process -Force

    $nuevo = Get-ChildItem "$cc\logs" -Filter '*.log' -ErrorAction SilentlyContinue |
             Where-Object { $antes -notcontains $_.FullName } | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($nuevo) {
        Say "Registro de CoreCycler: $($nuevo.FullName)"
        Get-Content $nuevo.FullName | Where-Object { $_ -match '(?i)error|whea|passed|fail|core 11|finished|completed|iteration' } |
            Select-Object -Last 25 | ForEach-Object { Say "  cc: $_" }
    } else { Say "CoreCycler no dejo registro nuevo en $cc\logs" }
}
finally {
    Say ""
    Say "Restaurando a $Base..."
    & $exe reset --to $Base | Out-Null
    $f = Read-Margin $Core
    Say "Nucleo $Core queda en $f"
    Say "Registro: $log"
}
