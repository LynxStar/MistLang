using Audit.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Audit;

public static class MetricsComputer
{
    /// <summary>
    /// Computes metrics for a single file: line count, type declaration count, method/constructor count.
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

            var typeCount = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Count()
                         + root.DescendantNodes().OfType<EnumDeclarationSyntax>().Count();

            var methodCount = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count()
                            + root.DescendantNodes().OfType<ConstructorDeclarationSyntax>().Count();

            return new FileMetrics(lineCount, typeCount, methodCount);
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not read file '{absolutePath}': {ex.Message}");
            return new FileMetrics(0, 0, 0);
        }
    }
}