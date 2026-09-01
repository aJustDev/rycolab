# Uninstall rycolab (elevating if needed): stops the guard, removes the task,
# the PATH entry and the binaries. Data stays unless -Purge.
#   .\uninstall.ps1
#   .\uninstall.ps1 -Purge     also delete %LOCALAPPDATA%\rycolab (profile, campaigns, y-cruncher)
param(
    [switch]$Purge
)

$ErrorActionPreference = "Stop"
$home_ = if ($env:RYCOLAB_HOME) { $env:RYCOLAB_HOME } else { Join-Path $env:LOCALAPPDATA "rycolab" }
$installed = Join-Path $home_ "bin\rycolab.exe"
$tfm = ([xml](Get-Content (Join-Path $PSScriptRoot "Directory.Build.props"))).Project.PropertyGroup.TargetFramework
$built = Join-Path $PSScriptRoot "src\Rycolab.Cli\bin\Release\$tfm\win-x64\rycolab.exe"
# Prefer the build: an exe cannot delete the folder it runs from, so the installed
# copy leaves bin\ behind and this script removes it afterwards.
$exe = if (Test-Path $built) { $built } elseif (Test-Path $installed) { $installed } else { Write-Error "rycolab is not installed and there is no build at $built" }
$opts = if ($Purge) { "--purge" } else { "" }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    if ($opts) { & $exe uninstall $opts } else { & $exe uninstall }
    $code = $LASTEXITCODE
} else {
    Write-Host "Elevating to run: rycolab uninstall $opts"
    $cmd = "& `"$exe`" uninstall $opts; Write-Host ''; Write-Host 'Press Enter to close'; Read-Host | Out-Null"
    $p = Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $cmd -Verb RunAs -Wait -PassThru
    $code = $p.ExitCode
}

if ($code -eq 0) {
    $leftover = if ($Purge) { $home_ } else { Join-Path $home_ "bin" }
    if (Test-Path $leftover) {
        Remove-Item -Recurse -Force $leftover
        Write-Host "removed $leftover"
    }
}
exit $code
