// Feature: deep-ownership-types, Property 8: Truth table enforcement

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
/// Property-based tests for truth table enforcement (Property 8).
///
/// Property 8: Truth Table Enforcement
/// For any owned variable in any of the five states (Live, LentReadonly, LentFrozen,
/// LentRef, Moved) and any of the six access kinds (Read, Write, LiveView, Freeze,
/// Borrow, Move), the type checker SHALL permit the action if and only if the truth
/// table entry for that (state, action) pair is ✅.
///
/// Since the input space is finite (5 states × 6 actions = 30 cases), this uses
/// deterministic [Theory] tests covering all pairs exhaustively.
///
/// **Validates: Requirements 11.1, 11.2, 11.3, 11.4, 11.5, 12.1, 12.2, 12.3, 12.7, 12.8**
/// </summary>
public class OwnershipTruthTablePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Truth table (mirrors the one in OwnershipAnalyzer) ──────────────

    /// <summary>
    /// The expected truth table from Requirement 11.
    /// Maps (state, action) → whether the action should be permitted.
    /// </summary>
    private static readonly Dictionary<(VariableOwnershipState, AccessKind), bool> ExpectedTruthTable = new()
    {
        // Live: all permitted
        { (VariableOwnershipState.Live, AccessKind.Read), true },
        { (VariableOwnershipState.Live, AccessKind.Write), true },
        { (VariableOwnershipState.Live, AccessKind.LiveView), true },
        { (VariableOwnershipState.Live, AccessKind.Freeze), true },
        { (VariableOwnershipState.Live, AccessKind.Borrow), true },
        { (VariableOwnershipState.Live, AccessKind.Move), true },

        // LentReadonly: read, write, live view, freeze permitted; borrow, move rejected
        { (VariableOwnershipState.LentReadonly, AccessKind.Read), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.Write), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.LiveView), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.Freeze), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.Borrow), false },
        { (VariableOwnershipState.LentReadonly, AccessKind.Move), false },

        // LentFrozen: read, live view, freeze permitted; write, borrow, move rejected
        { (VariableOwnershipState.LentFrozen, AccessKind.Read), true },
        { (VariableOwnershipState.LentFrozen, AccessKind.Write), false },
        { (VariableOwnershipState.LentFrozen, AccessKind.LiveView), true },
        { (VariableOwnershipState.LentFrozen, AccessKind.Freeze), true },
        { (VariableOwnershipState.LentFrozen, AccessKind.Borrow), false },
        { (VariableOwnershipState.LentFrozen, AccessKind.Move), false },

        // LentRef: nothing permitted
        { (VariableOwnershipState.LentRef, AccessKind.Read), false },
        { (VariableOwnershipState.LentRef, AccessKind.Write), false },
        { (VariableOwnershipState.LentRef, AccessKind.LiveView), false },
        { (VariableOwnershipState.LentRef, AccessKind.Freeze), false },
        { (VariableOwnershipState.LentRef, AccessKind.Borrow), false },
        { (VariableOwnershipState.LentRef, AccessKind.Move), false },

        // Moved: nothing permitted
        { (VariableOwnershipState.Moved, AccessKind.Read), false },
        { (VariableOwnershipState.Moved, AccessKind.Write), false },
        { (VariableOwnershipState.Moved, AccessKind.LiveView), false },
        { (VariableOwnershipState.Moved, AccessKind.Freeze), false },
        { (VariableOwnershipState.Moved, AccessKind.Borrow), false },
        { (VariableOwnershipState.Moved, AccessKind.Move), false },
    };

    // ─── Ownership-related diagnostic IDs ────────────────────────────────

    private static readonly HashSet<string> OwnershipDiagnosticIds = new()
    {
        "MIST_OWN_USE_AFTER_MOVE",
        "MIST_OWN_MUTATION_BLOCKED",
        "MIST_OWN_EXCLUSIVE_VIOLATION",
        "MIST_OWN_INCOMPATIBLE_LEND",
    };

    // ─── Source generation helpers ───────────────────────────────────────

    /// <summary>
    /// Generates the setup code that puts variable 'x' into the specified state.
    /// Returns the setup lines to insert before the action line.
    /// </summary>
    private static string GetStateSetup(VariableOwnershipState state) => state switch
    {
        VariableOwnershipState.Live =>
            // Just declared — already in Live state
            "",

        VariableOwnershipState.LentReadonly =>
            // Lend as readonly ref — puts x into LentReadonly state
            "readonly ref Gadget r = x;\n            ",

        VariableOwnershipState.LentFrozen =>
            // Lend as frozen — puts x into LentFrozen state
            "frozen Gadget f = x;\n            ",

        VariableOwnershipState.LentRef =>
            // Lend as borrowed — puts x into LentRef state
            "borrowed Gadget b = x;\n            ",

        VariableOwnershipState.Moved =>
            // Move to another owned binding — puts x into Moved state
            "owned var y = x;\n            ",

        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>
    /// Generates the action code that attempts the specified access kind on variable 'x'.
    /// Returns the action line to insert after the state setup.
    /// </summary>
    private static string GetActionCode(AccessKind action) => action switch
    {
        AccessKind.Read =>
            "var temp = x.value;",

        AccessKind.Write =>
            "x.value = 99;",

        AccessKind.LiveView =>
            "readonly ref Gadget v = x;",

        AccessKind.Freeze =>
            "frozen Gadget f2 = x;",

        AccessKind.Borrow =>
            "borrowed Gadget b2 = x;",

        AccessKind.Move =>
            "owned var z = x;",

        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>
    /// Builds a complete Mist program that puts variable 'x' into the given state
    /// and then attempts the given action.
    /// </summary>
    private static string BuildTruthTableProgram(VariableOwnershipState state, AccessKind action)
    {
        var stateSetup = GetStateSetup(state);
        var actionCode = GetActionCode(action);

        return $$"""
            owned class Gadget {
                public int value;
                public int GetValue() {
                    return value;
                }
                public void SetValue(int v) {
                    value = v;
                }
            }
            class Program {
                public static void Main() {
                    owned Gadget x = new Gadget();
                    {{stateSetup}}{{actionCode}}
                }
            }
            """;
    }

    // ─── Test data ───────────────────────────────────────────────────────

    /// <summary>
    /// All (state, action) pairs where the truth table says the action is PERMITTED.
    /// </summary>
    public static IEnumerable<object[]> PermittedPairs()
    {
        foreach (var ((state, action), permitted) in ExpectedTruthTable)
        {
            if (permitted)
                yield return new object[] { state, action };
        }
    }

    /// <summary>
    /// All (state, action) pairs where the truth table says the action is REJECTED.
    /// </summary>
    public static IEnumerable<object[]> RejectedPairs()
    {
        foreach (var ((state, action), permitted) in ExpectedTruthTable)
        {
            if (!permitted)
                yield return new object[] { state, action };
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8a: Permitted actions produce no ownership-related errors
    //
    // For each (state, action) pair where the truth table says ✅:
    // compile a program that puts x in that state and attempts the action,
    // verify NO ownership-related error is reported.
    //
    // **Validates: Requirements 11.1, 11.2, 11.3, 12.1, 12.2, 12.3, 12.7, 12.8**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(PermittedPairs))]
    public void PermittedAction_NoOwnershipError(VariableOwnershipState state, AccessKind action)
    {
        var source = BuildTruthTableProgram(state, action);
        var diagnostics = CompileAndGetDiagnostics(source);

        var ownershipErrors = diagnostics
            .Where(d => OwnershipDiagnosticIds.Contains(d.Id.Value))
            .ToList();

        Assert.True(
            ownershipErrors.Count == 0,
            $"Expected no ownership errors for ({state}, {action}) but got:\n" +
            string.Join("\n", ownershipErrors.Select(d => $"  [{d.Id.Value}] {d.Message}")));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8b: Rejected actions produce at least one ownership-related error
    //
    // For each (state, action) pair where the truth table says ❌:
    // compile a program that puts x in that state and attempts the action,
    // verify an ownership-related error IS reported.
    //
    // **Validates: Requirements 11.2, 11.3, 11.4, 11.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RejectedPairs))]
    public void RejectedAction_ProducesOwnershipError(VariableOwnershipState state, AccessKind action)
    {
        var source = BuildTruthTableProgram(state, action);
        var diagnostics = CompileAndGetDiagnostics(source);

        var ownershipErrors = diagnostics
            .Where(d => OwnershipDiagnosticIds.Contains(d.Id.Value))
            .ToList();

        Assert.True(
            ownershipErrors.Count > 0,
            $"Expected at least one ownership error for ({state}, {action}) but got none.\n" +
            $"Source:\n{source}\n" +
            $"All diagnostics: {string.Join(", ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Message}"))}");
    }
}
