// Feature: variadics - Smoke check for method-level type-parameter packs (Task 5.5)
// Requirements: 12.1, 12.4, 12.5, 22.1
//
// A minimal in-process smoke test confirming that MethodBuilder threads the
// construction DiagnosticBag into TypeParameterPopulator, so a *method* type-parameter
// list is governed by the same at-most-one-Unbracketed_Pack rule as every other
// declaration kind:
//   * two `params` packs on a method type-parameter list -> MIST_VAR_0001 (12.4, 12.5);
//   * a single `params` pack on a method is fine (12.1);
//   * a method's pack is a slot distinct from, and not constrained by, any pack on the
//     enclosing type, so a variadic method inside a variadic class does not error (12.1);
//   * the rule fires on extension-method type-parameter lists too, since they route
//     through the same MethodBuilder (22.1 -- uniform application).
//
// The full property test for the one-sugared-pack rule (including method lists) is
// Task 5.7, and pack-bearing declaration kinds are Task 12.8. This drives
// ProgramModelBuilder.Build directly: there are ZERO external process spawns.

using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 5.5 smoke tests for the at-most-one-Unbracketed_Pack rule on <em>method</em>
/// type-parameter lists (Requirements 12.1, 12.4, 12.5, 22.1). Each test builds the
/// program model in-process and inspects the <c>MIST_VAR_0001</c> diagnostics collected
/// during construction.
/// </summary>
public sealed class VariadicsMethodPackEnforcementTests : IDisposable
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    private readonly string outputDir;

    public VariadicsMethodPackEnforcementTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsMethodPack_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the temporary build directory.
            }
        }

        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirements 12.4, 12.5: more than one Unbracketed_Pack on a method
    // type-parameter list -> MIST_VAR_0001
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoParamsPacksOnMethodTypeParameterList_EmitMistVar0001()
    {
        var diagnostics = BuildDiagnostics(
            "public class Bundle\n"
            + "{\n"
            + "    public int M<params A, params B>() { return 0; }\n"
            + "}");

        // Reported exactly once, at the second `params` pack.
        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));

        var diagnostic = diagnostics.Single(d => d.Id.Value == MultipleParamsPacksId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        // The list-kind placeholder is shared with the type side (Task 4.2): "type-parameter".
        Assert.Contains("type-parameter", diagnostic.Message);
    }

    [Fact]
    public void ThreeParamsPacksOnMethod_EmitExactlyOneMistVar0001()
    {
        // Reported once (on the second declaration), not once per surplus pack.
        var diagnostics = BuildDiagnostics(
            "public class Bundle\n"
            + "{\n"
            + "    public int M<params A, params B, params C>() { return 0; }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 12.1: a single method pack is fine, and a method pack is
    // independent of any pack on the enclosing type
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleParamsPackOnMethod_DoesNotEmit()
    {
        var diagnostics = BuildDiagnostics(
            "public class Bundle\n"
            + "{\n"
            + "    public int Describe<params TArgs>(params TArgs args) { return 0; }\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void MethodPackInsideVariadicClass_AreIndependentSlots_DoNotError()
    {
        // The enclosing type declares its own `params TArgs`, and the method declares a
        // separate `params TMethod`. Each list has exactly one Unbracketed_Pack, so the
        // method's pack is not conflated with the type's -> no violation (Requirement 12.1).
        var diagnostics = BuildDiagnostics(
            "public class Outer<params TArgs>\n"
            + "{\n"
            + "    public int M<params TMethod>() { return 0; }\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 22.1: the same rule applies uniformly to extension-method
    // type-parameter lists (they route through the same MethodBuilder)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoParamsPacksOnExtensionMethod_EmitMistVar0001()
    {
        var diagnostics = BuildDiagnostics(
            "extension(int self)\n"
            + "{\n"
            + "    public int M<params A, params B>() { return 0; }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <paramref name="source"/> and builds the program model entirely
    /// in-process, returning every diagnostic collected during construction (where
    /// MIST_VAR_0001 is reported).
    /// </summary>
    private ImmutableArray<Diagnostic> BuildDiagnostics(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create("test.mist"),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var model = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(model);
        return buildDiagnostics.ToImmutableArray();
    }

    private static int CountDiagnostics(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Count(d => d.Id.Value == id);
    }
}