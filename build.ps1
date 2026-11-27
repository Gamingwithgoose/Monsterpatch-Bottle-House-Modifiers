param([string]$SharedLibDir, [string]$GameDir)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$log = Join-Path $PSScriptRoot 'build.log'
Set-Content -LiteralPath $log -Value ('Monsterpatch Bottle House Modifiers v0.2.1 - ' + (Get-Date))
function Report([string]$Message) {
    Write-Host $Message
    Add-Content -LiteralPath $log -Value $Message
}
try {
    if (-not $SharedLibDir) { $SharedLibDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'lib' }
    if (-not $GameDir) { $GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Monsterpatch' }
    $SharedLibDir = [IO.Path]::GetFullPath($SharedLibDir)
    $GameDir = [IO.Path]::GetFullPath($GameDir)
    Report "Shared lib: $SharedLibDir"
    Report "Game directory: $GameDir"
    $dotnet = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
    if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
    if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Install the .NET SDK first.' }
    $project = Join-Path $PSScriptRoot 'src\Monsterpatch_CatchBottleRates_Patcher.csproj'
    $release = Join-Path $PSScriptRoot 'Release\BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll'
    if (Test-Path -LiteralPath $release) { Remove-Item -LiteralPath $release -Force }
    Report 'Building patcher...'
    $ErrorActionPreference = 'Continue'
    & $dotnet build $project -c Release --nologo "-p:SharedLibDir=$SharedLibDir" "-p:GameDir=$GameDir" 2>&1 | Tee-Object -FilePath $log -Append | ForEach-Object { Write-Host $_ }
    $result = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($result -ne 0) { throw "dotnet build failed with exit code $result." }
    if (-not (Test-Path -LiteralPath $release)) { throw 'Expected release DLL missing.' }
    Report "SUCCESS: $release"
    Report 'Copy Release\BepInEx into the Monsterpatch game folder.'
    exit 0
} catch {
    Report ("ERROR: " + $_.ToString())
    exit 1
}
