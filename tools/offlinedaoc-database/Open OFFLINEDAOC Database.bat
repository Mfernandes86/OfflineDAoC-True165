@echo off
rem Opens the OFFLINEDAOC Database in the default browser. Nothing is installed and nothing goes online.
if not exist "%~dp0index.html" (
  echo index.html is not next to this file. Keep this .bat inside the "OFFLINEDAOC Database" folder.
  pause
  exit /b 1
)
start "" "%~dp0index.html"
