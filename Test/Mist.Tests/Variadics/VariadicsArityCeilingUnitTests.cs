// Feature: variadics, task 19.6: unit tests for the arity ceiling and ValueTuple nesting
// (classic pipeline).
//
// Covers:
//   - 17-input Func (MIST_VAR_0010 emitted)
//   - Boundary 16-input Func (no MIST_VAR_0010, compiles cleanly)
//   - 8-element ValueTuple nesting (verified via BuildValueTupleTypeName)
//
// Requirements: 16.2, 16.3.
//
// ZERO external process spawns. Uses VariadicsCompilerFixture.Compile (EmitToDisk:false)
// for full-pipeline tests and direct static method calls for VariadicMonomorphizer helpers.

using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Unit tests exercising the backend arity ceiling for <c>System.Func</c> and
/// <c>System.ValueTuple</c> nesting behavior.
/// <para>
/// Requirements: 16.2 (Func/Action over 16 inputs emits MIST_VAR_0010),
/// 16.3 (ValueTuple over 7 elements nests instead of rejecting).
/// </para>
/// </summary>
public sealed class VariadicsArityCeilingUnitTests
{
    /// <summary>
    /// A Func with 17 input parameters (18 type arguments total) exceeds the CLR limit.
    /// The backend MUST emit MIST_VAR_0010.
    /// Requirement 16.2.
    /// </summary>
    [Fact]
    public void Func17Inputs_EmitsMistVar0010()
    {
        // Func<T1, T2, ..., T17, TResult> has 18 type arguments; 17 are inputs.
        // This exceeds the System.Func ceiling of 16 inputs.
        const string source =
            "using Standard;\n"
            + "\n"
            + "namespace CeilingTest;\n"
            + "\n"
            + "public class Consumer\n"
            + "{\n"
            + "    public static int Use(Func<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> f)\n"
            + "    {\n"
            + "        return 0;\n"
            + "    }\n"
            + "}\n";

        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");

        Assert.True(
            VariadicsCompilerFixture.EmittedDiagnostic(result, "MIST_VAR_0010"),
            "Expected MIST_VAR_0010 for a 17-input Func but it was not emitted.\n"
            + $"Diagnostics: {FormatDiagnostics(result)}");
    }

    /// <summary>
    /// A Func with exactly 16 input parameters (17 type arguments total) is at the
    /// boundary and MUST compile cleanly without MIST_VAR_0010.
    /// Requirement 16.2 (the limit is "over 16", so 16 is fine).
    /// </summary>
    [Fact]
    public void Func16Inputs_CompilesWithoutMistVar0010()
    {
        // Func<T1, ..., T16, TResult> has 17 type arguments; 16 are inputs.
        // This is exactly at the ceiling and should emit cleanly.
        const string source =
            "using Standard;\n"
            + "\n"
            + "namespace CeilingTest;\n"
            + "\n"
            + "public class Consumer\n"
            + "{\n"
            + "    public static int Use(Func<int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int> f)\n"
            + "    {\n"
            + "        return 0;\n"
            + "    }\n"
            + "}\n";

        var result = VariadicsCompilerFixture.Compile(source, globalUsings: "Standard");

        Assert.False(
            VariadicsCompilerFixture.EmittedDiagnostic(result, "MIST_VAR_0010"),
            "Expected NO MIST_VAR_0010 for a 16-input Func but it was emitted.\n"
            + $"Diagnostics: {FormatDiagnostics(result)}");

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"Expected zero errors for boundary 16-input Func but got {errors.Count}:\n"
            + $"{FormatDiagnostics(result)}");
    }

    /// <summary>
    /// An 8-element ValueTuple uses nesting: the first 7 elements are flat and the 8th
    /// is wrapped in a nested ValueTuple. The backend represents this by nesting rather
    /// than rejecting with MIST_VAR_0010.
    /// Requirement 16.3.
    /// </summary>
    [Fact]
    public void BuildValueTupleTypeName_Arity8_ProducesNestedForm()
    {
        var result = VariadicMonomorphizer.BuildValueTupleTypeName(8);

        Assert.Equal(
            "System.ValueTuple<T1, T2, T3, T4, T5, T6, T7, System.ValueTuple<T8>>",
            result);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string FormatDiagnostics(Mist.Driver.Contracts.CompilerResult result)
    {
        return string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
    }
}