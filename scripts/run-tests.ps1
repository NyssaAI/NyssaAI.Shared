param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$postgresFixture = $null
$originalPostgres = $env:NYSSAAI_REPOSITORY_POSTGRES
. (Join-Path $PSScriptRoot 'PostgresFixture.ps1')
Push-Location $repoRoot
try {
    dotnet build NyssaAI.Shared.slnx --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $postgresFixture = Start-RepositoryPostgres -ScratchRoot (Join-Path $repoRoot '.temp')
    $env:NYSSAAI_REPOSITORY_POSTGRES = $postgresFixture.ConnectionString

    $testProjects = @(
        'nyssa-ai-result-cs/test/unit/NyssaAI.Result.Tests.csproj',
        'nyssa-ai-result-cs/test/use_case/NyssaAI.Result.UseCaseTests.csproj',
        'nyssa-ai-configuration-cs/test/unit/NyssaAI.Configuration.Tests.csproj',
        'nyssa-ai-configuration-cs/test/use_case/NyssaAI.Configuration.UseCaseTests.csproj',
        'nyssa-ai-logging-cs/test/unit/NyssaAI.Logging.Tests.csproj',
        'nyssa-ai-logging-cs/test/use_case/NyssaAI.Logging.UseCaseTests.csproj',
        'nyssa-ai-dbgateway-cs/test/unit/NyssaAI.DbGateway.Tests.csproj',
        'nyssa-ai-dbgateway-cs/test/use_case/NyssaAI.DbGateway.UseCaseTests.csproj'
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
