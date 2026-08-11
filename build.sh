#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Release}"
repository_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$repository_root"

dotnet restore Poppler.Net.sln --configfile NuGet.Config
dotnet build Poppler.Net.sln --configuration "$configuration" --no-restore
dotnet run \
  --project eng/Poppler.Net.ManagedOnlyVerifier/Poppler.Net.ManagedOnlyVerifier.csproj \
  --configuration "$configuration" \
  --no-build \
  -- "$repository_root"
dotnet run \
  --project tests/Poppler.Net.Tests/Poppler.Net.Tests.csproj \
  --configuration "$configuration" \
  --no-build \
  -- --noresult
dotnet pack \
  src/Poppler.Net/Poppler.Net.csproj \
  --configuration "$configuration" \
  --no-build \
  --output artifacts
package_version="$(dotnet msbuild \
  src/Poppler.Net/Poppler.Net.csproj \
  -nologo \
  -getProperty:Version)"
package_path="${repository_root}/artifacts/Poppler.Net.${package_version}.nupkg"
dotnet run \
  --project eng/Poppler.Net.PackageVerifier/Poppler.Net.PackageVerifier.csproj \
  --configuration "$configuration" \
  --no-build \
  -- \
  "$package_path" \
  "$package_version"
dotnet restore \
  eng/Poppler.Net.PackageSmoke/Poppler.Net.PackageSmoke.csproj \
  --source artifacts \
  --source "https://api.nuget.org/v3/index.json" \
  -p:PopplerPackageVersion="$package_version"
dotnet run \
  --project eng/Poppler.Net.PackageSmoke/Poppler.Net.PackageSmoke.csproj \
  --configuration "$configuration" \
  --no-restore \
  -p:PopplerPackageVersion="$package_version" \
  -- \
  tests/fixtures/rendering-beta2.pdf \
  "$package_version"
