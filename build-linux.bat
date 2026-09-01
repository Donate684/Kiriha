@echo off
setlocal

echo.
echo === Building Kiriha for Linux (linux-x64) ===
dotnet publish .\src\Kiriha\Kiriha.csproj -c Release -f net10.0 -r linux-x64 --self-contained true -p:PublishSingleFile=true -o .\publish_linux

if errorlevel 1 (
    echo.
    echo ERROR: Linux build failed.
    pause
    exit /b 1
)

echo.
echo ============================================================================
echo DONE: Linux build is ready in the 'publish_linux' folder!
echo.
echo To run it on Linux:
echo 1. Copy the 'publish_linux' folder to your Linux machine.
echo 2. Open a terminal in that folder.
echo 3. Run: chmod +x Kiriha
echo 4. Run: ./Kiriha
echo.
echo Note: Make sure libmpv is installed on the target Linux system
echo       (e.g., sudo apt install libmpv-dev)
echo ============================================================================
pause
exit /b 0
