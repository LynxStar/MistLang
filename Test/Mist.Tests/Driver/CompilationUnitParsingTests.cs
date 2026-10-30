using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class CompilationUnitParsingTests
{
    [Fact]
    public void ParsingKeepsUnitOrderAndSourceAssociationRegardlessOfTokenDictionaryOrder()
    {
        var standard = Unit("Standard", "Number.mist") with { IsStdlib = true };
        var reference = Unit("Library", "Item.mist") with { OriginProjectPath = "/Library/mist.toml" };
        var user = Unit("Application", "Program.mist");
        var compilation = new MistCompilation([standard, reference, user], "Program.mist", ProjectOutputType.Library)
        {
            DisplayName = "Application",
            HasStandardLibrary = true,
            Projects = [new MistProject("Library", "/Library", "/Library/mist.toml")],
            GlobalUsings = ["Library"],
        };
        var parser = new RecordingParser();
        var cache = new StdlibParseCache();
        var reporter = new RecordingProgressReporter();
        var driver = new CompilerDriver(new UnusedLexer(), parser, cache, reporter);
        var tokens = Tokens(user, reference, standard);
        var diagnostics = new DiagnosticBag();

        var parsed = driver.Parse(compilation, tokens, ImmutableArray.Create(tokens[standard]).ToBuilder(), null, diagnostics);

        Assert.Equal(compilation.Units.Select(unit => unit.Source.Path), parser.Paths);
        Assert.Equal(compilation with { Units = parsed.Units }, parsed);
        Assert.Equal(compilation.Units.Length, parsed.Units.Length);
        for(var i = 0; i < parsed.Units.Length; i++)
        {
            var original = compilation.Units[i];
            var root = parser.Roots[original.Source.Path];
            Assert.Equal(original with { Ast = root }, parsed.Units[i]);
            Assert.Same(root, parsed.Units[i].Ast);
            Assert.Null(original.Ast);
        }

        Assert.Same(parsed.Units[0].Ast, Assert.Single(cache.TryGet(1)!.Asts));
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal(["start 3", "complete 3"], reporter.Sequence);
        Assert.Equal(["3 AST nodes"], reporter.DetailsOf("3"));
    }

    [Fact]
    public void CachedStandardRootsStayOnTheirUnitsAndAreNotReparsed()
    {
        var first = Unit("Standard", "First.mist") with { IsStdlib = true };
        var user = Unit("App", "Program.mist");
        var second = Unit("Standard", "Second.mist") with { IsStdlib = true };
        var roots = ImmutableArray.Create<Storm.Runtime.SyntaxNode>(new CompilationUnitNode(), new CompilationUnitNode());
        var cache = new StdlibParseCache();
        cache.Offer(2, [new(first.Source, []), new(second.Source, [])], roots);
        var parser = new RecordingParser();
        var driver = new CompilerDriver(new UnusedLexer(), parser, cache);

        var parsed = driver.Parse(new MistCompilation([first, user, second]), Tokens(user), null, cache.TryGet(2), new DiagnosticBag());

        Assert.Equal([user.Source.Path], parser.Paths);
        Assert.Equal(new[] { first.Source, user.Source, second.Source }, parsed.Units.Select(unit => unit.Source));
        Assert.Same(roots[0], parsed.Units[0].Ast);
        Assert.Same(parser.Roots[user.Source.Path], parsed.Units[1].Ast);
        Assert.Same(roots[1], parsed.Units[2].Ast);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParseErrorsDoNotAttachOrCachePartialRoots(bool reportInBag)
    {
        var failed = Unit("Standard", "Rejected.mist") with { IsStdlib = true };
        var user = Unit("App", "Program.mist");
        var error = DiagnosticFixtures.Occurrence("TEST_PARSE_REJECTED", DiagnosticSeverity.Error,
            PipelineStage.Parsing, "Rejected by the parser", new(new(2, 3), new(2, 7)), failed.Source.Path, null);
        var parser = new RecordingParser(failed.Source.Path, error, reportInBag);
        var cache = new StdlibParseCache();
        var diagnostics = new DiagnosticBag();
        var driver = new CompilerDriver(new UnusedLexer(), parser, cache);
        var tokens = Tokens(failed, user);

        var parsed = driver.Parse(new MistCompilation([failed, user]), tokens, ImmutableArray.Create(tokens[failed]).ToBuilder(), null, diagnostics);

        Assert.Same(failed, parsed.Units[0]);
        Assert.Null(parsed.Units[0].Ast);
        Assert.Same(parser.Roots[user.Source.Path], parsed.Units[1].Ast);
        Assert.Same(error, Assert.Single(diagnostics.ToImmutableArray()));
        Assert.Null(cache.TryGet(1));
    }

    [Fact]
    public void AUnitWithoutTokensDoesNotConsumeTheFollowingUnitsRoot()
    {
        var failed = Unit("App", "Rejected.mist");
        var parsedUnit = Unit("App", "Parsed.mist");
        var parser = new RecordingParser();
        var driver = new CompilerDriver(new UnusedLexer(), parser, new StdlibParseCache());

        var parsed = driver.Parse(new MistCompilation([failed, parsedUnit]), Tokens(parsedUnit), null, null, new DiagnosticBag());

        Assert.Same(failed, parsed.Units[0]);
        Assert.Null(parsed.Units[0].Ast);
        Assert.Same(parser.Roots[parsedUnit.Source.Path], parsed.Units[1].Ast);
        Assert.Equal([parsedUnit.Source.Path], parser.Paths);
    }

    private static CompilationUnit Unit(string project, string name)
    {
        return new CompilationUnit(new SourceText($"/{project}/{name}", ""), project, $"/{project}");
    }

    private static Dictionary<CompilationUnit, TokenStream> Tokens(params CompilationUnit[] units)
    {
        return units.ToDictionary(unit => unit, unit => new TokenStream(unit.Source, []));
    }

    private sealed class UnusedLexer : ILexer
    {
        public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
        {
            throw new InvalidOperationException("Parsing must not invoke lexing.");
        }
    }

    private sealed class RecordingParser(string? rejected = null, Diagnostic? error = null, bool reportInBag = false) : IParser
    {
        public List<string> Paths { get; } = [];
        public Dictionary<string, Storm.Runtime.SyntaxNode> Roots { get; } = [];

        public ParseResult Parse(TokenStream tokens, DiagnosticBag diagnostics)
        {
            Paths.Add(tokens.Source.Path);
            var root = new CompilationUnitNode();
            Roots.Add(tokens.Source.Path, root);
            if(tokens.Source.Path == rejected && reportInBag)
            {
                diagnostics.Add(error!);
            }

            return new ParseResult(tokens.Source, tokens, root,
                tokens.Source.Path == rejected && !reportInBag ? [error!] : []);
        }
    }
}