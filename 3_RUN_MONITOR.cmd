@echo off
setlocal
cd /d "%~dp0"
title USB Point Monitor - Run Monitor
net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Requesting Administrator for USBPcap capture...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
if not exist "%~dp0dist\USBPointMonitor.exe" (
  echo USBPointMonitor.exe not found. Building now. This does NOT install dependencies.
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build_exe.ps1"
  if not exist "%~dp0dist\USBPointMonitor.exe" (
    echo.
    echo Build failed; cannot run monitor. Check build log on Desktop\usb_point_monitor_logs\build_logs.
    echo.
    choice /c R /n /m "Press R to close this window..."
    exit /b 1
  )
)
echo Launching monitor UI...
start "" "%~dp0dist\USBPointMonitor.exe"
echo.
echo Monitor launched. This window can be closed.
echo.
choice /c R /n /m "Press R to close this window..."
