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
dotnet pack \
  src/Poppler.Net.Cli/Poppler.Net.Cli.csproj \
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
tool_package_path="${repository_root}/artifacts/Poppler.Net.Cli.${package_version}.nupkg"
dotnet run \
  --project eng/Poppler.Net.PackageVerifier/Poppler.Net.PackageVerifier.csproj \
  --configuration "$configuration" \
  --no-build \
  -- \
  "$tool_package_path" \
  "$package_version"
dotnet restore \
  eng/Poppler.Net.PackageSmoke/Poppler.Net.PackageSmoke.csproj \
  --configfile NuGet.Config \
  -p:RestoreAdditionalProjectSources="${repository_root}/artifacts" \
  -p:PopplerPackageVersion="$package_version"
for framework in net8.0 net10.0; do
  dotnet run \
    --project eng/Poppler.Net.PackageSmoke/Poppler.Net.PackageSmoke.csproj \
    --configuration "$configuration" \
    --framework "$framework" \
    --no-restore \
    -p:PopplerPackageVersion="$package_version" \
    -- \
    tests/fixtures/rendering-beta2.pdf \
    "$package_version"
done

tool_root="$(mktemp -d)"
cleanup_tool() {
  rm -rf -- "$tool_root"
}
trap cleanup_tool EXIT
dotnet tool install Poppler.Net.Cli \
  --tool-path "$tool_root" \
  --version "$package_version" \
  --add-source "${repository_root}/artifacts" \
  --ignore-failed-sources
"${tool_root}/poppler-net" version
"${tool_root}/poppler-net" html \
  tests/fixtures/truetype-format0-subset.pdf \
  "${tool_root}/tool-smoke.html"
grep -F 'class="pdf-glyph ' "${tool_root}/tool-smoke.html" >/dev/null
grep -F 'pdf-managed-' "${tool_root}/tool-smoke.html" >/dev/null
if grep -F '<text ' "${tool_root}/tool-smoke.html" >/dev/null; then
  echo "The packaged CLI HTML smoke output unexpectedly retained native text in SVG." >&2
  exit 1
fi
if grep -F 'ABCDEF+DejaVuSans' "${tool_root}/tool-smoke.html" >/dev/null; then
  echo "The packaged CLI HTML smoke output contains an unnormalized subset font name." >&2
  exit 1
fi
