@echo off
echo Building OpenRA MapReader...

:: Set dotnet SDK version
set DOTNET_ROOT=C:\Program Files\dotnet

:: Restore packages
echo Restoring packages...
dotnet restore OpenRA.MapReader\OpenRA.MapReader.csproj

:: Build the project
echo Building project...
dotnet build OpenRA.MapReader\OpenRA.MapReader.csproj -c Release

:: Check build result
if %ERRORLEVEL% NEQ 0 (
  echo Build failed with error %ERRORLEVEL%
  pause
  exit /b %ERRORLEVEL%
)

echo Build completed successfully
pause
