// Feature: variadics, task 23.1: classic type-checker rejection test for the
// Requirement 21 Negative Proof Code.
//
// Requirements: 21.1, 21.2, 21.3
//
// The Negative Proof Code declares ForwardSumBad<params TArgs>(params TArgs args) which
// spreads `args...` into Sum3(int, int, int). Because `args` is a symbolic pack (TArgs...),
// the spread cannot be proven well-typed for every TArgs (it would fail for
// TArgs = [string]). The type checker MUST reject the spread with MIST_VAR_0011.
//
// This test:
//   - Compiles the Negative Proof Code VERBATIM through the shared classic CompilerDriver
//     (VariadicsCompilerFixture.Compile) in-process.
//   - Asserts compilation FAILS with at least one MIST_VAR_0011 diagnostic.
//   - Fails (via xunit Assert) if the fixture compiles successfully without MIST_VAR_0011.
//
// The fixture is NOT part of the runnable sample at samples/Variadics/Program.mist.
// ZERO external process spawns.

using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 23.1: locked negative proof for Requirement 21 (spread destination enforcement).
/// The Negative Proof Code MUST NOT compile; it must emit <c>MIST_VAR_0011</c> on the
/// <c>Sum3(args...)</c> spread. If it compiles successfully, this test fails.
/// </summary>
public sealed class VariadicsNegativeProofTests
{
    private const string SpreadDestinationMismatchId = "MIST_VAR_0011";

    /// <summary>
    /// The Requirement 21 Negative Proof Code, verbatim.
    /// </summary>
    private const string NegativeProofCode = """
        using Standard;

        namespace Variadics;

        public class NegativeProofs
        {
            // NEGATIVE PROOF: this MUST NOT compile. args is a symbolic pack (TArgs...), so
            // Sum3(args...) cannot be proven well-typed for every TArgs -- it would fail for
            // TArgs = [string]. This is ordinary open-generic checking: the same reason
            // Double(value) is rejected in Foo<TValue>. The type checker MUST reject the spread
            // with MIST_VAR_0011.
            public static int ForwardSumBad<params TArgs>(params TArgs args)
            {
                return Sum3(args...);
            }

            public static int Sum3(int a, int b, int c)
            {
                return a + b + c;
            }
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 21.1: compilation MUST fail with MIST_VAR_0011
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NegativeProof_ForwardSumBad_EmitsMistVar0011()
    {
        var result = VariadicsCompilerFixture.Compile(
            NegativeProofCode, ProjectOutputType.Library);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        // The code must NOT compile successfully (must have errors).
        Assert.NotEmpty(errors);

        // At least one diagnostic must be MIST_VAR_0011 (spread destination mismatch).
        Assert.Contains(
            result.Diagnostics,
            d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 21.3: if the fixture compiles successfully, the test fails
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NegativeProof_MustNotCompileCleanly()
    {
        var result = VariadicsCompilerFixture.Compile(
            NegativeProofCode, ProjectOutputType.Library);

        var hasSpreadMismatch = result.Diagnostics.Any(
            d => d.Id.Value == SpreadDestinationMismatchId);

        // This assertion's failure message explicitly states the gate condition:
        // the type checker is not enforcing destination typing on spread.
        Assert.True(
            hasSpreadMismatch,
            "NEGATIVE PROOF FAILURE: The Negative Proof Code compiled without emitting "
            + "MIST_VAR_0011. This means the type checker is NOT enforcing destination "
            + "typing on spread (Requirement 21). The code Sum3(args...) must be rejected "
            + "because TArgs is a symbolic pack that cannot be proven well-typed for every "
            + "instantiation.");
    }
}