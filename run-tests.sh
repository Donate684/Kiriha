#!/bin/bash
set -e

echo ""
echo "=== Running Kiriha tests ==="
dotnet test ./Tests/Kiriha.Tests/Kiriha.Tests.csproj --configuration Release

echo ""
echo "DONE: tests passed."
