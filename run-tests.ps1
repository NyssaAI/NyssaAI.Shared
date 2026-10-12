param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build NyssaAI.Shared.slnx --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $testProjects = @(
        'Nyssa.Result/test/Nyssa.Result.Tests/Nyssa.Result.Tests.csproj',
        'test/Nyssa.Result.UseCaseTests/Nyssa.Result.UseCaseTests.csproj',
        'Nyssa.Configuration/test/Nyssa.Configuration.Tests/Configuration.Tests.csproj',
        'test/Nyssa.Configuration.UseCaseTests/Nyssa.Configuration.UseCaseTests.csproj'
    )
    foreach ($testProject in $testProjects) {
        $projectDirectory = Split-Path -Parent $testProject
        $assemblyName = [IO.Path]::GetFileNameWithoutExtension($testProject)
        $outputDirectory = Join-Path $projectDirectory "bin/$Configuration/net10.0"
        # Configuration tutorials exercise the working-directory default. Keep their writes in
        # their own output folder, so cleanup cannot touch a developer's configuration directory.
        Push-Location $outputDirectory
        try {
            $runnerArguments = @('-trait-', 'Category=LiveService')
            if ($testProject -notlike '*Configuration*') { $runnerArguments += '-failSkips' }
            dotnet "$assemblyName.dll" @runnerArguments
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        finally {
            Pop-Location
        }
    }
}
finally {
    Pop-Location
}
