# Build rycolab and install it (elevating if needed). Run from anywhere:
#   .\install.ps1              build Release, then `rycolab install` from the build
#   .\install.ps1 -NoBuild     install what is already built
# Extra arguments go to `rycolab install` (e.g. -Args "--ycruncher C:\y-cruncher").
param(
    [switch]$NoBuild,
    [string]$Args = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "src\Rycolab.Cli\Rycolab.Cli.csproj"
# The output folder is named after the TargetFramework in Directory.Build.props; read it instead of hard-coding it.
$tfm = ([xml](Get-Content (Join-Path $root "Directory.Build.props"))).Project.PropertyGroup.TargetFramework
$exe = Join-Path $root "src\Rycolab.Cli\bin\Release\$tfm\win-x64\rycolab.exe"

if (-not $NoBuild) {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error "dotnet not found. Install a .NET SDK 9 or newer: https://dotnet.microsoft.com/download"
    }
    Write-Host "Building $proj ..."
    dotnet build -c Release $proj
    if ($LASTEXITCODE -ne 0) { Write-Error "build failed" }
}
if (-not (Test-Path $exe)) { Write-Error "no build at $exe" }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($isAdmin) {
    & $exe install $Args.Split(" ", [StringSplitOptions]::RemoveEmptyEntries)
    exit $LASTEXITCODE
}

Write-Host "Elevating to run: rycolab install $Args"
$cmd = "& `"$exe`" install $Args; Write-Host ''; Write-Host 'Press Enter to close'; Read-Host | Out-Null"
$p = Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $cmd -Verb RunAs -Wait -PassThru
exit $p.ExitCode
