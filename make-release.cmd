@echo off
setlocal
cd /d "%~dp0"
call "%~dp0build-setup.cmd" nopause
if errorlevel 1 exit /b 1
dotnet run --project "ReleaseTools\ReleaseTools.csproj" --configuration Release -- sign
set "RESULT=%ERRORLEVEL%"
pause
exit /b %RESULT%
