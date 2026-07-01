@echo off
setlocal
title USB Point Monitor - Kill Stuck USBPcapCMD
net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Requesting Administrator...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo Killing stuck USBPcapCMD.exe processes only...
taskkill /IM USBPcapCMD.exe /F
echo.
choice /c R /n /m "Press R to close this window..."
