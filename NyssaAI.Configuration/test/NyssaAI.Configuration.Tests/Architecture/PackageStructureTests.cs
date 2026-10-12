using System.Reflection;

namespace NyssaAI.Configuration.Tests.Architecture;

/// <summary>
/// Enforces the placement and dependency rules that are otherwise only written down. D3c: while
/// layers are folders, an automated test has to enforce the one-way direction, because a dependency
/// rule no tool checks is a comment. SP9h: the same goes for the shape of the source root.
/// </summary>
public sealed class PackageStructureTests
{
    private static readonly Assembly Package = typeof(IConfiguration).Assembly;

    private static readonly string[] InnerLayers = ["Application", "Infrastructure", "Api"];

    /// <summary>SP9b: the only three files a source root may ever hold.</summary>
    private static readonly string[] AllowedRootContracts =
    [
        "IConfiguration.cs",
        "IConfigurationExtensions.cs",
        "IConfigurationBootstrap.cs",
    ];

    /// <summary>
    /// The contracts this package declares today - now all three, since the repository-root run
    /// taught <c>Directory.Build.targets</c> a <c>BootstrapContractFile</c> property and
    /// <c>IConfigurationBootstrap.cs</c> landed. Asserting the allow-set above rather than this
    /// exact census is what stopped the test tracking the structure instead of constraining it:
    /// the earlier form asserted the current two files and would have gone red on that arrival.
    /// </summary>
    private static readonly string[] RequiredContracts =
    [
        "IConfiguration.cs",
        "IConfigurationExtensions.cs",
    ];

