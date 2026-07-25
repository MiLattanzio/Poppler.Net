#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Release}"
dotnet restore Poppler.Net.sln
dotnet build Poppler.Net.sln --configuration "$configuration" --no-restore
dotnet run \
  --project tests/Poppler.Net.Tests/Poppler.Net.Tests.csproj \
  --configuration "$configuration" \
  --no-build
dotnet pack \
  src/Poppler.Net/Poppler.Net.csproj \
  --configuration "$configuration" \
  --no-build \
  --output artifacts
