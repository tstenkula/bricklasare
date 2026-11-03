@echo off
setlocal
title Bricklasaren - stang via programfonstret
rem Use installed Windows PowerShell. Do not override security policies.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -STA -WindowStyle Minimized -File "%~dp0Program\Starta-Bricklasaren.ps1"
if errorlevel 1 (
    echo.
    echo Bricklasaren kunde inte starta. Kontakta IT.
    echo Vid sakerhetsblockering: anvand organisationens ordinarie godkannande.
    echo IT\Starta-med-felsokning.cmd visar eventuella startfel i ett synligt fonster.
    pause
    exit /b 1
)
endlocal
exit /b 0
