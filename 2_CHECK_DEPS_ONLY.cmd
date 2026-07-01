@echo off
setlocal
cd /d "%~dp0"
title USB Point Monitor - Check Dependencies Only
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check_deps.ps1"
echo.
echo Exit code: %errorlevel%
echo.
choice /c R /n /m "Press R to close this window..."