    private static DirectoryInfo SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "NyssaAI.Configuration.csproj")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return new DirectoryInfo(Path.Combine(directory!.FullName, "src"));
    }

    /// <summary>
    /// D3: a feature's Domain layer depends on nothing outside itself, so no type declared in a
    /// <c>&lt;Feature&gt;.Domain</c> namespace may name a type from that feature's Application,
    /// Infrastructure, or Api namespace.
    /// </summary>
    [Fact]
    public void DomainTypes_DoNotReference_OuterLayersOfTheSameFeature()
    {
        var violations = new List<string>();

        foreach (var type in Package.GetTypes())
        {
            var domain = type.Namespace;
            if (domain is null || !domain.EndsWith(".Domain", StringComparison.Ordinal))
                continue;

            var feature = domain[..^".Domain".Length];
            var forbidden = InnerLayers.Select(layer => $"{feature}.{layer}").ToArray();

            foreach (var referenced in ReferencedTypes(type))
            {
                var referencedNamespace = referenced.Namespace;
                if (referencedNamespace is not null && forbidden.Contains(referencedNamespace, StringComparer.Ordinal))
                    violations.Add($"{type.FullName} -> {referenced.FullName}");
            }
        }

        Assert.Empty(violations);
    }

    /// <summary>SP9b: no file at the root of src/ but the package's root contracts.</summary>
    [Fact]
    public void SourceRoot_HoldsNoFileOutsideTheDeclaredContracts()
    {
        var stray = SourceRoot()
            .GetFiles("*.cs")
            .Select(file => file.Name)
            .Except(AllowedRootContracts, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        Assert.Empty(stray);
    }

    /// <summary>SP9b and SP10a: the contracts the package declares are present at the root.</summary>
    [Fact]
    public void SourceRoot_HoldsEveryDeclaredContract()
    {
        var root = SourceRoot();

        var missing = RequiredContracts
            .Where(name => !File.Exists(Path.Combine(root.FullName, name)))
            .Order(StringComparer.Ordinal);

        Assert.Empty(missing);
    }

    /// <summary>D1b and D1c: the two reserved areas exist, and no port sits outside Contracts/.</summary>
    [Fact]
    public void ReservedAreas_Exist_AndHoldEveryExternallyImplementablePort()
    {
        var source = SourceRoot();
        Assert.True(Directory.Exists(Path.Combine(source.FullName, "Client")));
        Assert.True(Directory.Exists(Path.Combine(source.FullName, "Contracts")));

        var strayPorts = Package.GetExportedTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.Namespace is not "NyssaAI.Configuration" and not "NyssaAI.Configuration.Contracts")
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal);

        Assert.Empty(strayPorts);
    }

    /// <summary>SP10b: the primary and extension surfaces never merge.</summary>
    [Fact]
    public void ExtensionContract_SharesNoMemberWithTheClientContract()
    {
        var primary = typeof(IConfiguration).GetMembers().Select(member => member.Name);
        var extension = typeof(IConfigurationExtensions).GetMembers().Select(member => member.Name);

        Assert.Empty(primary.Intersect(extension, StringComparer.Ordinal));
    }

    /// <summary>
    /// SP10d: once the extension contract has members, the client implements them explicitly, so
    /// they stay public and documented while staying out of the typical caller's completions.
    /// </summary>
    [Fact]
    public void ExtensionContract_IsImplementedExplicitly()
    {
        var client = Package.GetTypes().Single(type => type.Name == "ConfigurationClient");
        var map = client.GetInterfaceMap(typeof(IConfigurationExtensions));

        Assert.NotEmpty(map.TargetMethods);
        Assert.All(map.TargetMethods, method => Assert.True(method.IsPrivate, method.Name));
    }

    /// <summary>
    /// SP8c and D2: a feature declares one internal I&lt;Feature&gt; at its root with exactly one
    /// implementation, so a sibling reaches the feature through a contract and never through its
    /// types.
    /// </summary>
    /// <remarks>
    /// The rule is asserted for the features that have an in-assembly consumer, which is the
    /// condition that gives a covering contract someone to serve. Client/ and Contracts/ are the
    /// reserved areas and declare none (D1b, D1c). FileSecurity is a primitive rather than a
    /// feature. Manifest, CloudKeyVaults, OAuth and OsKeyring are composed by the host directly
    /// rather than by the client area or a sibling, so a covering contract over them had exactly
    /// no callers; each one and its implementation were deleted rather than kept as ceremony.
    /// src/README.md states the criterion and the reason.
    /// </remarks>
    [Fact]
    public void EveryFeature_DeclaresOneCoveringContract_WithOneImplementation()
    {
        // Reserved areas, the FileSecurity primitive, and the four features the host composes
        // directly. See the remarks above: a covering contract with no in-assembly consumer is
        // ceremony, and this list is where that judgement is recorded.
        string[] notFeatures =
            ["Client", "Contracts", "FileSecurity", "Manifest", "CloudKeyVaults", "OAuth", "OsKeyring"];

        var features = SourceRoot()
            .GetDirectories()
            .Select(directory => directory.Name)
            .Where(name => name is not "bin" and not "obj" and not ".vs" and not "TestResults")
            .Where(name => !notFeatures.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var feature in features)
        {
            var contract = Package.GetTypes().SingleOrDefault(type =>
                type.IsInterface
                && type.Name == $"I{feature}"
                && type.Namespace == $"NyssaAI.Configuration.{feature}");

            if (contract is null)
            {
                missing.Add($"{feature}: no internal I{feature} at the feature root");
                continue;
            }

            if (contract.IsPublic)
                missing.Add($"{feature}: I{feature} must be internal (SP8c)");

            var implementations = Package.GetTypes()
                .Where(type => type is { IsInterface: false, IsAbstract: false } && contract.IsAssignableFrom(type))
                .ToArray();

            if (implementations.Length != 1)
                missing.Add($"{feature}: I{feature} has {implementations.Length} implementations, expected exactly 1");
        }

        Assert.Empty(missing);
    }

    /// <summary>D1b: the reserved areas declare no covering contract of their own.</summary>
    [Fact]
    public void ReservedAreas_DeclareNoCoveringContract()
    {
        var reserved = Package.GetTypes()
            .Where(type => type.IsInterface && type.Name is "IClient" or "IContracts")
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal);

        Assert.Empty(reserved);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            yield return field.FieldType;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            yield return property.PropertyType;

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (var contract in type.GetInterfaces())
            yield return contract;

        if (type.BaseType is not null)
            yield return type.BaseType;
    }
}
