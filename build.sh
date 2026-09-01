#!/bin/bash
set -e

echo ""
echo "=== [1/2] Building Kiriha ==="
dotnet build --configuration Debug

echo ""
echo "=== [2/2] Running tests ==="
dotnet test ./Tests/Kiriha.Tests/Kiriha.Tests.csproj --configuration Debug

echo ""
echo "============================================================================"
echo " DONE: Kiriha successfully built and passed all tests!"
echo " Note: On Linux, ensure you have libmpv installed (e.g. apt install libmpv-dev)"
echo "============================================================================"
