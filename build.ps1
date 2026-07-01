param(
  [string]$SharedLibDir
)

$ErrorActionPreference = "Stop"
$ModDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ModsRoot = Split-Path -Parent $ModDir
if ([string]::IsNullOrWhiteSpace($SharedLibDir)) {
  $SharedLibDir = Join-Path $ModsRoot "lib"
}

Write-Host "Monsterpatch Catch Bottle Rates PRELOADER PATCHER build"
Write-Host "Version: v0.1.0 final"
Write-Host "Mods root: $ModsRoot"
Write-Host "Shared lib: $SharedLibDir"
Write-Host ""

dotnet build (Join-Path $ModDir "src\Monsterpatch_CatchBottleRates_Patcher.csproj") -c Release -p:SharedLibDir="$SharedLibDir"

Write-Host ""
Write-Host "Build complete."
Write-Host "Output copied to:"
Write-Host "  $ModDir\dist\BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll"
