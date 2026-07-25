$ErrorActionPreference = "Stop"
$configuration = if ($args.Count -gt 0) { $args[0] } else { "Release" }

dotnet restore Poppler.Net.sln
dotnet build Poppler.Net.sln --configuration $configuration --no-restore
dotnet run `
    --project tests/Poppler.Net.Tests/Poppler.Net.Tests.csproj `
    --configuration $configuration `
    --no-build
dotnet pack `
    src/Poppler.Net/Poppler.Net.csproj `
    --configuration $configuration `
    --no-build `
    --output artifacts
