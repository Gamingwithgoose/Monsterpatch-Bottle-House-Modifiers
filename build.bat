@echo off
setlocal
cd /d "%~dp0"
set "LIB=%~1"
if not defined LIB set "LIB=%~dp0..\lib"
if not "%~2"=="" set "GAME=%~2"
if not defined GAME set "GAME=C:\Program Files (x86)\Steam\steamapps\common\Monsterpatch"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: Install the .NET SDK first.
  exit /b 1
)
echo Building Monsterpatch Bottle House Modifiers v0.2.1...
dotnet build "src\Monsterpatch_CatchBottleRates_Patcher.csproj" -c Release -p:SharedLibDir="%LIB%" -p:GameDir="%GAME%"
if errorlevel 1 exit /b 1
if not exist "Release\BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll" (
  echo ERROR: Expected release DLL missing.
  exit /b 1
)
echo.
echo SUCCESS: Drag Release\BepInEx into your Monsterpatch game folder.
echo %~dp0Release\BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll
exit /b 0
