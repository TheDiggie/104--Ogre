@echo off
REM Applies the net8-core branch if needed, then runs the checks and the live probe.
REM Usage:  run-m59-probe.bat "C:\path\to\104--Ogre"

setlocal
set REPO=%~1
if "%REPO%"=="" set REPO=%USERPROFILE%\OneDrive\Documents\GitHub\104--Ogre
set BUNDLE=%~dp0m59-net8-core.bundle

if not exist "%REPO%\Meridian59.sln" (
  echo Could not find the repo at: %REPO%
  exit /b 1
)
where dotnet >nul 2>&1 || ( echo .NET 8 SDK not found & exit /b 1 )
cd /d "%REPO%" || exit /b 1

for /f "delims=" %%B in ('git rev-parse --abbrev-ref HEAD') do set CUR=%%B
if /i "%CUR%"=="net8-core" (
  echo == already on net8-core, skipping fetch
) else (
  git show-ref --verify --quiet refs/heads/net8-core
  if errorlevel 1 (
    if not exist "%BUNDLE%" ( echo Missing %BUNDLE% & exit /b 1 )
    echo == fetching net8-core
    git fetch "%BUNDLE%" net8-core:net8-core || exit /b 1
  )
  git checkout net8-core || exit /b 1
)

echo.
echo == offline checks
dotnet run --project Tools\Meridian59.Net8Verify -- resource
if errorlevel 1 echo (offline checks reported a failure)

echo.
echo == live probe against Server 104
dotnet run --project Tools\Meridian59.Net8Live -- 3.141.65.36 5959

echo.
echo To also try a login, set M59USER and M59PASS first, then re-run.
endlocal
