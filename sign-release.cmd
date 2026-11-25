@echo off
setlocal
cd /d "%~dp0"
dotnet run --project "ReleaseTools\ReleaseTools.csproj" --configuration Release -- sign
set "RESULT=%ERRORLEVEL%"
pause
exit /b %RESULT%
