@echo off
echo Capture ^& Release - one-time recording tools setup
echo Downloads a checksum-verified FFmpeg package directly from its provider.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-RecordingTools.ps1"
if errorlevel 1 (
  echo Setup failed. Please read the message above and README.md.
  pause
  exit /b 1
)
pause
