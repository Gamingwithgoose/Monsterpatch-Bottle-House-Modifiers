@echo off
setlocal

REM Shared-lib build for the normal Monsterpatch_Mods layout:
REM   C:\Monsterpatch_Mods\lib
REM   C:\Monsterpatch_Mods\Monsterpatch_CatchBottleRates_Patcher_Source_v0.1.0_Final
REM
REM Usage from inside this source folder:
REM   build.bat
REM
REM Optional override:
REM   build.bat "C:\Monsterpatch_Mods\lib"
REM
REM Game-folder fallback, if you do not use shared lib:
REM   dotnet build src\Monsterpatch_CatchBottleRates_Patcher.csproj -c Release -p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Monsterpatch"

set MOD_DIR=%~dp0
for %%I in ("%MOD_DIR%..") do set MODS_ROOT=%%~fI

set SHARED_LIB_DIR=%~1
if "%SHARED_LIB_DIR%"=="" set SHARED_LIB_DIR=%MODS_ROOT%\lib

echo Monsterpatch Catch Bottle Rates PRELOADER PATCHER build
echo Version: v0.1.0 final
echo Mods root: %MODS_ROOT%
echo Shared lib: %SHARED_LIB_DIR%
echo.

dotnet build "%MOD_DIR%src\Monsterpatch_CatchBottleRates_Patcher.csproj" -c Release -p:SharedLibDir="%SHARED_LIB_DIR%"

if errorlevel 1 (
  echo.
  echo Build failed.
  exit /b 1
)

echo.
echo Build complete.
echo Output copied to:
echo   %MOD_DIR%dist\BepInEx\patchers\Monsterpatch_CatchBottleRates_Patcher.dll
endlocal
