// Feature: drop-semantics, Property 2: Conflicting modifier rejection
// **Validates: Requirements 1.6, 1.7**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Example-based tests for the conflicting-modifier rejection rule.
///
/// Feature: drop-semantics, Property 2: Conflicting modifier rejection
/// **Validates: Requirements 1.6, 1.7**
///
/// For any pair of modifiers drawn from <c>{affine, linear, relevant}</c>, and for any
/// combination of <c>{owned + relevant}</c>, <c>{owned + affine}</c>, or <c>{owned + linear}</c>,
/// applying both to a type declaration SHALL produce a compile-time error
/// (<c>MIST_DROP_CONFLICTING_MODIFIERS</c>).
///
/// The invalid combination set is finite, so the property is exercised exhaustively with
/// example-based <see cref="TheoryAttribute"/>/<see cref="InlineDataAttribute"/> cases over
/// both <c>class</c> and <c>struct</c> declarations. Compilation runs entirely in-process via
/// a shared <see cref="CompilerDriver"/>; it spawns 0 external processes.
/// </summary>
public sealed class ConflictingModifierRejectionTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_conflicting_modifiers_" + Guid.NewGuid().ToString("N"));
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

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string BuildSource(string modifiers, string kind) => $$"""
        {{modifiers}} {{kind}} Sample
        {
        }
        class Program
        {
            public static void Main()
            {
            }
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Conflicting modifier rejection
    //
    // Each invalid modifier combination, applied to a class or struct, SHALL emit
    // exactly the MIST_DROP_CONFLICTING_MODIFIERS diagnostic.
    //
    // Invalid set:
    //   affine linear, affine relevant, linear relevant   (mutually-exclusive substructural)
    //   owned affine, owned linear, owned relevant         (owned + substructural)
    //
    // **Validates: Requirements 1.6, 1.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("affine linear", "class")]
    [InlineData("affine relevant", "class")]
    [InlineData("linear relevant", "class")]
    [InlineData("owned affine", "class")]
    [InlineData("owned linear", "class")]
    [InlineData("owned relevant", "class")]
    [InlineData("affine linear", "struct")]
    [InlineData("affine relevant", "struct")]
    [InlineData("linear relevant", "struct")]
    [InlineData("owned affine", "struct")]
    [InlineData("owned linear", "struct")]
    [InlineData("owned relevant", "struct")]
    public void ConflictingModifiers_AreRejected(string modifiers, string kind)
    {
        var source = BuildSource(modifiers, kind);

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.Contains(
            diagnostics,
            d => d.Id.Value == "MIST_DROP_CONFLICTING_MODIFIERS");
    }
}
