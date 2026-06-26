// Bugfix: Entry point detection fails when abstract type is present in same file.
// Reproduces the issue where CheckEntryPoint emits MIST_TC_0017 ("no valid entry point")
// when an abstract declaration coexists with a class containing Main.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Regression tests for the entry point detection bug where the presence of an
/// abstract declaration in the same compilation unit causes the Main
/// method symbol to not be found via GetDeclaringNode, resulting in a spurious
/// MIST_TC_0017 diagnostic.
/// </summary>
public sealed class EntryPointWithAbstractBugTests : IDisposable
{
    private readonly string outputDir;

    public EntryPointWithAbstractBugTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistEntryPointBug_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private CompilerResult CompileSource(string source, string fileName = "Program.mist")
    {
        var filePath = Path.Combine(outputDir, fileName);
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        return SharedDriver.Compile(options);
    }

    // ─── Bug reproduction tests ──────────────────────────────────────────

    /// <summary>
    /// Baseline: class with Main alone compiles without MIST_TC_0017.
    /// </summary>
    [Fact]
    public void ClassWithMainAlone_NoEntryPointError()
    {
        var source = """
            namespace Test;
            public class Program {
                public static void Main() { }
            }
            """;

        var result = CompileSource(source);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Id.Value == "MIST_TC_0017");
    }

    /// <summary>
    /// Reproduces the bug: abstract + class with Main in same file → MIST_TC_0017.
    /// </summary>
    [Fact]
    public void AbstractAndClassWithMain_ShouldNotProduceMIST_TC_0017()
    {
        var source = """
            namespace Test;
            public abstract IFoo {
                string GetName();
            }
            public class Program {
                public static void Main() { }
            }
            """;

        var result = CompileSource(source);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Id.Value == "MIST_TC_0017");
    }

    /// <summary>
    /// Diagnostic test: verify the Main symbol is findable in the semantic model.
    /// </summary>
    [Fact]
    public void AbstractAndClassWithMain_MainSymbolIsRegistered()
    {
        var source = """
            namespace Test;
            public abstract IFoo {
                string GetName();
            }
            public class Program {
                public static void Main() { }
            }
            """;

        // Build the semantic model directly to inspect it
        var filePath = Path.Combine(outputDir, "Diag.mist");
        File.WriteAllText(filePath, source);

        var lexer = new Mist.Lexing.MistLexer();
        var parser = new Mist.Parsing.MistParser();
        var builder = new SemanticModelBuilder();

        var sourceText = new Mist.Common.Source.SourceText(filePath, source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var lexBag = new Mist.Common.Diagnostics.DiagnosticBag();
        var tokenStream = lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lexing failed");

        var parseBag = new Mist.Common.Diagnostics.DiagnosticBag();
        var parseResult = parser.Parse(tokenStream, parseBag);
        Assert.False(parseBag.HasErrors, "Parsing failed");
        Assert.NotNull(parseResult.Root);

        var semanticBag = new Mist.Common.Diagnostics.DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: outputDir,
            semanticBag);

        // Check that the parser correctly puts types in the namespace body
        var cu = parseResult.Root as Generated.CompilationUnitNode;
        Assert.NotNull(cu);
        var nsItem = cu.Items.OfType<Generated.NamespaceItemTopLevelItemNode>().FirstOrDefault();
        Assert.NotNull(nsItem);
        var nsDecl = nsItem.NamespaceDeclaration as Generated.FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(nsDecl);
        Assert.Equal(2, nsDecl.Body.Length);

        // Check that Main is in AllSymbols and findable
        var mainSymbols = model.AllSymbols
            .Where(s => s.Kind == Mist.Semantics.Contracts.SymbolKind.Method && s.Name == "Main")
            .ToList();
        Assert.NotEmpty(mainSymbols);

        foreach(var mainSym in mainSymbols)
        {
            var node = model.GetDeclaringNode(mainSym);
            Assert.NotNull(node);
        }
    }

    /// <summary>
    /// Multiple abstracts + class with Main should all work.
    /// </summary>
    [Fact]
    public void MultipleAbstractsAndClassWithMain_ShouldNotProduceMIST_TC_0017()
    {
        var source = """
            namespace Test;
            public abstract IFoo {
                string GetName();
            }
            public abstract IBar {
                int GetCount();
            }
            public class Program {
                public static void Main() { }
            }
            """;

        var result = CompileSource(source);

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Id.Value == "MIST_TC_0017");
    }
}