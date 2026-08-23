@echo off
chcp 65001 >nul
cd /d "%~dp0"
title SubtitleBoom v1.0 - First Public Release - Offline Build
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD.ps1"
if errorlevel 1 (
  echo.
  echo BUILD NIJE USPEO.
  echo Posalji screenshot ili ceo tekst greske.
  pause
)
