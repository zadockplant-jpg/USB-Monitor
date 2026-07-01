@echo off
setlocal
cd /d "%~dp0"
title USB Point Monitor - Menu
:menu
cls
echo USB Point Monitor - Stable v2
echo.
echo 1  Install/repair deps ONCE        ^(Wireshark + USBPcap + extcap dedupe^)
echo 2  Check deps only                 ^(no installs, copies log to clipboard^)
echo 3  Run monitor                     ^(no installs; builds EXE if missing^)
echo 4  Build EXE only                  ^(no installs^)
echo 5  Repair extcap duplicates only   ^(fixes old package duplicate USBPcapCMD copies^)
echo 9  Kill stuck USBPcapCMD only
echo Q  Quit
echo.
choice /c 123459Q /n /m "Press a key: "
set choice=%errorlevel%
if "%choice%"=="1" call "%~dp01_INSTALL_DEPS_ONCE.cmd"
if "%choice%"=="2" call "%~dp02_CHECK_DEPS_ONLY.cmd"
if "%choice%"=="3" call "%~dp03_RUN_MONITOR.cmd"
if "%choice%"=="4" call "%~dp04_BUILD_EXE.cmd"
if "%choice%"=="5" call "%~dp05_REPAIR_EXTCAP_DUPES_ONLY.cmd"
if "%choice%"=="6" call "%~dp09_KILL_STUCK_USBPCAP.cmd"
if "%choice%"=="7" exit /b 0
goto menu
