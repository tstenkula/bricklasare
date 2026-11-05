@echo off
setlocal
title Bricklasaren - anslutningskontroll
rem Read-only reader preflight. Do not override security policies.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -File "%~dp0..\Program\Starta-Bricklasaren.ps1" -ConnectionReport
set "RESULT=%ERRORLEVEL%"
echo.
if not "%RESULT%"=="0" echo Kontrollen kunde inte koras. Kontakta IT och andra inte sakerhetspolicyn.
pause
exit /b %RESULT%
