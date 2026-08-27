# ============================================================================
# Fase 1: barrido por nucleos. Para cada nucleo, cada motor de y-cruncher a
# un margen de partida; si canta, sube Paso cuentas y repite hasta pasar o
# llegar a Tope. Deja un JSON por nucleo en runs\fase1\.
#
# Reutiliza diag-ycruncher.ps1 (pone el margen, prueba, restaura, exit 0/10).
# Con el conocimiento del 27/08 (docs/RESULTADOS.md): el nucleo 11 no falla
# en todo el rango en 6 min, asi que se empieza por el minimo del SMU y se
# sube solo si hay positivo. "Limite" = primer margen limpio en ambos motores.
#
# Escribe en el SMU: consola elevada. Cada motor y margen restaura -5 al
# terminar (lo hace diag-ycruncher.ps1), asi que un corte deja la base puesta.
# ============================================================================
param(
    [int[]]$Nucleos = @(0..15),
    [int]$Inicio    = -50,
    [int]$Tope      = -5,
    [int]$Paso      = 5,
    [int]$Seconds   = 360,
    [string[]]$Modos = @('04-P4P', '24-ZN5 ~ Komari'),
    [switch]$Suspender = $true
)

$ErrorActionPreference = 'Stop'
$repo = "$env:USERPROFILE\Proyectos\legion-co-lab"
$runs = "$repo\runs\fase1"
$log  = "$runs\fase1.log"
New-Item -ItemType Directory -Force $runs | Out-Null

function Say([string]$m) {
    $l = "{0}  {1}" -f (Get-Date -Format 'HH:mm:ss'), $m
    Write-Host $l
    Add-Content -Path $log -Value $l -Encoding utf8
}

Say "Fase 1: nucleos $($Nucleos -join ',')  desde $Inicio hasta $Tope de $Paso en $Paso  motores $($Modos -join ' | ')  $Seconds s"
$t0 = Get-Date
foreach ($c in $Nucleos) {
    $json = Join-Path $runs "core$c.json"
    if (Test-Path $json) { Say "nucleo ${c}: ya tiene resultado, se salta"; continue }
    $historial = @()
    $limite = $null
    for ($m = $Inicio; $m -le $Tope; $m += $Paso) {
        $limpio = $true
        foreach ($modo in $Modos) {
            Say "nucleo $c  margen $m  $modo"
            # Proceso aparte: el exit y las excepciones del hijo no pueden tumbar el barrido
            $args = @('-NoProfile','-File',"$PSScriptRoot\diag-ycruncher.ps1",'-Core',$c,'-Margin',$m,'-Veces','1','-Seconds',$Seconds,'-Modo',"`"$modo`"")
            if ($Suspender) { $args += '-Suspender' }
            $hijo = Start-Process -FilePath 'pwsh' -ArgumentList $args -PassThru -Wait -WindowStyle Minimized
            $code = $hijo.ExitCode
            $tag = '-ycr-' + ($modo -replace '[^0-9A-Za-z]', '') + '-susp' + $(if ($c -ne 11) { "-c$c" })
            $ws = "$repo\runs\fase0\watch-m$m$tag-p1.json"
            $tele = if (Test-Path $ws) { Get-Content $ws -Raw | ConvertFrom-Json } else { $null }
            $historial += [pscustomobject]@{ margen = $m; modo = $modo; codigo = $code; ghz = $tele.freqMedian; volt = $tele.voltMedian; watts = $tele.powerMedian; temp = $tele.tempMedian; ts = (Get-Date).ToString('s') }
            Say ("  -> {0}   GHz {1:F3}  V {2:F4}  W {3:F2}" -f $(switch ($code) { 0 {'limpio'} 10 {'POSITIVO'} default {"guion fallo ($code)"} }), $tele.freqMedian, $tele.voltMedian, $tele.powerMedian)
            if ($code -ne 0) { $limpio = $false; break }
        }
        if ($limpio) { $limite = $m; break }
    }
    $res = [pscustomobject]@{ core = $c; limite = $limite; inicio = $Inicio; tope = $Tope; paso = $Paso; seconds = $Seconds; modos = $Modos; historial = $historial; fecha = (Get-Date).ToString('s') }
    $res | ConvertTo-Json -Depth 5 | Set-Content -Path $json -Encoding ascii
    Say "nucleo ${c}: limite $(if ($null -ne $limite) { $limite } else { 'NINGUNO hasta ' + $Tope })   ($json)"
}
Say ("Fase 1 terminada en {0:F0} min" -f ((Get-Date) - $t0).TotalMinutes)
