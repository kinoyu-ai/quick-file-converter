@echo off
title QuickConvert Updater
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update.ps1" -NoPause %*
echo.
pause
