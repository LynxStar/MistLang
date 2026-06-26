// Feature: owned-fields, Property 9: Existing truth table preserved (ownership-v2 Req 7)
//
// For all states s and actions a applied to a parent binding's composite state,
// permissions equal the v2 truth table.
//
// The owned-fields spec introduces per-field state tracking, composite state, and
// this-tier inference — but the fundamental truth table (state × action → permitted/denied)
// MUST remain unchanged. This property guards against regressions introduced by the
// owned-fields delta.
//
// In-process semantic-model inspection via CompilerDriver.Compile() — 0 external process spawns.
//
// **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**

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
/// Property-based test: Existing truth table preserved after owned-fields changes.
///
/// Feature: owned-fields, Property 9
/// **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**
///
/// For all states s and actions a, the OwnershipAnalyzer's truth table still maps
/// (state, action) to the same permitted/denied values as the v2 baseline. The
/// owned-fields spec adds per-field state and composite state computations, but
/// the underlying truth table is unchanged.
///
/// The test compiles minimal programs that drive an owned binding into each state
/// and attempt each action, verifying that the compiler emits an ownership
/// diagnostic if and only if the truth table forbids the action.
///
/// Pure in-process — spawns 0 external processes.
/// </summary>
public sealed class OwnedFieldsTruthTablePreservedPropertyTests
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

    // ─── v2 truth table — the immutable ground truth ─────────────────────

    /// <summary>
    /// The expected truth table from ownership-v2. The owned-fields spec MUST preserve
    /// these exact values. Maps (state, action) → whether the action is permitted.
    /// </summary>
    private static readonly Dictionary<(VariableOwnershipState, AccessKind), bool> V2TruthTable = new()
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

    private static string GetStateSetup(VariableOwnershipState state) => state switch
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

    private static string GetActionCode(AccessKind action) => action switch
    {
        AccessKind.Read => "var temp = x.value;",
        AccessKind.Write => "x.value = 99;",
        AccessKind.LiveView => "readonly ref Gadget v = x;",
        AccessKind.Freeze => "frozen Gadget f2 = x;",
        AccessKind.Borrow => "borrowed Gadget b2 = x;",
        AccessKind.Move => "owned var z = x;",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>
    /// Builds a Mist program that drives variable 'x' into <paramref name="state"/>
    /// and then attempts <paramref name="action"/>. Uses an owned type so the truth
    /// table is the only gate being exercised (no this-tier blocking, no owned-field
    /// extraction rules).
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

    /// <summary>
    /// A (state, action) pair from the truth table domain.
    /// </summary>
    public sealed record TruthTableCase(VariableOwnershipState State, AccessKind Action)
    {
        public override string ToString() => $"({State}, {Action})";
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

    public static Arbitrary<TruthTableCase> ArbitraryTruthTableCase() =>
        GenTruthTableCase().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Existing truth table preserved
    //
    // For all (state, action) pairs, the owned-fields analyzer rejects the
    // action (emits an ownership diagnostic) if and only if the v2 truth
    // table forbids it. The owned-fields changes (per-field state, composite
    // state, this-tier, inference) MUST NOT alter the fundamental truth table.
    //
    // **Validates: Requirements 16.9, 16.10, 16.11, 16.12, 16.13, 16.14**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnedFieldsTruthTablePreservedPropertyTests) })]
    public Property TruthTable_PreservedAfterOwnedFieldsChanges(TruthTableCase testCase)
    {
        var expectedPermitted = V2TruthTable[(testCase.State, testCase.Action)];

        var source = BuildTruthTableProgram(testCase.State, testCase.Action);
        var diagnostics = CompileAndGetDiagnostics(source);

        var hasOwnershipError = diagnostics.Any(d => OwnershipDiagnosticIds.Contains(d.Id.Value));

        // The action is permitted ⟺ no ownership error ⟺ truth table permits it.
        var actualPermitted = !hasOwnershipError;
        var matches = actualPermitted == expectedPermitted;

        return matches.ToProperty()
            .Label($"Case: {testCase}")
            .Label($"Expected permitted: {expectedPermitted}, Actual permitted: {actualPermitted}")
            .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]")
            .Label($"Source:\n{source}");
    }
}