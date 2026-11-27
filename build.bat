@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set "RESULT=%ERRORLEVEL%"
echo.
if "%RESULT%"=="0" (echo BUILD SUCCEEDED) else (echo BUILD FAILED - exit code %RESULT%)
echo Build log: "%~dp0build.log"
pause
exit /b %RESULT%
