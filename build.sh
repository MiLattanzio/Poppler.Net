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

separate_root="${tool_root}/separated"
"${tool_root}/poppler-net" separate \
  tests/fixtures/truetype-format0-subset.pdf \
  "$separate_root"
separated_page="${separate_root}/truetype-format0-subset-page-0001.pdf"
test -f "$separated_page"
separated_info="$("${tool_root}/poppler-net" info "$separated_page")"
grep -F 'Pages:              1' <<<"$separated_info" >/dev/null
grep -F 'Encrypted:          no' <<<"$separated_info" >/dev/null
"${tool_root}/poppler-net" separate \
  tests/fixtures/truetype-format0-subset.pdf \
  "$separate_root"
test -f "${separate_root}/truetype-format0-subset-page-0001-2.pdf"

structured_json="${tool_root}/structured.json"
"${tool_root}/poppler-net" json \
  tests/fixtures/truetype-format0-subset.pdf \
  "$structured_json" \
  --page 1
grep -F '"schemaVersion": "1.0"' "$structured_json" >/dev/null
grep -F '"name": "DejaVuSans"' "$structured_json" >/dev/null
grep -F '"rawName": "ABCDEF+DejaVuSans"' "$structured_json" >/dev/null

structured_root="${tool_root}/structured-bundle"
"${tool_root}/poppler-net" export \
  tests/fixtures/images-and-color.pdf \
  "$structured_root" \
  --page 1
test -f "${structured_root}/manifest.json"
test -f "${structured_root}/document.xml"
test -f "${structured_root}/document.xhtml"

range_html="${tool_root}/range.html"
"${tool_root}/poppler-net" html \
  tests/fixtures/compatibility-beta1.pdf \
  "$range_html" \
  --first-page 2 \
  --last-page 3
if grep -F 'id="page-1"' "$range_html" >/dev/null; then
  echo "The packaged CLI HTML page range included page 1." >&2
  exit 1
fi
grep -F 'id="page-2"' "$range_html" >/dev/null
grep -F 'id="page-3"' "$range_html" >/dev/null

range_json="${tool_root}/range.json"
"${tool_root}/poppler-net" json \
  tests/fixtures/compatibility-beta1.pdf \
  "$range_json" \
  --first-page 2 \
  --last-page 3 \
  --no-images
grep -F '"index": 1' "$range_json" >/dev/null
grep -F '"index": 2' "$range_json" >/dev/null

if "${tool_root}/poppler-net" html \
  tests/fixtures/compatibility-beta1.pdf \
  "${tool_root}/limited.html" \
  --first-page 1 \
  --last-page 2 \
  --max-pages 1 2>/dev/null; then
  echo "The packaged CLI did not enforce the HTML page limit." >&2
  exit 1
fi
if "${tool_root}/poppler-net" json \
  tests/fixtures/compatibility-beta1.pdf \
  "${tool_root}/limited.json" \
  --max-nodes 1 2>/dev/null; then
  echo "The packaged CLI did not enforce the structured node limit." >&2
  exit 1
fi
if "${tool_root}/poppler-net" separate \
  tests/fixtures/compatibility-beta1.pdf \
  "${tool_root}/limited-pages" \
  --first-page 1 \
  --last-page 2 \
  --max-pages 1 2>/dev/null; then
  echo "The packaged CLI did not enforce the separated-page limit." >&2
  exit 1
fi
