@echo off
setlocal
cd /d "%~dp0"
dotnet run --project "ReleaseTools\ReleaseTools.csproj" --configuration Release -- self-test
if errorlevel 1 (
  echo RELEASE BLOCKED: self-tests failed.
  pause
  exit /b 1
)
call "%~dp0build-setup.cmd" nopause
if errorlevel 1 exit /b 1
dotnet run --project "ReleaseTools\ReleaseTools.csproj" --configuration Release -- sign
set "RESULT=%ERRORLEVEL%"
pause
exit /b %RESULT%
