using System.Xml.Linq;
using Xunit;

namespace NyssaAI.Logging.Tests;

/// <summary>
/// The SP9h check the package owes for itself. This package publishes no root contract: it is a
/// provider package, so an application configures Microsoft logging and injects
/// <c>ILogger&lt;T&gt;</c>, and this package contributes one destination through an
/// <c>ILoggingBuilder</c> extension. That is a deliberate opt-out, declared in the project file and
/// pinned here, in the package that would break if someone reintroduced a client contract without
/// deciding to.
/// </summary>
public sealed class PackageSurfaceTests
{
    [Fact]
    public void Project_DeclaresTheProviderOptOutRatherThanAClientContract()
    {
        var project = XDocument.Load(Path.Combine(SourceRoot, "NyssaAI.Logging.csproj"));

        Assert.Empty(project.Descendants("ClientContractFile"));
        Assert.False(
            string.IsNullOrWhiteSpace(project.Descendants("SourceRootContractOptOut").SingleOrDefault()?.Value),
            "A package with no client contract must say why (SP9b): declare SourceRootContractOptOut.");
    }

    [Fact]
    public void SourceRoot_ProviderImplementationAndRegistrationHaveTheirOwnAreas()
    {
        Assert.Empty(Directory.GetFiles(SourceRoot, "*.cs"));
        Assert.True(Directory.Exists(Path.Combine(SourceRoot, "Configuration")));
        Assert.True(Directory.Exists(Path.Combine(SourceRoot, "FileLogging")));
        // This package implements Microsoft's provider contract. It has no separate client
        // implementation or public contract that would justify a Client directory.
        Assert.False(Directory.Exists(Path.Combine(SourceRoot, "Client")));
    }

    [Fact]
    public void SourceRoot_HasNoContractsAreaWhileNoPortIsPublished()
    {
        Assert.Empty(Directory.GetDirectories(SourceRoot, "Contracts", SearchOption.AllDirectories));
    }

    private static string SourceRoot => Path.Combine(PackageRoot, "src");

    private static string PackageRoot
    {
        get
        {
            // CI maps compile-time source paths; the executing assembly still has a real path.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "src"))
                    && Directory.Exists(Path.Combine(directory.FullName, "test")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the package root above the test assembly.");
        }
    }
}
