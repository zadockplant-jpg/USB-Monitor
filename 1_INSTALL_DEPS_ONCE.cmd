@echo off
setlocal
cd /d "%~dp0"
title USB Point Monitor - Install Dependencies ONCE
net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Requesting Administrator...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install_deps_once.ps1"
echo.
echo Exit code: %errorlevel%
echo.
choice /c R /n /m "Press R to close this window..."
