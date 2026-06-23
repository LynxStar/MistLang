// Feature: abstract-type-unification, Task 1.2 — error recovery for removed INTERFACE and TRAIT tokens.
//
// When the parser encounters 'interface' or 'trait' at a declaration position,
// it parses the body for maximum diagnostic coverage but the SemanticModelBuilder
// emits a targeted error diagnostic and does NOT register the type in the semantic model.
//
// These tests verify:
//   - The exact error message text (Requirements 2.2, 3.2)
//   - The diagnostic ID (MIST_REMOVED_INTERFACE, MIST_REMOVED_TRAIT)
//   - That the declaration is NOT registered in the semantic model
//   - That subsequent declarations ARE still parsed and registered (error recovery)
//   - That multiple removed declarations all produce separate diagnostics
//
// In-process only: 0 external process spawns (test-process-spawning policy).

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests asserting that removed <c>interface</c> and <c>trait</c> keywords produce
/// targeted error diagnostics with migration guidance, while allowing subsequent
/// declarations to be parsed and registered normally.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 3.1, 3.2, 3.5**
/// </summary>
public sealed class RemovedKeywordErrorRecoveryTests
{
    private const string RemovedInterfaceId = "MIST_REMOVED_INTERFACE";
    private const string RemovedTraitId = "MIST_REMOVED_TRAIT";

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static CompilerResult CompileSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_removed_kw_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
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

            return SharedDriver.Compile(options);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Interface keyword removal (Requirements 2.1, 2.2, 2.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The 'interface' keyword produces the exact error message specified in Requirement 2.2.
    /// </summary>
    [Fact]
    public void InterfaceKeyword_ProducesExactErrorMessage()
    {
        const string source = """
            namespace TestProgram;

            interface IFoo
            {
                string Name();
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedInterfaceId);

        Assert.NotNull(diagnostic);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic!.Severity);
        Assert.Equal(
            "The 'interface' keyword has been removed. Use 'abstract' instead.",
            diagnostic.Message);
    }

    /// <summary>
    /// The 'interface' keyword error allows subsequent declarations to be parsed
    /// and registered normally (error recovery — maximum diagnostic coverage).
    /// </summary>
    [Fact]
    public void InterfaceKeyword_SubsequentDeclarationsStillRegistered()
    {
        const string source = """
            namespace TestProgram;

            interface IFoo
            {
                string Name();
            }

            class MyClass
            {
                public string Hello()
                {
                    return "hello";
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new MyClass();
                }
            }
            """;

        var result = CompileSource(source);

        // Interface error is present
        var interfaceDiag = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedInterfaceId);
        Assert.NotNull(interfaceDiag);

        // MyClass and Program are registered (no "undefined type" errors for them)
        var undefinedTypeErrors = result.Diagnostics
            .Where(d => d.Message.Contains("MyClass") && d.Message.Contains("not defined"))
            .ToList();
        Assert.Empty(undefinedTypeErrors);
    }

    /// <summary>
    /// Multiple 'interface' declarations each produce their own diagnostic.
    /// </summary>
    [Fact]
    public void InterfaceKeyword_MultipleDiagnosticsForMultipleDeclarations()
    {
        const string source = """
            namespace TestProgram;

            interface IFirst
            {
                string A();
            }

            interface ISecond
            {
                string B();
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);
        var interfaceDiags = result.Diagnostics
            .Where(d => d.Id.Value == RemovedInterfaceId)
            .ToList();

        Assert.Equal(2, interfaceDiags.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Trait keyword removal (Requirements 3.1, 3.2, 3.5)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The 'trait' keyword produces the exact error message specified in Requirement 3.2.
    /// </summary>
    [Fact]
    public void TraitKeyword_ProducesExactErrorMessage()
    {
        const string source = """
            namespace TestProgram;

            trait TFoo
            {
                string Name();
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedTraitId);

        Assert.NotNull(diagnostic);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic!.Severity);
        Assert.Equal(
            "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance.",
            diagnostic.Message);
    }

    /// <summary>
    /// The 'trait' keyword error allows subsequent declarations to be parsed
    /// and registered normally.
    /// </summary>
    [Fact]
    public void TraitKeyword_SubsequentDeclarationsStillRegistered()
    {
        const string source = """
            namespace TestProgram;

            trait TFoo
            {
                string Name();
            }

            class MyClass
            {
                public string Hello()
                {
                    return "hello";
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new MyClass();
                }
            }
            """;

        var result = CompileSource(source);

        // Trait error is present
        var traitDiag = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedTraitId);
        Assert.NotNull(traitDiag);

        // MyClass and Program are registered
        var undefinedTypeErrors = result.Diagnostics
            .Where(d => d.Message.Contains("MyClass") && d.Message.Contains("not defined"))
            .ToList();
        Assert.Empty(undefinedTypeErrors);
    }

    /// <summary>
    /// Multiple 'trait' declarations each produce their own diagnostic.
    /// </summary>
    [Fact]
    public void TraitKeyword_MultipleDiagnosticsForMultipleDeclarations()
    {
        const string source = """
            namespace TestProgram;

            trait TFirst
            {
                string A();
            }

            trait TSecond
            {
                string B();
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);
        var traitDiags = result.Diagnostics
            .Where(d => d.Id.Value == RemovedTraitId)
            .ToList();

        Assert.Equal(2, traitDiags.Count);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Mixed declarations and namespace-scoped declarations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Both 'interface' and 'trait' in the same file produce both diagnostics.
    /// </summary>
    [Fact]
    public void MixedRemovedKeywords_BothDiagnosticsEmitted()
    {
        const string source = """
            namespace TestProgram;

            interface IFoo
            {
                string Name();
            }

            trait TBar
            {
                string Label();
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var result = CompileSource(source);

        var interfaceDiag = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedInterfaceId);
        var traitDiag = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedTraitId);

        Assert.NotNull(interfaceDiag);
        Assert.NotNull(traitDiag);
    }

    /// <summary>
    /// Removed keywords inside a block namespace also produce diagnostics.
    /// </summary>
    [Fact]
    public void InterfaceInBlockNamespace_ProducesDiagnostic()
    {
        const string source = """
            namespace TestProgram
            {
                interface IFoo
                {
                    string Name();
                }

                class Program
                {
                    public static void Main()
                    {
                    }
                }
            }
            """;

        var result = CompileSource(source);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedInterfaceId);

        Assert.NotNull(diagnostic);
        Assert.Equal(
            "The 'interface' keyword has been removed. Use 'abstract' instead.",
            diagnostic!.Message);
    }

    /// <summary>
    /// Removed 'trait' inside a block namespace produces the diagnostic.
    /// </summary>
    [Fact]
    public void TraitInBlockNamespace_ProducesDiagnostic()
    {
        const string source = """
            namespace TestProgram
            {
                trait TFoo
                {
                    string Name();
                }

                class Program
                {
                    public static void Main()
                    {
                    }
                }
            }
            """;

        var result = CompileSource(source);
        var diagnostic = result.Diagnostics.FirstOrDefault(d => d.Id.Value == RemovedTraitId);

        Assert.NotNull(diagnostic);
        Assert.Equal(
            "The 'trait' keyword has been removed. Use 'abstract' with nominal typing (': AbstractType') or a 'with' block for retroactive conformance.",
            diagnostic!.Message);
    }
}
