$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true
$configuration = if ($args.Count -gt 0) { $args[0] } else { "Release" }
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $repositoryRoot
try {
    dotnet restore Poppler.Net.sln --configfile NuGet.Config
    dotnet build Poppler.Net.sln --configuration $configuration --no-restore
    dotnet run `
        --project eng/Poppler.Net.ManagedOnlyVerifier/Poppler.Net.ManagedOnlyVerifier.csproj `
        --configuration $configuration `
        --no-build `
        -- $repositoryRoot
    dotnet run `
        --project tests/Poppler.Net.Tests/Poppler.Net.Tests.csproj `
        --configuration $configuration `
        --no-build `
        -- --noresult
    dotnet pack `
        src/Poppler.Net/Poppler.Net.csproj `
        --configuration $configuration `
        --no-build `
        --output artifacts
    dotnet pack `
        src/Poppler.Net.Cli/Poppler.Net.Cli.csproj `
        --configuration $configuration `
        --no-build `
        --output artifacts
    $packageVersion = dotnet msbuild `
        src/Poppler.Net/Poppler.Net.csproj `
        -nologo `
        -getProperty:Version
    $packagePath = Join-Path `
        $repositoryRoot `
        "artifacts/Poppler.Net.$packageVersion.nupkg"
    dotnet run `
        --project eng/Poppler.Net.PackageVerifier/Poppler.Net.PackageVerifier.csproj `
        --configuration $configuration `
        --no-build `
        -- `
        $packagePath `
        $packageVersion
    $toolPackagePath = Join-Path `
        $repositoryRoot `
        "artifacts/Poppler.Net.Cli.$packageVersion.nupkg"
    dotnet run `
        --project eng/Poppler.Net.PackageVerifier/Poppler.Net.PackageVerifier.csproj `
        --configuration $configuration `
        --no-build `
        -- `
        $toolPackagePath `
        $packageVersion
    dotnet restore `
        eng/Poppler.Net.PackageSmoke/Poppler.Net.PackageSmoke.csproj `
        --configfile NuGet.Config `
        "-p:RestoreAdditionalProjectSources=$(Join-Path $repositoryRoot 'artifacts')" `
        "-p:PopplerPackageVersion=$packageVersion"
    foreach ($framework in @("net8.0", "net10.0")) {
        dotnet run `
            --project eng/Poppler.Net.PackageSmoke/Poppler.Net.PackageSmoke.csproj `
            --configuration $configuration `
            --framework $framework `
            --no-restore `
            "-p:PopplerPackageVersion=$packageVersion" `
            -- `
            tests/fixtures/rendering-beta2.pdf `
            $packageVersion
    }
    $toolRoot = Join-Path `
        ([IO.Path]::GetTempPath()) `
        "poppler-net-tool-$([Guid]::NewGuid().ToString('N'))"
    try {
        dotnet tool install Poppler.Net.Cli `
            --tool-path $toolRoot `
            --version $packageVersion `
            --add-source (Join-Path $repositoryRoot "artifacts") `
            --ignore-failed-sources
        $toolCommand = Join-Path $toolRoot "poppler-net.exe"
        & $toolCommand version
        $htmlOutput = Join-Path $toolRoot "tool-smoke.html"
        & $toolCommand html `
            tests/fixtures/truetype-format0-subset.pdf `
            $htmlOutput
        $html = Get-Content -Raw -LiteralPath $htmlOutput
        if ($html.IndexOf('class="pdf-glyph ', [StringComparison]::Ordinal) -lt 0 -or
            $html.IndexOf('pdf-managed-', [StringComparison]::Ordinal) -lt 0 -or
            $html.IndexOf('<text ', [StringComparison]::Ordinal) -ge 0 -or
            $html.IndexOf('ABCDEF+DejaVuSans', [StringComparison]::Ordinal) -ge 0) {
            throw "The packaged CLI HTML smoke output is invalid."
        }
        $separateOutput = Join-Path $toolRoot "separated"
        & $toolCommand separate `
            tests/fixtures/truetype-format0-subset.pdf `
            $separateOutput
        $separatedPage = Join-Path `
            $separateOutput `
            "truetype-format0-subset-page-0001.pdf"
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $separatedPage)) {
            throw "The packaged CLI did not create standalone PDF output."
        }
        $separatedInfo = (& $toolCommand info $separatedPage | Out-String)
        if ($LASTEXITCODE -ne 0 -or
            $separatedInfo.IndexOf("Pages:              1", [StringComparison]::Ordinal) -lt 0 -or
            $separatedInfo.IndexOf("Encrypted:          no", [StringComparison]::Ordinal) -lt 0) {
            throw "The packaged CLI standalone PDF smoke output is invalid."
        }
        & $toolCommand separate `
            tests/fixtures/truetype-format0-subset.pdf `
            $separateOutput
        $collisionPage = Join-Path `
            $separateOutput `
            "truetype-format0-subset-page-0001-2.pdf"
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $collisionPage)) {
            throw "The packaged CLI did not choose a collision-free PDF name."
        }
        $structuredJson = Join-Path $toolRoot "structured.json"
        & $toolCommand json `
            tests/fixtures/truetype-format0-subset.pdf `
            $structuredJson
        $structured = Get-Content -Raw -LiteralPath $structuredJson
        if ($LASTEXITCODE -ne 0 -or
            $structured.IndexOf('"schemaVersion": "1.0"', [StringComparison]::Ordinal) -lt 0 -or
            $structured.IndexOf('"name": "DejaVuSans"', [StringComparison]::Ordinal) -lt 0 -or
            $structured.IndexOf('"rawName": "ABCDEF+DejaVuSans"', [StringComparison]::Ordinal) -lt 0) {
            throw "The packaged CLI structured JSON smoke output is invalid."
        }
        $structuredOutput = Join-Path $toolRoot "structured-bundle"
        & $toolCommand export `
            tests/fixtures/images-and-color.pdf `
            $structuredOutput
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $structuredOutput "manifest.json")) -or
            -not (Test-Path -LiteralPath (Join-Path $structuredOutput "document.xml")) -or
            -not (Test-Path -LiteralPath (Join-Path $structuredOutput "document.xhtml"))) {
            throw "The packaged CLI structured bundle smoke output is invalid."
        }
    }
    finally {
        if (Test-Path -LiteralPath $toolRoot) {
            Remove-Item -LiteralPath $toolRoot -Recurse -Force
        }
    }
}
finally {
    Pop-Location
}
