param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$postgresFixture = $null
$originalPostgres = $env:NYSSAAI_REPOSITORY_POSTGRES
. (Join-Path $PSScriptRoot 'test/PostgresFixture.ps1')
Push-Location $PSScriptRoot
try {
    dotnet build NyssaAI.Shared.slnx --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $postgresFixture = Start-RepositoryPostgres -ScratchRoot (Join-Path $PSScriptRoot '.temp')
    $env:NYSSAAI_REPOSITORY_POSTGRES = $postgresFixture.ConnectionString

    $testProjects = @(
        'NyssaAI.Result/test/NyssaAI.Result.Tests/NyssaAI.Result.Tests.csproj',
        'test/NyssaAI.Result.UseCaseTests/NyssaAI.Result.UseCaseTests.csproj',
        'NyssaAI.Configuration/test/NyssaAI.Configuration.Tests/NyssaAI.Configuration.Tests.csproj',
        'test/NyssaAI.Configuration.UseCaseTests/NyssaAI.Configuration.UseCaseTests.csproj',
        'NyssaAI.Logging/test/NyssaAI.Logging.Tests/NyssaAI.Logging.Tests.csproj',
        'test/NyssaAI.Logging.UseCaseTests/NyssaAI.Logging.UseCaseTests.csproj',
        'NyssaAI.Repository/test/NyssaAI.Repository.Tests/NyssaAI.Repository.Tests.csproj',
        'test/NyssaAI.Repository.UseCaseTests/NyssaAI.Repository.UseCaseTests.csproj'
    )
    foreach ($testProject in $testProjects) {
        $projectDirectory = Split-Path -Parent $testProject
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($testProject)
        $outputDirectory = Join-Path $projectDirectory "bin/$Configuration/net10.0"
        # Run each suite from its own output folder. Configuration defaults also use this
        # folder through AppContext.BaseDirectory.
        Push-Location $outputDirectory
        try {
            $runnerArguments = @('-trait-', 'Category=LiveService')
            if ($testProject -notlike '*Configuration*' -and $testProject -notlike '*Logging.Tests*') {
                $runnerArguments += '-failSkips'
            }
            dotnet "$assemblyName.dll" @runnerArguments
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        finally {
            Pop-Location
        }
    }
}
finally {
    Stop-RepositoryPostgres $postgresFixture
    $env:NYSSAAI_REPOSITORY_POSTGRES = $originalPostgres
    Pop-Location
}
