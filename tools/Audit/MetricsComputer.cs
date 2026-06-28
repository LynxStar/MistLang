using Audit.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using TypeInfo = Audit.Models.TypeInfo;

namespace Audit;

public static class MetricsComputer
{
    /// <summary>
    /// Computes metrics for a single file: line count and per-type info.
    /// Returns zero metrics on read failure (logs warning to stderr).
    /// </summary>
    public static FileMetrics Compute(string absolutePath)
    {
        try
        {
            var lines = File.ReadAllLines(absolutePath);
            var lineCount = lines.Length;

            var source = string.Join(Environment.NewLine, lines);
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetRoot();

            var types = ExtractTypes(root);

            return new FileMetrics(lineCount, types);
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not read file '{absolutePath}': {ex.Message}");
            return new FileMetrics(0, []);
        }
    }

    /// <summary>
    /// Collects all declared type names across the given files.
    /// Returns a map from type name to (declaring file path, index in that file's Types list).
    /// If a type name appears in multiple files, it is excluded (ambiguous).
    /// </summary>
    public static Dictionary<string, (string FilePath, int TypeIndex)> CollectDeclaredTypes(
        string repositoryRoot, IReadOnlyList<string> filePaths)
    {
        var seen = new Dictionary<string, (string FilePath, int TypeIndex)>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        foreach(var relativePath in filePaths)
        {
            var absolutePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            SyntaxNode root;
            try
            {
                var source = File.ReadAllText(absolutePath);
                var tree = CSharpSyntaxTree.ParseText(source);
                root = tree.GetRoot();
            }
            catch
            {
                continue;
            }

            var types = ExtractTypes(root);
            for(var i = 0; i < types.Count; i++)
            {
                var name = types[i].Name;
                if(ambiguous.Contains(name))
                {
                    continue;
                }

                if(seen.ContainsKey(name))
                {
                    _ = seen.Remove(name);
                    _ = ambiguous.Add(name);
                }
                else
                {
                    seen[name] = (relativePath, i);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// Scans all files for identifier references to known type names.
    /// Returns type name to count of distinct other files that reference it.
    /// </summary>
    public static Dictionary<string, int> ComputeExternalDependents(
        string repositoryRoot, IReadOnlyList<string> filePaths,
        Dictionary<string, string> typeToFile)
    {
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach(var typeName in typeToFile.Keys)
        {
            dependents[typeName] = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach(var relativePath in filePaths)
        {
            var absolutePath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            SyntaxNode root;
            try
            {
                var source = File.ReadAllText(absolutePath);
                var tree = CSharpSyntaxTree.ParseText(source);
                root = tree.GetRoot();
            }
            catch
            {
                continue;
            }

            var referencedTypes = new HashSet<string>(StringComparer.Ordinal);

            foreach(var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                var text = identifier.Identifier.Text;
                if(typeToFile.ContainsKey(text))
                {
                    _ = referencedTypes.Add(text);
                }
            }

            foreach(var generic in root.DescendantNodes().OfType<GenericNameSyntax>())
            {
                var text = generic.Identifier.Text;
                if(typeToFile.ContainsKey(text))
                {
                    _ = referencedTypes.Add(text);
                }
            }

            foreach(var typeName in referencedTypes)
            {
                if(!string.Equals(typeToFile[typeName], relativePath, StringComparison.Ordinal))
                {
                    _ = dependents[typeName].Add(relativePath);
                }
            }
        }

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach(var (typeName, files) in dependents)
        {
            result[typeName] = files.Count;
        }

        return result;
    }

    private static List<TypeInfo> ExtractTypes(SyntaxNode root)
    {
        var types = new List<TypeInfo>();

        foreach(var node in root.DescendantNodes())
        {
            switch(node)
            {
                case EnumDeclarationSyntax enumDecl:
                    types.Add(new TypeInfo(enumDecl.Identifier.Text, "enum", true, 0, 0));
                    break;

                case TypeDeclarationSyntax typeDecl:
                    var name = typeDecl.Identifier.Text;
                    var kind = typeDecl switch
                    {
                        RecordDeclarationSyntax => "record",
                        ClassDeclarationSyntax => "class",
                        StructDeclarationSyntax => "struct",
                        InterfaceDeclarationSyntax => "interface",
                        _ => "class",
                    };

                    var methodCount = typeDecl.Members
                        .OfType<MethodDeclarationSyntax>()
                        .Count();

                    var isPureData = methodCount == 0;

                    types.Add(new TypeInfo(name, kind, isPureData, methodCount, 0));
                    break;
            }
        }

        return types;
    }
}