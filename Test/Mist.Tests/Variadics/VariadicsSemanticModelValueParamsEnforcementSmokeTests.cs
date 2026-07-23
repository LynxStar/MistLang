// Feature: variadics - Smoke test for the classic (legacy Mist.Semantics) enforcement of
// one value-side `params` parameter per value-parameter list (Task 10.2).
// Requirements: 7.8, 7.9.
//
// Task 10.2 completes, over the LEGACY Mist.Semantics model (the classic, emitting
// pipeline), the value-side counterpart of the type-parameter-list rule from task 9.2:
//   * MIST_VAR_0001 when a value-parameter list declares more than one value-side `params`
//     parameter (7.9), and
//   * a single value-side `params` alongside explicit plain `(TArgs...)` tuple parameters
//     is valid -- the sanctioned way to write an additional pack (7.8).
//
// Both a Value_Pack (`params TArgs args`, target tuple `(TArgs...)`) and a Value_Level_Params
// (`params T[] xs`, target array) count as a value-side `params` parameter, so the count is
// driven by the `params` modifier on the parameter node, not by the resolved parameter type
// or call convention. The rule lives in SemanticModelBuilder.RegisterParameters (the
// canonical point for parameter-related diagnostics) and mirrors the type-parameter side in
// SemanticModelBuilder.ExtractTypeParameters and the ProgramModel track's ParameterBuilder.
//
// This drives SemanticModelBuilder entirely in-process (ZERO external process spawns: no
// dotnet run, no compiled DLL). Because the count is purely syntactic (the `params`
// modifier), the check is independent of stdlib and downstream type resolution; only the
// legacy model construction stage emits MIST_VAR_0001.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Smoke tests asserting that the classic <see cref="SemanticModelBuilder"/> reports
/// <c>MIST_VAR_0001</c> when a value-parameter list declares more than one value-side
/// <c>params</c> parameter (Requirement 7.9) and does not report it when a single
/// value-side <c>params</c> is combined with explicit plain <c>(TArgs...)</c> tuple
/// parameters (Requirement 7.8).
/// </summary>
public sealed class VariadicsSemanticModelValueParamsEnforcementSmokeTests
{
    private const string MultipleParamsPacksId = "MIST_VAR_0001";

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 7.9: more than one value-side `params` -> MIST_VAR_0001
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TwoValuePacks_ReportMultipleParamsPacks_OverLegacyModel()
    {
        // Two value packs (both tuple targets). The type-parameter list has exactly one
        // `params` pack, so any MIST_VAR_0001 here is attributable to the value-parameter
        // list -- and it fires (7.9).
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, params TArgs b) { }\n"
            + "}");

        // Reported exactly once, on the second declaration (not per surplus pack).
        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));

        var diagnostic = diagnostics.Single(d => d.Id.Value == MultipleParamsPacksId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        // The message names the list kind being validated (mirrors the type-parameter side).
        Assert.Contains("value-parameter", diagnostic.Message);
    }

    [Fact]
    public void ThreeValuePacks_ReportExactlyOneMultipleParamsPacks_OverLegacyModel()
    {
        // Three value packs still produce a single diagnostic (on the second declaration).
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, params TArgs b, params TArgs c) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void MixOfValuePackAndValueLevelParams_ReportMultipleParamsPacks_OverLegacyModel()
    {
        // A Value_Pack (`params TArgs`) plus a Value_Level_Params (`params string[]`): both
        // count as a value-side `params`, so the mix violates the rule (7.9). Counting is
        // driven by the `params` modifier on the node, so this holds regardless of whether
        // the array target yet carries the Flattened_Call_Convention.
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, params string[] b) { }\n"
            + "}");

        Assert.Equal(1, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void TwoValuePacksOnConstructor_ReportMultipleParamsPacks_OverLegacyModel()
    {
        // Constructors resolve their value-parameter list through the same
        // RegisterParameters pass, so the rule is enforced there too.
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
    public void SingleValuePackPlusExplicitTuple_DoesNotReport_OverLegacyModel()
    {
        // The additional pack is written as an explicit plain `(TArgs...)` tuple parameter
        // (no `params`), which does not count -- so no violation (7.8).
        var diagnostics = BuildDiagnostics(
            "public class Bundle<params TArgs>\n"
            + "{\n"
            + "    public void M(params TArgs a, (TArgs...) b) { }\n"
            + "}");

        Assert.Equal(0, CountDiagnostics(diagnostics, MultipleParamsPacksId));
    }

    [Fact]
    public void SingleValuePackAmongFixedParameters_DoesNotReport_OverLegacyModel()
    {
        // A single value pack surrounded by fixed scalar parameters is well-formed.
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
    /// Parses <paramref name="source"/> and builds the legacy semantic model entirely
    /// in-process (no stdlib, no analyzers, no emit, zero process spawns), returning every
    /// construction-stage diagnostic. The at-most-one-value-side-<c>params</c> rule is
    /// decided during model construction (in <c>RegisterParameters</c>), so inspecting the
    /// construction diagnostics is sufficient for this smoke check.
    /// </summary>
    private static ImmutableArray<Diagnostic> BuildDiagnostics(string source)
    {
        var parse = VariadicsCompilerFixture.Parse(source);
        Assert.NotNull(parse.Root);
        Assert.True(
            VariadicsCompilerFixture.ParsedCleanly(parse),
            "Smoke-test source is expected to parse without lex/parse errors.");

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(new SourceText("test.mist", source))));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();

        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parse.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);

        Assert.NotNull(model);
        return diagnostics.ToImmutableArray();
    }

    private static int CountDiagnostics(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Count(d => d.Id.Value == id);
    }
}