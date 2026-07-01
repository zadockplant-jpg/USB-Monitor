@echo off
setlocal
cd /d "%~dp0"
title USB Point Monitor - Build EXE
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build_exe.ps1"
echo.
echo Exit code: %errorlevel%
echo.
choice /c R /n /m "Press R to close this window..."
