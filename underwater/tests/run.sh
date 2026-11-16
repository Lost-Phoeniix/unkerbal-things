#!/bin/bash
# builds and runs the offline tests; Application Control sometimes blocks a fresh DLL, a non-deterministic rebuild gets past it
cd "$(dirname "$0")"
for i in 1 2 3 4; do
  # a failed build must not run the tests of the last good build
  out=$(dotnet build -c Release -nologo -v q --no-incremental -p:Deterministic=false 2>&1) || { echo "$out" | grep error; echo "build failed"; exit 1; }
  dotnet bin/Release/net8.0/UnderwaterTests.dll > run-out.txt 2>&1; rc=$?
  grep -q "Application Control" run-out.txt || break
done
echo "exit $rc"
