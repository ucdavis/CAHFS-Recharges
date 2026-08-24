@echo off
REM Launch helpers for Task Scheduler / manual runs on the GP host.
REM Set DOTNET_ENVIRONMENT before calling (Test or Production).

setlocal
cd /d "%~dp0"

if "%DOTNET_ENVIRONMENT%"=="" set DOTNET_ENVIRONMENT=Production

echo Environment=%DOTNET_ENVIRONMENT%
echo.

if /I "%~1"=="CAHFS" goto run
if /I "%~1"=="EQUINE" goto run
echo Usage: %~nx0 CAHFS^|EQUINE [--dry-run] [--max N]
exit /b 2

:run
LockboxCashReceiptPoster.exe --company %*
exit /b %ERRORLEVEL%
