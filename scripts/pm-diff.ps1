# ============================================================================
# Compara la tabla de potencia del SMU (floats crudos de ZenStates) entre dos
# margenes, a partir de los watch-m<M>-p<i>.jsonl de diag-margin.ps1.
#
# Para cada posicion de la tabla: mediana en A, mediana en B, diferencia.
# Lista las posiciones que mas cambian. La tension de nucleo debe aparecer
# como una posicion que baja ~0,003-0,005 V por cuenta de margen.
# ============================================================================
param(
    [int]$A = -5,
    [int]$B = -25,
    [int]$Top = 25,
    [int]$MinElapsed = 30      # descarta el arranque de la pasada
)

$runs = "$env:USERPROFILE\Proyectos\legion-co-lab\runs\fase0"

function Load([int]$m) {
    $rows = @()
    foreach ($f in Get-ChildItem "$runs\watch-m$m-p*.jsonl" -ErrorAction Stop) {
        foreach ($l in Get-Content $f) {
            if (-not $l.Trim()) { continue }
            $o = $l | ConvertFrom-Json
            if ($o.elapsed -ge $MinElapsed -and $o.pmTable) { $rows += ,$o.pmTable }
        }
    }
    if ($rows.Count -eq 0) { throw "sin muestras con tabla PM para margen $m" }
    $rows
}
function Median($xs) {
    $s = @($xs | Sort-Object)
    $s[[int][math]::Round(0.5 * ($s.Count - 1))]
}

$ra = Load $A
$rb = Load $B
$len = [math]::Min($ra[0].Count, $rb[0].Count)
"Margen $A : $($ra.Count) muestras   Margen $B : $($rb.Count) muestras   tabla de $len floats"
""

$out = for ($k = 0; $k -lt $len; $k++) {
    $ma = Median ($ra | ForEach-Object { $_[$k] })
    $mb = Median ($rb | ForEach-Object { $_[$k] })
    $d  = $mb - $ma
    $rel = if ($ma -ne 0) { [math]::Abs($d / $ma) } else { if ($mb -ne 0) { 1 } else { 0 } }
    [pscustomobject]@{ idx = $k; A = $ma; B = $mb; diff = $d; rel = $rel }
}

"Posiciones con valores entre 0,5 y 1,6 (candidatas a tension) que cambian:"
$out | Where-Object { $_.A -ge 0.5 -and $_.A -le 1.6 -and [math]::Abs($_.diff) -ge 0.005 } |
    Sort-Object { [math]::Abs($_.diff) } -Descending |
    Format-Table idx, @{n='A';e={'{0:F4}' -f $_.A}}, @{n='B';e={'{0:F4}' -f $_.B}}, @{n='diff';e={'{0:+0.0000;-0.0000}' -f $_.diff}} -AutoSize

"Las $Top posiciones que mas cambian en terminos relativos:"
$out | Where-Object { $_.rel -gt 0.02 } | Sort-Object rel -Descending | Select-Object -First $Top |
    Format-Table idx, @{n='A';e={'{0:F4}' -f $_.A}}, @{n='B';e={'{0:F4}' -f $_.B}}, @{n='diff';e={'{0:+0.0000;-0.0000}' -f $_.diff}}, @{n='rel';e={'{0:P1}' -f $_.rel}} -AutoSize
