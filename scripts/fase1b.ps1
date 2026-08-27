# ============================================================================
# Fase 1b: perfil candidato en los 16 nucleos + soak en reposo con vigilancia
# de WHEA y del margen leido del hardware. Al terminar (o al fallar) restaura
# la base. Deja runs\fase1b\fase1b.log y runs\fase1b\resultado.json.
#
# Escribe en el SMU: consola elevada. La maquina debe quedarse en reposo.
# Si el equipo se reinicia, la BIOS devuelve -5 sola; runs\fase1b\en-curso.json
# queda como prueba de que el soak estaba en marcha.
# ============================================================================
param(
    [int[]]$Perfil = @(-35,-35,-35,-40,-40,-40,-40,-40, -40,-35,-45,-40,-40,-45,-45,-45),
    [int]$Base     = -5,
    [int]$Minutos  = 30,
    [int]$Intervalo = 60
)

$ErrorActionPreference = 'Stop'
$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$exe  = "$repo\src\LegionCoLab.Cli\bin\Release\net9.0-windows\win-x64\colab.exe"
$runs = "$repo\runs\fase1b"
$log  = "$runs\fase1b.log"
$enCurso = "$runs\en-curso.json"
New-Item -ItemType Directory -Force $runs | Out-Null
if ($Perfil.Count -ne 16) { throw "el perfil necesita 16 valores, tiene $($Perfil.Count)" }

function Say([string]$m) {
    $l = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $l
    Add-Content -Path $log -Value $l -Encoding utf8
}
function Read-Margins {
    $tmp = Join-Path $runs 'probe.json'
    & $exe probe --no-compare --json $tmp | Out-Null
    $p = (Get-Content $tmp -Raw | ConvertFrom-Json).psm | Sort-Object core
    [int[]]($p | ForEach-Object { $_.margin })
}
function Whea([datetime]$desde) {
    @(Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-WHEA-Logger'; StartTime=$desde} -ErrorAction SilentlyContinue)
}

$t0 = Get-Date
$wheaAntes = (Whea $t0.Date).Count
$muestras = @()
$salida = 1
try {
    Say "Fase 1b: perfil $($Perfil -join ',')  soak $Minutos min, muestra cada $Intervalo s"
    Say "WHEA hoy antes de empezar: $wheaAntes"

    for ($c = 0; $c -lt 16; $c++) {
        & $exe apply --core $c --margin $Perfil[$c] | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "apply nucleo $c a $($Perfil[$c]) devolvio $LASTEXITCODE" }
    }
    $leido = Read-Margins
    if (Compare-Object $leido $Perfil -SyncWindow 0) { throw "VERIFICACION FALLIDA: hardware $($leido -join ',')" }
    Say "Hardware verificado: $($leido -join ',')"
    [pscustomobject]@{ perfil = $Perfil; ts = $t0.ToString('s') } | ConvertTo-Json -Compress | Set-Content $enCurso -Encoding ascii

    $fin = (Get-Date).AddMinutes($Minutos)
    $tInicioSoak = Get-Date
    while ((Get-Date) -lt $fin) {
        Start-Sleep -Seconds $Intervalo
        $m = Read-Margins
        $ok = -not (Compare-Object $m $Perfil -SyncWindow 0)
        $w = (Whea $tInicioSoak)
        $cpu = (Get-CimInstance Win32_Processor).LoadPercentage
        $el = [int]((Get-Date) - $tInicioSoak).TotalMinutes
        $muestras += [pscustomobject]@{ min = $el; margenOk = $ok; whea = $w.Count; cpu = $cpu; ts = (Get-Date).ToString('s') }
        Say ("  {0,3} min   margen {1}   WHEA {2}   CPU {3}%" -f $el, $(if($ok){'ok'}else{"CAMBIADO: $($m -join ',')"}), $w.Count, $cpu)
        if (-not $ok) { throw "el margen ha cambiado bajo el soak" }
        if ($w.Count -gt 0) { $w | ForEach-Object { Say "  WHEA $($_.TimeCreated.ToString('HH:mm:ss')) id $($_.Id): $(($_.Message -split "`n")[0])" }; break }
    }
    $wheaSoak = (Whea $tInicioSoak)
    $salida = if ($wheaSoak.Count -gt 0) { 10 } else { 0 }
    Say $(if ($salida -eq 0) { "SOAK LIMPIO: $Minutos min en reposo con el perfil, WHEA 0" } else { "POSITIVO: $($wheaSoak.Count) WHEA durante el soak" })
}
finally {
    Remove-Item $enCurso -ErrorAction SilentlyContinue
    Say "Restaurando a $Base..."
    & $exe reset --to $Base | Out-Null
    $f = Read-Margins
    Say "Hardware queda en: $(($f | Sort-Object -Unique) -join ',')"
    [pscustomobject]@{ perfil = $Perfil; minutos = $Minutos; codigo = $salida; muestras = $muestras; fecha = (Get-Date).ToString('s') } |
        ConvertTo-Json -Depth 4 | Set-Content "$runs\resultado.json" -Encoding ascii
    Say "Registro: $log"
}
exit $salida
