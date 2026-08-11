$ErrorActionPreference = "Stop"
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
}
finally {
    Pop-Location
}
