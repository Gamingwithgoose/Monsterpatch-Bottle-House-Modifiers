param([string]$SharedLibDir, [string]$GameDir)
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build.bat") $SharedLibDir $GameDir
exit $LASTEXITCODE
