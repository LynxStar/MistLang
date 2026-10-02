using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Helpers;

internal static class StdlibTestHelper
{
    internal static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    }

    /// <summary>
    /// Loads stdlib compilation units, lexes/parses them, and returns the
    /// units and their ASTs ready to be prepended to a test compilation.
    /// Results are cached for the process lifetime.
    /// </summary>
    public static (ImmutableArray<CompilationUnit> Units, ImmutableArray<Storm.Runtime.SyntaxNode> Asts) LoadStdlib()
    {
        return Stdlib.Value;
    }

    private static readonly Lazy<(ImmutableArray<CompilationUnit> Units, ImmutableArray<Storm.Runtime.SyntaxNode> Asts)> Stdlib =
        new(ReadStdlib);

    private static (ImmutableArray<CompilationUnit> Units, ImmutableArray<Storm.Runtime.SyntaxNode> Asts) ReadStdlib()
    {
        var repoRoot = FindRepoRoot();
        var stdlibRoot = Path.Combine(repoRoot, "Source", "Standard");

        var loaderDiag = new DiagnosticBag();
        var units = StdlibLoader.LoadFrom(stdlibRoot, loaderDiag);
        if(loaderDiag.HasErrors || units.IsEmpty)
        {
            throw new InvalidOperationException(
                $"No standard library could be read from {stdlibRoot}: "
                + string.Join("; ", loaderDiag.ToImmutableArray().Select(d => d.Message)));
        }

        ILexer lexer = new MistLexer();
        IParser parser = new MistParser();
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>(units.Length);

        foreach(var unit in units)
        {
            var parseDiag = new DiagnosticBag();
            var tokens = lexer.Lex(unit.Source, parseDiag);
            var parseResult = parser.Parse(tokens, parseDiag);
            if(parseResult.Root is null || parseDiag.HasErrors)
            {
                throw new InvalidOperationException(
                    $"{unit.Source.Path} did not lex and parse: "
                    + string.Join("; ", parseDiag.ToImmutableArray().Concat(parseResult.Diagnostics).Select(d => d.Message)));
            }

            asts.Add(parseResult.Root);
        }

        return (units, asts.ToImmutable());
    }
}