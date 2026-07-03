// Feature: target-typed-new, Task 5.2: Unit tests for type checker diagnostics
// **Validates: Requirements 4.3, 4.4, 4.5, 4.6, 5.1, 5.2**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for type checker diagnostics related to target-typed new expressions.
///
/// Covers:
/// - MIST_TARGET_NEW_WITH_VAR: <c>var x = new()</c> (Requirement 5.1)
/// - MIST_TARGET_NEW_NO_CONTEXT: <c>new()</c> in expression statement (Requirement 5.2)
/// - MIST_TARGET_NEW_ABSTRACT: abstract type target type (Requirement 4.4)
/// - MIST_ABSTRACT_INSTANTIATION: abstract class target (Requirement 4.6)
/// - Constructor argument count mismatch (Requirement 4.3)
/// </summary>
public sealed class TargetTypedNewDiagnosticTests : IDisposable
{
    private readonly string outputDir;

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    public TargetTypedNewDiagnosticTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistTargetNewDiag_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source, bool withFrameworkReferences = false)
    {
        var filePath = Path.Combine(outputDir, "Test_" + Guid.NewGuid().ToString("N") + ".mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false,
            FrameworkReferences: withFrameworkReferences
                ? ImmutableArray.Create(new FrameworkReference("Microsoft.NETCore.App"))
                : default);

        var result = SharedDriver.Compile(options);
        return result.Diagnostics;
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TARGET_NEW_WITH_VAR — var x = new() (Requirement 5.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VarWithTargetTypedNew_EmitsTargetNewWithVarDiagnostic()
    {
        var source = """
            class Program
            {
                void Run()
                {
                    var x = new();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var targetDiags = WithId(diagnostics, "MIST_TARGET_NEW_WITH_VAR");

        _ = Assert.Single(targetDiags);
        Assert.Equal(DiagnosticSeverity.Error, targetDiags[0].Severity);
        Assert.Contains("var", targetDiags[0].Message);
    }

    [Fact]
    public void VarWithTargetTypedNewWithArgs_EmitsTargetNewWithVarDiagnostic()
    {
        var source = """
            class Program
            {
                void Run()
                {
                    var x = new(1, 2);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var targetDiags = WithId(diagnostics, "MIST_TARGET_NEW_WITH_VAR");

        _ = Assert.Single(targetDiags);
        Assert.Equal(DiagnosticSeverity.Error, targetDiags[0].Severity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TARGET_NEW_NO_CONTEXT — new() with no target type (Requirement 5.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExpressionStatement_TargetTypedNew_EmitsNoContextDiagnostic()
    {
        var source = """
            class Program
            {
                void Run()
                {
                    new();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var targetDiags = WithId(diagnostics, "MIST_TARGET_NEW_NO_CONTEXT");

        _ = Assert.Single(targetDiags);
        Assert.Equal(DiagnosticSeverity.Error, targetDiags[0].Severity);
        Assert.Contains("cannot be inferred", targetDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_TARGET_NEW_ABSTRACT — abstract type target (Requirement 4.4)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractTarget_EmitsTargetNewAbstractDiagnostic()
    {
        var source = """
            abstract IFoo
            {
                void DoThing();
            }

            class Program
            {
                void Run()
                {
                    IFoo x = new();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var targetDiags = WithId(diagnostics, "MIST_TARGET_NEW_ABSTRACT");

        _ = Assert.Single(targetDiags);
        Assert.Equal(DiagnosticSeverity.Error, targetDiags[0].Severity);
        Assert.Contains("IFoo", targetDiags[0].Message);
        Assert.Contains("abstract type", targetDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_ABSTRACT_INSTANTIATION — abstract class target (Requirement 4.6)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractClassTarget_EmitsAbstractInstantiationDiagnostic()
    {
        // System.IO.Stream is abstract in .NET — use it via framework reference.
        var source = """
            using System.IO;

            class Program
            {
                void Run()
                {
                    Stream s = new();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source, withFrameworkReferences: true);
        var targetDiags = WithId(diagnostics, "MIST_ABSTRACT_INSTANTIATION");

        _ = Assert.Single(targetDiags);
        Assert.Equal(DiagnosticSeverity.Error, targetDiags[0].Severity);
        Assert.Contains("Stream", targetDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor argument count mismatch (Requirement 4.3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstructorArgMismatch_EmitsError()
    {
        var source = """
            class Point
            {
                public int X;
                public int Y;

                public Point(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }

            class Program
            {
                void Run()
                {
                    Point p = new(1);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Should get either MIST_TC_0011 (no matching constructor) or MIST_TC_0031 (no overload matches).
        var hasCtorError = diagnostics.Any(d =>
            d.Id.Value is "MIST_TC_0011" or "MIST_TC_0013" or "MIST_TC_0031");
        Assert.True(hasCtorError,
            $"Expected MIST_TC_0011, MIST_TC_0013, or MIST_TC_0031. Got: [{string.Join(", ", diagnostics.Select(d => d.Id.Value + ": " + d.Message))}]");
    }

    [Fact]
    public void ConstructorTooManyArgs_EmitsError()
    {
        var source = """
            class Simple
            {
            }

            class Program
            {
                void Run()
                {
                    Simple s = new(1, 2, 3);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var hasCtorError = diagnostics.Any(d =>
            d.Id.Value is "MIST_TC_0011" or "MIST_TC_0013" or "MIST_TC_0031");
        Assert.True(hasCtorError,
            $"Expected MIST_TC_0011, MIST_TC_0013, or MIST_TC_0031. Got: [{string.Join(", ", diagnostics.Select(d => d.Id.Value + ": " + d.Message))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Positive case — valid target-typed new should NOT emit these diagnostics
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidTargetTypedNew_DoesNotEmitTargetNewDiagnostics()
    {
        var source = """
            class Point
            {
                public int X;
                public int Y;

                public Point(int x, int y)
                {
                    X = x;
                    Y = y;
                }
            }

            class Program
            {
                void Run()
                {
                    Point p = new(1, 2);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Empty(WithId(diagnostics, "MIST_TARGET_NEW_WITH_VAR"));
        Assert.Empty(WithId(diagnostics, "MIST_TARGET_NEW_NO_CONTEXT"));
        Assert.Empty(WithId(diagnostics, "MIST_TARGET_NEW_ABSTRACT"));
        Assert.Empty(WithId(diagnostics, "MIST_ABSTRACT_INSTANTIATION"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0011"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0013"));
        Assert.Empty(WithId(diagnostics, "MIST_TC_0031"));
    }
}