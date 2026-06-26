using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Helpers;

/// <summary>
/// Shared helper for tests that need to build a semantic model with stdlib
/// types loaded. After Task 42, String and Array are stdlib types (not
/// ambient types), so tests that reference <c>string.Length()</c> or
/// <c>array.Length</c> need the stdlib loaded.
/// </summary>
internal static class StdlibTestHelper
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
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
        if(_cachedUnits.HasValue)
            return (_cachedUnits.Value, _cachedAsts!.Value);

        var repoRoot = FindRepoRoot();
        var stdlibRoot = Path.Combine(repoRoot, "Source", "Standard");

        var loaderDiag = new DiagnosticBag();
        var units = StdlibLoader.LoadFrom(stdlibRoot, loaderDiag);

        ILexer lexer = new MistLexer();
        IParser parser = new MistParser();
        var parseDiag = new DiagnosticBag();
        var asts = ImmutableArray.CreateBuilder<Storm.Runtime.SyntaxNode>(units.Length);

        foreach(var unit in units)
        {
            var tokens = lexer.Lex(unit.Source, parseDiag);
            var parseResult = parser.Parse(tokens, parseDiag);
            if(parseResult.Root is not null)
                asts.Add(parseResult.Root);
        }

        _cachedUnits = units;
        _cachedAsts = asts.ToImmutable();
        return (units, _cachedAsts.Value);
    }

    private static ImmutableArray<CompilationUnit>? _cachedUnits;
    private static ImmutableArray<Storm.Runtime.SyntaxNode>? _cachedAsts;

    /// <summary>
    /// Builds a semantic model that includes stdlib types alongside the
    /// given user AST and source.
    /// </summary>
    public static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithStdlib(
        Storm.Runtime.SyntaxNode userAst,
        IAmbientTypeResolver? ambientTypeResolver = null,
        string source = "")
    {
        var (stdlibUnits, stdlibAsts) = LoadStdlib();

        var userSourceText = new SourceText("test.mist", source);
        var userUnit = new CompilationUnit(userSourceText);

        var allUnits = stdlibUnits.Add(userUnit);
        var allAsts = stdlibAsts.Add(userAst);

        var compilation = new MistCompilation(allUnits);
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(
            compilation, allAsts, ambientTypeResolver,
            namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);

        return (model, diagnostics.ToImmutableArray());
    }
}