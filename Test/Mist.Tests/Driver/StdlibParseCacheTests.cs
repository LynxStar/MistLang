using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

public sealed class StdlibParseCacheTests : IDisposable
{
    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), $"mist-stdlib-parse-cache-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if(!Directory.Exists(scratchDir))
        {
            return;
        }

        try
        {
            Directory.Delete(scratchDir, recursive: true);
        }
        catch(IOException)
        {
        }
    }

    [Fact]
    [Trait("Tier", "4")]
    public void Compile_AfterACompileWhoseLexerRejectedTheStdlib_ParsesAndRecordsTheWholeStdlib()
    {
        var cache = new StdlibParseCache();
        var options = WriteProgram();
        _ = CreateDriver(new RejectingLexer(), cache).Compile(options);

        var result = CreateDriver(new MistLexer(), cache).Compile(options);

        var whole = StdlibLoader.LoadFor(new DiagnosticBag());
        Assert.NotEmpty(whole);
        Assert.Equal(whole.Length, cache.TryGet(whole.Length)?.Asts.Length);
        Assert.DoesNotContain(result.Diagnostics, d => d.Stage == PipelineStage.Parsing);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void TryGet_ForAStdlibOfAnotherSize_ReturnsNothing()
    {
        var cache = new StdlibParseCache();
        cache.Offer(0, ImmutableArray<TokenStream>.Empty, ImmutableArray<Storm.Runtime.SyntaxNode>.Empty);

        Assert.Null(cache.TryGet(1));
    }

    private CompilerOptions WriteProgram()
    {
        _ = Directory.CreateDirectory(scratchDir);
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, "namespace X;\npublic class Y { }\n");
        return new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "X",
            NamespaceRoot: "X");
    }

    private static CompilerDriver CreateDriver(ILexer lexer, StdlibParseCache cache)
    {
        return new CompilerDriver(lexer, new MistParser(), cache, ambientTypes: _ => StubAmbientTypeSource.Instance);
    }

    private sealed class RejectingLexer : ILexer
    {
        public TokenStream Lex(SourceText source, DiagnosticBag diagnostics)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_TEST_LEX_REJECTED"),
                DiagnosticSeverity.Error,
                PipelineStage.Lexing,
                "Rejected by the test lexer",
                default,
                source.Path,
                null));
            return new TokenStream(source, ImmutableArray<Storm.Runtime.Token>.Empty);
        }
    }
}