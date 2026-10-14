param([string] $Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $repoRoot ('.temp/packages-' + [Guid]::NewGuid().ToString('N'))
$feed = Join-Path $scratch 'feed'
$consumer = Join-Path $scratch 'consumer'
New-Item -ItemType Directory -Path $feed, $consumer -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$versions = @{}
[xml] $centralPackages = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Packages.props')
$dependencyInjectionVersion = ($centralPackages.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -eq 'Microsoft.Extensions.DependencyInjection' }).Version
foreach ($name in @('Result', 'Configuration', 'Logging', 'Repository')) {
    $id = "NyssaAI.$name"
    $project = Join-Path $repoRoot "$id/src/$id.csproj"
    [xml] $projectXml = Get-Content -LiteralPath $project
    $version = [string]($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    $versions[$id] = $version
    dotnet pack $project -c $Configuration -o $feed --nologo
    if ($LASTEXITCODE -ne 0) { throw "Packing $id failed." }
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $feed "$id.$version.nupkg"))
    try {
        foreach ($required in @('README.md', 'LICENSE.txt', "lib/net10.0/$id.dll", "$id.nuspec")) {
            if (-not $archive.GetEntry($required)) { throw "$id is missing $required." }
        }
        $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($manifest.package.metadata.id -ne $id -or $manifest.package.metadata.version -ne $version) {
            throw "Unexpected identity in $id package."
        }
        if ($manifest.package.metadata.license.InnerText -ne 'LICENSE.txt') {
            throw "Unexpected license metadata in $id package."
        }
        Write-Host "Inspected $id ${version}: assembly, README, license, identity."
    }
    finally { $archive.Dispose() }
}

# The generated consumer has no project references and uses a fresh package cache.
# Explicit local mapping prevents an existing public package from satisfying these IDs.
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
@"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedFeed" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="NyssaAI.*" /></packageSource>
    <packageSource key="nuget"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'NuGet.Config') -Encoding UTF8
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <GenerateDocumentationFile>true</GenerateDocumentationFile><IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="NyssaAI.Logging" Version="$($versions['NyssaAI.Logging'])" />
    <PackageReference Include="NyssaAI.Repository" Version="$($versions['NyssaAI.Repository'])" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="$dependencyInjectionVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $consumer 'Consumer.csproj') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PackageConsumer/Program.cs') -Destination $consumer
$consumerProject = Join-Path $consumer 'Consumer.csproj'
dotnet restore $consumerProject --configfile (Join-Path $consumer 'NuGet.Config') --packages (Join-Path $scratch 'cache')
if ($LASTEXITCODE -ne 0) { throw 'Restoring the isolated package consumer failed.' }
dotnet run --project $consumerProject -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw 'The package consumer failed.' }
