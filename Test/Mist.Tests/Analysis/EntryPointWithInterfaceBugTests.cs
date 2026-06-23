// Bugfix: Entry point detection fails when interface/trait is present in same file.
// Reproduces the issue where CheckEntryPoint emits MIST_TC_0017 ("no valid entry point")
// when an interface or trait declaration coexists with a class containing Main.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Regression tests for the entry point detection bug where the presence of an
/// interface or trait declaration in the same compilation unit causes the Main
/// method symbol to not be found via GetDeclaringNode, resulting in a spurious
/// MIST_TC_0017 diagnostic.
/// </summary>
public sealed class EntryPointWithInterfaceBugTests : IDisposable
{
    private readonly string _outputDir;

    public EntryPointWithInterfaceBugTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistEntryPointBug_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
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
        var filePath = Path.Combine(_outputDir, fileName);
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
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
    /// Reproduces the bug: interface + class with Main in same file → MIST_TC_0017.
    /// </summary>
    [Fact]
    public void InterfaceAndClassWithMain_ShouldNotProduceMIST_TC_0017()
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
    public void InterfaceAndClassWithMain_MainSymbolIsRegistered()
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
        var filePath = Path.Combine(_outputDir, "Diag.mist");
        File.WriteAllText(filePath, source);

        var lexer = new Mist.Lexing.MistLexer();
        var parser = new Mist.Parsing.MistParser();
        var builder = new SemanticModelBuilder();

        var sourceText = new Mist.Common.Text.SourceText(filePath, source);
        var compilation = new Mist.Driver.Contracts.MistCompilation(
            ImmutableArray.Create(new Mist.Driver.Contracts.CompilationUnit(sourceText)));

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
            projectFolder: _outputDir,
            semanticBag);

        // Check that the parser correctly puts types in the namespace body
        var cu = parseResult.Root as Mist.Generated.CompilationUnitNode;
        Assert.NotNull(cu);
        var nsItem = cu.Items.OfType<Mist.Generated.NamespaceItemTopLevelItemNode>().FirstOrDefault();
        Assert.NotNull(nsItem);
        var nsDecl = nsItem.NamespaceDeclaration as Mist.Generated.FileScopedNamespaceNamespaceDeclarationNode;
        Assert.NotNull(nsDecl);
        Assert.Equal(2, nsDecl.Body.Length);

        // Check that Main is in AllSymbols and findable
        var mainSymbols = model.AllSymbols
            .Where(s => s.Kind == Mist.Semantics.Contracts.SymbolKind.Method && s.Name == "Main")
            .ToList();
        Assert.NotEmpty(mainSymbols);

        foreach (var mainSym in mainSymbols)
        {
            var node = model.GetDeclaringNode(mainSym);
            Assert.NotNull(node);
        }
    }

    /// <summary>
    /// Reproduces the bug with a trait instead of an interface.
    /// </summary>
    [Fact]
    public void TraitAndClassWithMain_ShouldNotProduceMIST_TC_0017()
    {
        var source = """
            namespace Test;
            public trait Printable {
                string Name;
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
    /// Multiple interfaces + class with Main should all work.
    /// </summary>
    [Fact]
    public void MultipleInterfacesAndClassWithMain_ShouldNotProduceMIST_TC_0017()
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
