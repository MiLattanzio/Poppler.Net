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
            $structuredJson `
            --page 1
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
            $structuredOutput `
            --page 1
        if ($LASTEXITCODE -ne 0 -or
            -not (Test-Path -LiteralPath (Join-Path $structuredOutput "manifest.json")) -or
            -not (Test-Path -LiteralPath (Join-Path $structuredOutput "document.xml")) -or
            -not (Test-Path -LiteralPath (Join-Path $structuredOutput "document.xhtml"))) {
            throw "The packaged CLI structured bundle smoke output is invalid."
        }
        $rangeHtml = Join-Path $toolRoot "range.html"
        & $toolCommand html `
            tests/fixtures/compatibility-beta1.pdf `
            $rangeHtml `
            --first-page 2 `
            --last-page 3
        $rangeHtmlContent = Get-Content -Raw -LiteralPath $rangeHtml
        if ($LASTEXITCODE -ne 0 -or
            $rangeHtmlContent.IndexOf('id="page-1"', [StringComparison]::Ordinal) -ge 0 -or
            $rangeHtmlContent.IndexOf('id="page-2"', [StringComparison]::Ordinal) -lt 0 -or
            $rangeHtmlContent.IndexOf('id="page-3"', [StringComparison]::Ordinal) -lt 0) {
            throw "The packaged CLI HTML page-range smoke output is invalid."
        }
        $rangeJson = Join-Path $toolRoot "range.json"
        & $toolCommand json `
            tests/fixtures/compatibility-beta1.pdf `
            $rangeJson `
            --first-page 2 `
            --last-page 3 `
            --no-images
        $rangePages = @((Get-Content -Raw -LiteralPath $rangeJson | ConvertFrom-Json).pages)
        if ($LASTEXITCODE -ne 0 -or
            $rangePages.Count -ne 2 -or
            $rangePages[0].index -ne 1 -or
            $rangePages[1].index -ne 2) {
            throw "The packaged CLI structured page-range smoke output is invalid."
        }
        $limitedHtml = Join-Path $toolRoot "limited.html"
        $nativeErrorPreference = $PSNativeCommandUseErrorActionPreference
        $errorPreference = $ErrorActionPreference
        try {
            $PSNativeCommandUseErrorActionPreference = $false
            $ErrorActionPreference = "Continue"
            & $toolCommand html `
                tests/fixtures/compatibility-beta1.pdf `
                $limitedHtml `
                --first-page 1 `
                --last-page 2 `
                --max-pages 1 2>$null
            $limitedHtmlExitCode = $LASTEXITCODE
        }
        finally {
            $PSNativeCommandUseErrorActionPreference = $nativeErrorPreference
            $ErrorActionPreference = $errorPreference
        }
        if ($limitedHtmlExitCode -eq 0 -or (Test-Path -LiteralPath $limitedHtml)) {
            throw "The packaged CLI did not enforce the HTML page limit."
        }
        $limitedJson = Join-Path $toolRoot "limited.json"
        try {
            $PSNativeCommandUseErrorActionPreference = $false
            $ErrorActionPreference = "Continue"
            & $toolCommand json `
                tests/fixtures/compatibility-beta1.pdf `
                $limitedJson `
                --max-nodes 1 2>$null
            $limitedJsonExitCode = $LASTEXITCODE
        }
        finally {
            $PSNativeCommandUseErrorActionPreference = $nativeErrorPreference
            $ErrorActionPreference = $errorPreference
        }
        if ($limitedJsonExitCode -eq 0 -or (Test-Path -LiteralPath $limitedJson)) {
            throw "The packaged CLI did not enforce the structured node limit."
        }
        $limitedPages = Join-Path $toolRoot "limited-pages"
        try {
            $PSNativeCommandUseErrorActionPreference = $false
            $ErrorActionPreference = "Continue"
            & $toolCommand separate `
                tests/fixtures/compatibility-beta1.pdf `
                $limitedPages `
                --first-page 1 `
                --last-page 2 `
                --max-pages 1 2>$null
            $limitedPagesExitCode = $LASTEXITCODE
        }
        finally {
            $PSNativeCommandUseErrorActionPreference = $nativeErrorPreference
            $ErrorActionPreference = $errorPreference
        }
        if ($limitedPagesExitCode -eq 0) {
            throw "The packaged CLI did not enforce the separated-page limit."
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
