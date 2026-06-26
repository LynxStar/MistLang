using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Verifies that Console, Math, and Convert resolve correctly as stdlib types
/// when <c>using Standard;</c> is in scope, and that <c>using System;</c> no
/// longer resolves them.
///
/// Validates: Requirements 7.3, 7.4, 8.5
/// </summary>
public sealed class StdlibResolutionTests : IDisposable
{
    private readonly string scratchDir;

    public StdlibResolutionTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-stdlib-res-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wraps <see cref="SemanticModelBuilder"/> to capture the semantic model
    /// after the driver builds it, so we can inspect resolved symbols.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();

        public ISemanticModel? CapturedModel { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            CapturedModel = model;
            return model;
        }
    }

    private static CompilerDriver CreateDriver()
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private static CompilerDriver CreateDriverWithCapture(CapturingSemanticModelBuilder capture)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private CompilerOptions MakeOptions(string filePath, ImmutableArray<string> globalUsings = default)
    {
        return new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false,
            GlobalUsings: globalUsings);
    }

    // --- using Standard; resolves Console, Math, Convert without errors ---

    [Fact]
    public void UsingStandard_ConsoleWriteLine_NoErrors()
    {
        var source = """
            using Standard;

            public class Program
            {
                public static void Main()
                {
                    Console.WriteLine("hello");
                }
            }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var result = driver.Compile(MakeOptions(filePath));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void UsingStandard_MathAbs_NoErrors()
    {
        var source = """
            using Standard;

            public class Program
            {
                public static void Main()
                {
                    var result = Math.Abs(-1);
                }
            }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var result = driver.Compile(MakeOptions(filePath));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void UsingStandard_ConvertToInt32_NoErrors()
    {
        var source = """
            using Standard;

            public class Program
            {
                public static void Main()
                {
                    var result = Convert.ToInt32("42");
                }
            }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var result = driver.Compile(MakeOptions(filePath));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.Empty(errors);
    }

    // --- using System; no longer resolves Console ---

    [Fact]
    public void UsingSystem_ConsoleWriteLine_ProducesUndefinedDiagnostic()
    {
        // With the AssemblyTypeProvider, `using System` is a valid CLR namespace.
        // Console is provided by the Standard Library (Standard.Console), so
        // `Console.WriteLine` resolves through the stdlib, not through the CLR namespace.
        // This test verifies that `using System` does NOT produce an undefined namespace error.
        var source = """
            using System;

            public class Program
            {
                public static void Main()
                {
                    Console.WriteLine("hello");
                }
            }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var driver = CreateDriver();
        var result = driver.Compile(MakeOptions(filePath));

        var namespaceErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value == "MIST_UNDEFINED_NAMESPACE")
            .ToList();

        // System is a valid CLR namespace — no undefined namespace error.
        Assert.Empty(namespaceErrors);
    }

    // --- Resolved symbols have SymbolOrigin.UserCode (not Backend) ---

    [Fact]
    public void StdlibTypes_HaveUserCodeOrigin()
    {
        var source = """
            using Standard;

            public class Program
            {
                public static void Main()
                {
                    Console.WriteLine("hello");
                    var x = Math.Abs(-1);
                    var y = Convert.ToInt32("42");
                }
            }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        var result = driver.Compile(MakeOptions(filePath));

        // Compilation should succeed
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(errors);

        // The semantic model should have been captured
        Assert.NotNull(capture.CapturedModel);
        var model = capture.CapturedModel!;

        // Console, Math, and Convert should be registered as symbols with UserCode origin
        // (they come from .mist source files in the stdlib, which are treated as UserCode)
        var consoleSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "Console" && s.Kind == SymbolKind.Class);
        Assert.NotNull(consoleSymbol);
        Assert.Equal(SymbolOrigin.UserCode, consoleSymbol!.Origin);

        var mathSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "Math" && s.Kind == SymbolKind.Class);
        Assert.NotNull(mathSymbol);
        Assert.Equal(SymbolOrigin.UserCode, mathSymbol!.Origin);

        var convertSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "Convert" && s.Kind == SymbolKind.Class);
        Assert.NotNull(convertSymbol);
        Assert.Equal(SymbolOrigin.UserCode, convertSymbol!.Origin);
    }
}