// Feature: ownership-v2, Property 9: Truth table preserved (regression guard)

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for ownership-v2 Property 9: Truth table preserved.
///
/// Property 9: Truth table preserved (Req 7)
/// For all states s and actions a, v2's permitted(s, a) == the v1 truth table value.
/// The lending state machine is unchanged by the v2 rewrite; this is a regression guard.
///
/// The v1 truth table (the ground truth) is hardcoded here directly from Requirement 7.
/// For each (state, action) pair, a minimal Mist program puts an owned binding into the
/// target state and attempts the action. The v2 analyzer is then run in-process via
/// CompilerDriver.Compile() (no per-iteration process spawn). The program is rejected
/// (an ownership diagnostic fires) if and only if the truth table forbids the action.
///
/// FsCheck quantifies over the finite (state, action) domain (5 states x 6 actions = 30).
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
/// </summary>
public class OwnershipV2TruthTablePreservedPropertyTests
{
    // ─── Shared infrastructure (in-process compile, no dotnet spawn) ─────

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
        _ = Directory.CreateDirectory(tempDir);
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

    // ─── v1 truth table — the immutable ground truth from Requirement 7 ──

    /// <summary>
    /// The expected truth table copied verbatim from Requirement 7. v2 MUST preserve
    /// these exact values. Maps (state, action) → whether the action is permitted.
    /// </summary>
    private static readonly Dictionary<(VariableOwnershipState, AccessKind), bool> V1TruthTable = new()
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

    // ─── Ownership diagnostics that signal a truth-table rejection ───────

    private static readonly HashSet<string> OwnershipDiagnosticIds = new()
    {
        "MIST_OWN_USE_AFTER_MOVE",
        "MIST_OWN_MUTATION_BLOCKED",
        "MIST_OWN_EXCLUSIVE_VIOLATION",
        "MIST_OWN_INCOMPATIBLE_LEND",
    };

    // ─── Source generation helpers ───────────────────────────────────────

    private static string GetStateSetup(VariableOwnershipState state)
    {
        return state switch
        {
            // Just declared — already Live.
            VariableOwnershipState.Live => "",
            // Lend as readonly ref → LentReadonly.
            VariableOwnershipState.LentReadonly => "readonly ref Gadget r = x;\n            ",
            // Lend as frozen → LentFrozen.
            VariableOwnershipState.LentFrozen => "frozen Gadget f = x;\n            ",
            // Lend as borrowed → LentRef.
            VariableOwnershipState.LentRef => "borrowed Gadget b = x;\n            ",
            // Move to another owned binding → Moved.
            VariableOwnershipState.Moved => "owned var y = x;\n            ",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };
    }

    private static string GetActionCode(AccessKind action)
    {
        return action switch
        {
            AccessKind.Read => "var temp = x.value;",
            AccessKind.Write => "x.value = 99;",
            AccessKind.LiveView => "readonly ref Gadget v = x;",
            AccessKind.Freeze => "frozen Gadget f2 = x;",
            AccessKind.Borrow => "borrowed Gadget b2 = x;",
            AccessKind.Move => "owned var z = x;",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    /// <summary>
    /// Builds a Mist program that drives variable 'x' into <paramref name="state"/>
    /// and then attempts <paramref name="action"/>. Gadget is declared 'owned' so the
    /// v2 cooperation contract grants full method access — the truth table is the only
    /// gate being exercised here.
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

    // ─── Generator over the finite (state, action) domain ────────────────

#pragma warning disable CA1034

    public sealed record TruthTableCase(VariableOwnershipState State, AccessKind Action)
    {
        public override string ToString()
        {
            return $"({State}, {Action})";
        }
    }

#pragma warning restore CA1034

    private static Gen<TruthTableCase> GenTruthTableCase()
    {
        var states = new[]
        {
            VariableOwnershipState.Live,
            VariableOwnershipState.LentReadonly,
            VariableOwnershipState.LentFrozen,
            VariableOwnershipState.LentRef,
            VariableOwnershipState.Moved,
        };
        var actions = new[]
        {
            AccessKind.Read,
            AccessKind.Write,
            AccessKind.LiveView,
            AccessKind.Freeze,
            AccessKind.Borrow,
            AccessKind.Move,
        };

        return from state in Gen.Elements(states)
               from action in Gen.Elements(actions)
               select new TruthTableCase(state, action);
    }

    public static Arbitrary<TruthTableCase> ArbitraryTruthTableCase()
    {
        return GenTruthTableCase().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Truth table preserved
    //
    // For all (state, action) pairs, v2 rejects the action (emits an ownership
    // diagnostic) if and only if the v1 truth table forbids it.
    //
    // **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipV2TruthTablePreservedPropertyTests) })]
    public Property TruthTable_Preserved(TruthTableCase testCase)
    {
        var expectedPermitted = V1TruthTable[(testCase.State, testCase.Action)];

        var source = BuildTruthTableProgram(testCase.State, testCase.Action);
        var diagnostics = CompileAndGetDiagnostics(source);

        var hasOwnershipError = diagnostics.Any(d => OwnershipDiagnosticIds.Contains(d.Id.Value));

        // v2 permits the action ⟺ no ownership error ⟺ v1 table permits it.
        var v2Permitted = !hasOwnershipError;
        var matches = v2Permitted == expectedPermitted;

        return matches.ToProperty()
            .Label($"Case: {testCase}")
            .Label($"v1 expected permitted: {expectedPermitted}, v2 permitted: {v2Permitted}")
            .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{source}");
    }
}