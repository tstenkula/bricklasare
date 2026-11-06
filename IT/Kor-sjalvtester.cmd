@echo off
setlocal
title Bricklasaren - IT
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -File "%~dp0..\Program\Starta-Bricklasaren.ps1" -FullSelfTest
set "RESULT=%ERRORLEVEL%"
echo.
if not "%RESULT%"=="0" echo Korningen misslyckades. Las felet ovan; andra inte sakerhetspolicyn.
pause
exit /b %RESULT%
