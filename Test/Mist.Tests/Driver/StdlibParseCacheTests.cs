using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Semantics.Contracts;

namespace Mist.Tests.Driver;

public sealed class StdlibParseCacheTests : IDisposable
{
    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), $"mist-stdlib-parse-cache-{Guid.NewGuid():N}");

    public StdlibParseCacheTests()
    {
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(scratchDir, recursive: true);
        }
        catch(IOException)
        {
        }
    }

    [Fact]
    public void Compile_AfterACompileWhoseLexerRejectedTheStdlib_ParsesAndRecordsTheWholeStdlib()
    {
        var cache = new StdlibParseCache();
        var options = WriteProgram();
        _ = CreateDriver(new RejectingLexer(), new CapturingSemanticModelBuilder(), cache).Compile(options);

        var capture = new CapturingSemanticModelBuilder();
        _ = CreateDriver(new MistLexer(), capture, cache).Compile(options);

        var stdlibUnitCount = capture.Units.Count(u => u.IsStdlib);
        Assert.NotEqual(0, stdlibUnitCount);
        Assert.Equal(capture.Units.Length, capture.AstCount);
        Assert.Equal(stdlibUnitCount, cache.TryGet(stdlibUnitCount)?.Asts.Length);
    }

    [Fact]
    public void TryGet_ForAStdlibOfAnotherSize_ReturnsNothing()
    {
        var cache = new StdlibParseCache();
        cache.Offer(0, ImmutableArray<TokenStream>.Empty, ImmutableArray<Storm.Runtime.SyntaxNode>.Empty);

        Assert.Null(cache.TryGet(1));
    }

    private CompilerOptions WriteProgram()
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, "namespace X;\npublic class Y { }\n");
        return new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "X",
            NamespaceRoot: "X",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);
    }

    private static CompilerDriver CreateDriver(ILexer lexer, ISemanticModelBuilder builder, StdlibParseCache cache)
    {
        return new CompilerDriver(
            lexer,
            new MistParser(),
            builder,
            ImmutableArray<IAnalyzer>.Empty,
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()),
            cache);
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

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        public ImmutableArray<CompilationUnit> Units { get; private set; } = ImmutableArray<CompilationUnit>.Empty;

        public int AstCount { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            Units = compilation.Units;
            AstCount = asts.Length;
            return null!;
        }
    }
}