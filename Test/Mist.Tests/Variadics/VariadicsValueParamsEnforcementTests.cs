// Feature: variadics - Smoke check for the one-value-side-params-per-list rule (Task 5.2)
// Requirements: 7.8, 7.9
//
// A minimal in-process smoke test confirming that ParameterBuilder enforces
// "at most one value-side `params` parameter per value-parameter list":
//   * two value-side `params` parameters -> MIST_VAR_0001 (7.9);
//   * both a Value_Pack (`params TArgs args`) and a Value_Level_Params (`params T[] xs`)
//     count as a value-side `params` parameter, so a pack+array or array+array pair also
//     violates the rule (7.8) -- the count is driven by the `params` modifier on the
//     parameter node, not by the resolved call convention;
//   * a single `params` plus an explicit plain `(TArgs...)` tuple parameter is fine (7.8);
//   * the rule fires on methods, constructors, and indexers alike (the diagnostics bag is
//     threaded through every value-parameter-list build site).
//
// The full property test for the one-sugared-pack rule is Task 5.7. This drives
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
/// Task 5.2 smoke tests for the at-most-one-value-side-<c>params</c> rule
/// (Requirements 7.8, 7.9). Each test builds the program model in-process and inspects
/// the <c>MIST_VAR_0001</c> diagnostics collected during construction.
/// </summary>
public sealed class VariadicsValueParamsEnforcementTests : IDisposable
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    private readonly string outputDir;

    public VariadicsValueParamsEnforcementTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistVariadicsValueParams_" + Guid.NewGuid().ToString("N"));
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
    // Requirement 7.9: more than one value-side `params` -> MIST_VAR_0001
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoValuePacks_EmitMistVar0001_ForValueParameterList()
    {
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, params TArgs b) { }\n"
            + "}");

        // Exactly one diagnostic, reported once at the second `params` (not per surplus).
        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));

        var diagnostic = diagnostics.Single(d => d.Id.Value == MultipleParamsPacksId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        // The message names the list kind being validated (mirrors the type-parameter side).
        Assert.Contains("value-parameter", diagnostic.Message);
    }

    [Fact]
    public void ThreeValuePacks_EmitExactlyOneMistVar0001()
    {
        // Reported once (on the second declaration), not once per surplus pack.
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, params TArgs b, params TArgs c) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void TwoValueLevelParamsArrays_EmitMistVar0001()
    {
        // Both `params` targets are homogeneous arrays. Counting is driven by the
        // `params` modifier on the node, so this violates the rule regardless of whether
        // the array target yet carries the Flattened_Call_Convention (robust to Task 5.3).
        var diagnostics = BuildDiagnostics(
            "public class Log\n"
            + "{\n"
            + "    public void Write(params string[] a, params int[] b) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void MixOfValuePackAndValueLevelParams_EmitMistVar0001()
    {
        // A Value_Pack plus a Value_Level_Params: both count as value-side `params`.
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, params string[] b) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void TwoValuePacksOnConstructor_EmitMistVar0001()
    {
        // Constructors resolve their value-parameter list through the same delegate
        // resolver, so the rule is enforced there too.
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public Bundle(params TArgs a, params TArgs b) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.8: one `params` plus explicit `(TArgs...)` tuple params is valid
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleValuePackPlusExplicitTuple_DoesNotEmit()
    {
        // The additional pack is written as an explicit plain `(TArgs...)` tuple
        // parameter (no `params`), which does not count -- so no violation.
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, (TArgs...) b) { }\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void SingleValuePack_DoesNotEmit()
    {
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(int number, params TArgs a, bool enabled) { }\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
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