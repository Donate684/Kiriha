@echo off
setlocal

echo.
echo === [1/4] Downloading/Updating libmpv ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\download-mpv.ps1"
if errorlevel 1 (
    echo ERROR: libmpv download/update failed.
    pause
    exit /b 1
)

echo.
echo === [2/4] Downloading/Updating TorrServer ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\download-torrserver.ps1"
if errorlevel 1 (
    echo ERROR: TorrServer download/update failed.
    pause
    exit /b 1
)

echo.
echo === [3/4] Building Kiriha ===
dotnet build --configuration Debug
if errorlevel 1 (
    echo ERROR: build failed.
    pause
    exit /b 1
)

echo.
echo === [4/4] Running tests ===
dotnet test .\tests\Kiriha.Tests\Kiriha.Tests.csproj --configuration Debug
if errorlevel 1 (
    echo ERROR: tests failed.
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo  DONE: Kiriha successfully downloaded, built, and passed all tests!
echo ============================================================================
pause
exit /b 0
