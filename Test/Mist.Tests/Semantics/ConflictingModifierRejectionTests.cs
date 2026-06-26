// Feature: drop-semantics, Property 2: Conflicting modifier rejection
// **Validates: Requirements 1.6, 1.7**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Semantics;

/// <summary>
/// Example-based tests for Property 2: Conflicting modifier rejection.
///
/// Feature: drop-semantics, Property 2: Conflicting modifier rejection
/// **Validates: Requirements 1.6, 1.7**
///
/// For any pair of modifiers drawn from <c>{affine, linear, relevant}</c>, and for any
/// combination of <c>{owned + affine}</c>, <c>{owned + linear}</c>, or <c>{owned + relevant}</c>,
/// applying both to a type declaration SHALL produce the <c>MIST_DROP_CONFLICTING_MODIFIERS</c>
/// compile-time error.
///
/// The conflicting set is finite, so this is covered exhaustively by an example-based
/// <see cref="TheoryAttribute"/> rather than a generative property. Compilation runs fully
/// in-process via a per-instance <see cref="CompilerDriver"/> whose analyzer array includes
/// the <see cref="DropAnalyzer"/> (the analyzer that reports the diagnostic), and the
/// resulting <see cref="CompilerResult.Diagnostics"/> are inspected directly.
/// Spawns 0 external processes.
/// </summary>
public sealed class ConflictingModifierRejectionTests : IDisposable
{
    private const string ConflictingModifiersId = "MIST_DROP_CONFLICTING_MODIFIERS";

    private readonly string _outputDir;

    public ConflictingModifierRejectionTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistConflictingModifierPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Compile helper (driver includes DropAnalyzer) ───────────────────

    private ImmutableArray<Mist.Common.Diagnostics.Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new Mist.Semantics.Model.SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(
                new TypeCheckerAnalyzer(),
                new ReadonlyAnalyzer(),
                new OwnershipAnalyzer(),
                new DropAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = driver.Compile(options);
        return result.Diagnostics;
    }

    // ─── Property 2: Conflicting modifier rejection ──────────────────────
    //
    // The complete finite invalid set:
    //   affine linear, affine relevant, linear relevant  (mutually exclusive substructurals)
    //   owned affine, owned linear, owned relevant        (owned + substructural)
    // Each is asserted to produce MIST_DROP_CONFLICTING_MODIFIERS.

    [Theory]
    [InlineData("affine linear")]
    [InlineData("affine relevant")]
    [InlineData("linear relevant")]
    [InlineData("owned affine")]
    [InlineData("owned linear")]
    [InlineData("owned relevant")]
    public void ConflictingModifiers_AreRejected(string modifiers)
    {
        var source = $$"""
            public {{modifiers}} class Sample
            {
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Contains(
            diagnostics,
            d => d.Id.Value == ConflictingModifiersId);
    }

    // ─── Negative sanity checks: valid single modifiers do NOT conflict ──

    [Theory]
    [InlineData("affine")]
    [InlineData("linear")]
    [InlineData("relevant")]
    [InlineData("owned")]
    public void SingleModifier_DoesNotConflict(string modifier)
    {
        var source = $$"""
            public {{modifier}} class Sample
            {
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id.Value == ConflictingModifiersId);
    }
}