@echo off
title QuickConvert Installer
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-native.ps1" -NoPause %*
echo.
pause
