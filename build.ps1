$ErrorActionPreference = "Stop"
$configuration = if ($args.Count -gt 0) { $args[0] } else { "Release" }
$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Push-Location $repositoryRoot
try {
    dotnet restore Poppler.Net.sln
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
}
finally {
    Pop-Location
}
