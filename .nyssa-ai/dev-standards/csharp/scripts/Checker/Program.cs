using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            Console.Error.WriteLine("usage: Checker SOURCE_ROOT [grouped-dtos.json]");
            return 2;
        }
        try { return Check(Path.GetFullPath(args[0]), args.Length == 2 ? args[1] : null); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    private static int Check(string root, string? groupingFile)
    {
        var groups = groupingFile is null ? new Dictionary<string, string>() :
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(groupingFile)) ?? throw new JsonException("Expected an object of relative file paths to reasons.");
        var usedGroups = new HashSet<string>();
        var failures = 0;
        var count = 0;
        void Report(string path, SyntaxNode? node, string rule, string message)
        {
            var line = node?.GetLocation().GetLineSpan().StartLinePosition.Line + 1 ?? 1;
            Console.WriteLine($"{path}:{line} {rule} {message}");
            failures++;
        }
        IEnumerable<string> Walk(string directory)
        {
            var info = new DirectoryInfo(directory);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"Refusing symlink: {directory}");
            var lowerName = info.Name.ToLowerInvariant();
            if (new[] { "contracts", "shared", "utils", "helpers", "common", "misc", "types", "models" }.Contains(lowerName) ||
                (directory != root && new[] { "tmp", "temp", "scratch" }.Contains(lowerName)))
                Report(directory, null, "CS-STRUCTURE", "Forbidden catch-all or scratch folder.");
            foreach (var file in Directory.GetFiles(directory, "*.cs"))
            {
                if (File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint)) throw new IOException($"Refusing symlink: {file}");
                yield return file;
            }
            foreach (var child in Directory.GetDirectories(directory))
            {
                var name = Path.GetFileName(child);
                if (name.StartsWith('.') || name is "bin" or "obj") continue;
                foreach (var file in Walk(child)) yield return file;
            }
        }
        foreach (var file in Walk(root))
        {
            count++;
            var rawText = File.ReadAllText(file);
            var normalizedLines = rawText.Replace("\r\n", "\n").TrimEnd('\n');
            var lineCount = normalizedLines.Length == 0 ? 0 : normalizedLines.Split('\n').Length;
            if (lineCount > 350)
                Report(file, null, "CS-FILE", $"File has {lineCount} lines; maximum allowed is 350 (files over 100 lines are discouraged).");
            var tree = CSharpSyntaxTree.ParseText(rawText);
            foreach (var error in tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error))
                Report(file, null, "CS-PARSE", error.ToString());
            var syntax = tree.GetRoot();
            var types = syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().ToList();
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (types.Count > 1)
            {
                var allowed = groups.TryGetValue(relative, out var reason) && !string.IsNullOrWhiteSpace(reason);
                var namespaces = types.Select(t => string.Join('.', t.Ancestors()
                    .OfType<BaseNamespaceDeclarationSyntax>().Reverse()
                    .Select(n => string.Concat(n.Name.DescendantTokens().Select(token => token.ValueText)))))
                    .Distinct().ToList();
                bool Trivial(BaseTypeDeclarationSyntax type) => type is EnumDeclarationSyntax || type is RecordDeclarationSyntax record &&
                    record.ParameterList is null && record.BaseList is null && record.TypeParameterList is null && !record.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any() &&
                    record.Members.All(m => m is PropertyDeclarationSyntax property && property.ExpressionBody is null &&
                        property.AccessorList is not null && property.AccessorList.Accessors.All(a => a.Body is null && a.ExpressionBody is null));
                if (!allowed || namespaces.Count != 1 || namespaces[0].Length == 0 || !types.All(Trivial))
                    Report(file, null, "CS-FILE", "Multiple types require a reviewed same-namespace simple DTO/enum grouping entry; classes/interfaces with behavior cannot be grouped.");
                else usedGroups.Add(relative);
            }
            else if (types.Count == 1 && Path.GetFileNameWithoutExtension(file) != types[0].Identifier.ValueText)
                Report(file, types[0], "CS-FILE", $"Rename file for {types[0].Identifier.ValueText}.");
            foreach (var type in types)
            {
                if (type is RecordDeclarationSyntax record && record.ParameterList is not null)
                    Report(file, record, "CS-DATA", "Use an expanded-body record, not a positional record.");
                if (type is ClassDeclarationSyntax cls && cls.ParameterList is not null)
                    Report(file, cls, "CS-DATA", "Primary constructors are prohibited.");
                if (type is StructDeclarationSyntax structure && structure.ParameterList is not null)
                    Report(file, structure, "CS-DATA", "Primary constructors are prohibited.");
                if (type is InterfaceDeclarationSyntax contract && !contract.Identifier.ValueText.StartsWith('I'))
                    Report(file, contract, "CS-NAME", "Interface names require an I prefix.");
            }
            var segments = relative.Split('/').Prepend(Path.GetFileName(Path.TrimEndingDirectorySeparator(root))).ToList();
            var role = segments.LastOrDefault(p => p is "Domain" or "Application");
            if (role is not null)
            {
                var dirSegmentsBeforeRole = segments.Skip(1).TakeWhile(s => s != role).ToList();
                var rootPrefixes = syntax.DescendantNodes()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .Select(n => string.Concat(n.Name.DescendantTokens().Select(token => token.ValueText)).Split('.'))
                    .Select(nsParts =>
                    {
                        var roleIdx = Array.LastIndexOf(nsParts, role);
                        if (roleIdx <= 0) return Array.Empty<string>();
                        var prefixLen = roleIdx - dirSegmentsBeforeRole.Count;
                        if (prefixLen > 0 && nsParts.Skip(prefixLen).Take(dirSegmentsBeforeRole.Count).SequenceEqual(dirSegmentsBeforeRole))
                            return nsParts.Take(prefixLen).ToArray();
                        return nsParts.Take(roleIdx).ToArray();
                    })
                    .Where(p => p.Length > 0)
                    .ToList();

                foreach (var name in syntax.DescendantNodes().OfType<QualifiedNameSyntax>())
                {
                    if (name.Parent is QualifiedNameSyntax) continue;
                    if (name.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(ns => ns.Name.Span.Contains(name.Span))) continue;
                    var parts = name.ToString().Split('.');
                    var matchPrefix = rootPrefixes.FirstOrDefault(prefix =>
                        parts.Length > prefix.Length && parts.Take(prefix.Length).SequenceEqual(prefix));
                    var effectiveParts = matchPrefix is not null ? parts.Skip(matchPrefix.Length) : parts;
                    if (effectiveParts.Any(p => p is "Infrastructure" or "Configuration" or "Api" or "Ui" || role == "Domain" && p == "Application"))
                        Report(file, name, "CS-BOUNDARY", $"{role} references an outer named layer.");
                }
            }
        }
        foreach (var unused in groups.Keys.Except(usedGroups)) Report(groupingFile!, null, "CS-FILE", $"Unused grouping exception: {unused}");
        if (count == 0) { Console.Error.WriteLine("No C# source files found."); return 2; }
        if (failures > 0) return 1;
        Console.WriteLine($"PASS: {count} C# files; parsed type placement, grouping, record syntax, interface prefixes, folders and named-layer references. Full conformance requires review.");
        return 0;
    }
}
