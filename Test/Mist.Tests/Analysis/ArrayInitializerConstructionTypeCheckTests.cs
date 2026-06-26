// Feature: definite-assignment
// Task 5.2: Type-check `new Array<T, Size>(fn)`
// Validates: Requirements 5.1, 5.2, 5.5, 5.7

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for type-checking of `new Array&lt;T, Size&gt;(fn)` initializer construction.
///
/// Validates:
/// - Req 5.1: new Array&lt;T, Size&gt;(fn) where fn is index→value function
/// - Req 5.2: Accept any element type T provided fn's result type is assignable to T
/// - Req 5.5: fn must accept a single integer index parameter and return assignable to T
/// - Req 5.7: new Array&lt;T, Size&gt;(fn) is the supported form for non-stampable T
/// </summary>
public sealed class ArrayInitializerConstructionTypeCheckTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "mist_array_ctor_tc_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Any(d => d.Id.Value == id);
    }

    private static bool HasNoSemanticErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        return !diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Stage == PipelineStage.SemanticModelConstruction);
    }

    private static bool HasNoTypeCheckerErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        return !diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Stage == PipelineStage.Analysis);
    }

    private static string FormatErrors(ImmutableArray<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error &&
                        (d.Stage == PipelineStage.SemanticModelConstruction || d.Stage == PipelineStage.Analysis))
            .Select(d => $"  {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // VALID CASES — should type-check without semantic/analysis errors
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// new Array&lt;Label, 3&gt;(i =&gt; new Label(...)) type-checks for a reference type.
    /// Validates: Req 5.1, 5.2, 5.7
    /// </summary>
    [Fact]
    public void ArrayInitializer_ReferenceType_TypeChecksSuccessfully()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            public class Label
            {
                public string Text;
                public Label(string text) { this.Text = text; }
            }

            public class Program
            {
                public static void Main()
                {
                    var labels = new Array<Label, 3>(i => new Label("item"));
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.True(HasNoSemanticErrors(diagnostics),
            $"Expected no semantic errors, but got:\n{FormatErrors(diagnostics)}");
        Assert.True(HasNoTypeCheckerErrors(diagnostics),
            $"Expected no type-checker errors, but got:\n{FormatErrors(diagnostics)}");
    }

    /// <summary>
    /// new Array&lt;int, 5&gt;(i =&gt; i * 2) type-checks for a primitive type.
    /// Validates: Req 5.1, 5.2
    /// </summary>
    [Fact]
    public void ArrayInitializer_PrimitiveType_TypeChecksSuccessfully()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            public class Program
            {
                public static void Main()
                {
                    var ints = new Array<int, 5>(i => i * 2);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.True(HasNoSemanticErrors(diagnostics),
            $"Expected no semantic errors, but got:\n{FormatErrors(diagnostics)}");
        Assert.True(HasNoTypeCheckerErrors(diagnostics),
            $"Expected no type-checker errors, but got:\n{FormatErrors(diagnostics)}");
    }

    /// <summary>
    /// new Array&lt;string, 2&gt;(i =&gt; ...) type-checks for string.
    /// Validates: Req 5.2, 5.7
    /// </summary>
    [Fact]
    public void ArrayInitializer_StringType_TypeChecksSuccessfully()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            public class Program
            {
                public static void Main()
                {
                    var names = new Array<string, 2>(i => "hello");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.True(HasNoSemanticErrors(diagnostics),
            $"Expected no semantic errors, but got:\n{FormatErrors(diagnostics)}");
        Assert.True(HasNoTypeCheckerErrors(diagnostics),
            $"Expected no type-checker errors, but got:\n{FormatErrors(diagnostics)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INVALID CASES — should emit diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Passing a non-function argument (integer literal) is rejected.
    /// Validates: Req 5.5
    /// </summary>
    [Fact]
    public void ArrayInitializer_NonFunctionArgument_EmitsDiagnostic()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            public class Program
            {
                public static void Main()
                {
                    var a = new Array<int, 3>(42);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0011"),
            $"Expected MIST_TC_0011, but got:\n{FormatErrors(diagnostics)}");
    }

    /// <summary>
    /// Passing no arguments to Array constructor is rejected.
    /// Validates: Req 5.5
    /// </summary>
    [Fact]
    public void ArrayInitializer_NoArguments_EmitsDiagnostic()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            public class Program
            {
                public static void Main()
                {
                    var a = new Array<int, 3>();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0011"),
            $"Expected MIST_TC_0011, but got:\n{FormatErrors(diagnostics)}");
    }

    /// <summary>
    /// Passing too many arguments to Array constructor is rejected.
    /// Validates: Req 5.5
    /// </summary>
    [Fact]
    public void ArrayInitializer_TooManyArguments_EmitsDiagnostic()
    {
        const string source = """
            using Standard;
            namespace TestProgram;

            public class Program
            {
                public static void Main()
                {
                    var a = new Array<int, 3>(i => i, i => i);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        Assert.True(HasDiagnostic(diagnostics, "MIST_TC_0011"),
            $"Expected MIST_TC_0011, but got:\n{FormatErrors(diagnostics)}");
    }
}