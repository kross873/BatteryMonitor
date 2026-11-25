@echo off
setlocal
cd /d "%~dp0"
set "ISCC="
if /i not "%~1"=="nopause" if not "%~1"=="" if exist "%~1" set "ISCC=%~1"
if not defined ISCC for /f "delims=" %%I in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%I"
if not defined ISCC for %%D in ("%ProgramFiles%\Inno Setup 7" "%ProgramFiles(x86)%\Inno Setup 7" "%ProgramFiles%\Inno Setup 6" "%ProgramFiles(x86)%\Inno Setup 6" "%LOCALAPPDATA%\Programs\Inno Setup 7" "%LOCALAPPDATA%\Programs\Inno Setup 6") do if exist "%%~D\ISCC.exe" set "ISCC=%%~D\ISCC.exe"
if not defined ISCC (
  echo Inno Setup compiler not found. Install Inno Setup 6 or 7 from:
  echo https://jrsoftware.org/isdl.php
  echo Or drag ISCC.exe onto this CMD file.
  pause
  exit /b 1
)
call "%~dp0build-windows.cmd" nopause
if errorlevel 1 exit /b 1
"%ISCC%" "%~dp0BatteryMonitor.iss"
if errorlevel 1 (
  echo INSTALLER BUILD FAILED. Read the error above.
  pause
  exit /b 1
)
echo SUCCESS: release\BatteryMonitor-0.11.2-Setup.exe
if /i "%~1"=="nopause" exit /b 0
pause
exit /b 0
