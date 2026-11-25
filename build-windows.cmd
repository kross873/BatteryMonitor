@echo off
setlocal
cd /d "%~dp0"
where dotnet.exe >nul 2>&1
if errorlevel 1 (
  echo .NET 8 SDK is required on the BUILD computer only.
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
echo Building Battery Monitor 0.11.0 for Windows x64...
dotnet publish "BatteryMonitor.csproj" -c Release -r win-x64 --self-contained true --disable-build-servers -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o "%~dp0publish"
if errorlevel 1 (
  echo BUILD FAILED. Read the error above.
  pause
  exit /b 1
)
if not exist "%~dp0publish\BatteryMonitor.exe" (
  echo BUILD FAILED: executable missing.
  pause
  exit /b 1
)
echo SUCCESS: publish\BatteryMonitor.exe
 echo Keep the ENTIRE publish folder when sharing the portable build.
if /i "%~1"=="nopause" exit /b 0
pause
exit /b 0
