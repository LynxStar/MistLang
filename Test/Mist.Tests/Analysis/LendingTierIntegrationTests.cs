// Feature: ownership-v2, Task 8.5 — unit tests for lending tier integration.
//
// The lending / truth-table machinery is retained verbatim from v1 in
// OwnershipAnalyzer.cs (task 8.1). These tests assert the Requirement 9 acceptance
// criteria end-to-end through the in-process SharedDriver: a Mist program is compiled
// and the resulting diagnostics are inspected for the specific lending diagnostic ids.
//
// Coverage (Requirement 9):
//   9.1 owned binding lent as 'borrowed'      -> owner -> LentRef     (no access)
//   9.2 owned binding lent as 'frozen'        -> owner -> LentFrozen  (no mutation)
//   9.3 owned binding lent as 'readonly ref'  -> owner -> LentReadonly (no borrow/move)
//   9.4 owned binding passed to owned param   -> source marked Moved  (use-after-move)
//   9.5 unrestricted -> readonly ref param    -> permitted
//   9.6 unrestricted -> borrowed/frozen param -> MIST_OWN_UNRESTRICTED_TO_REF
//   9.7 outstanding borrowed + second lend    -> MIST_OWN_EXCLUSIVE_VIOLATION
//   9.8 outstanding frozen: +frozen/+readonly permitted; +borrowed / move rejected
//
// These are EXPLICIT [Fact]/[Theory] cases that complement the existing v1 property
// tests (OwnershipLendingPropertyTests, OwnershipExclusivityPropertyTests,
// OwnershipFrozenBlockingPropertyTests, UnrestrictedBindingRestrictionPropertyTests)
// without duplicating their randomized matrix coverage. They pin the Requirement 9
// integration behaviour with named, deterministic scenarios.
//
// In-process only: 0 external process spawns (test-process-spawning policy).

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for ownership-v2 lending tier integration (task 8.5). Verifies that an
/// owned binding integrates with the borrowed / frozen / readonly-ref lending tiers
/// per Requirement 9: lends transition the owner to the correct state, the truth table
/// gates access while a loan is outstanding, unrestricted bindings cannot guarantee
/// exclusive access, a second lend over an outstanding borrowed loan is rejected, and a
/// frozen loan permits additional frozen / readonly-ref loans while rejecting a borrowed
/// loan or a move.
///
/// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8**
/// </summary>
public sealed class LendingTierIntegrationTests
{
    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private const string ExclusiveViolationId = "MIST_OWN_EXCLUSIVE_VIOLATION";
    private const string MutationBlockedId = "MIST_OWN_MUTATION_BLOCKED";
    private const string UnrestrictedToRefId = "MIST_OWN_UNRESTRICTED_TO_REF";
    private const string UseAfterMoveId = "MIST_OWN_USE_AFTER_MOVE";

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_lend_tier_" + Guid.NewGuid().ToString("N"));
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

            return SharedDriver.Compile(options).Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.Any(d => d.Id.Value == id);

    private static string Render(ImmutableArray<Diagnostic> diagnostics) =>
        $"[{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]";

    private static void AssertHas(ImmutableArray<Diagnostic> diagnostics, string id) =>
        Assert.True(HasDiagnostic(diagnostics, id), $"Expected {id} but got: {Render(diagnostics)}");

    private static void AssertNotHas(ImmutableArray<Diagnostic> diagnostics, string id) =>
        Assert.False(HasDiagnostic(diagnostics, id), $"Did not expect {id} but got: {Render(diagnostics)}");

    /// <summary>An owned class with a readonly getter and a mutable setter, reused by the scenarios.</summary>
    private const string WidgetDecl = """
        owned class Widget
        {
            public int Value;
            public int GetValue()
            {
                return Value;
            }
            public void SetValue(int v)
            {
                this.Value = v;
            }
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // 9.1 — owned binding lent as 'borrowed' transitions the owner to LentRef.
    //       While the borrow is outstanding the owner has NO access (read or write):
    //       both are rejected with MIST_OWN_EXCLUSIVE_VIOLATION.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_LentAsBorrowed_OwnerReadDuringLoan_RejectedExclusive()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    borrowed Widget b = w;
                    w.GetValue();
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), ExclusiveViolationId);
    }

    [Fact]
    public void OwnedBinding_LentAsBorrowed_OwnerWriteDuringLoan_RejectedExclusive()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    borrowed Widget b = w;
                    w.SetValue(1);
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), ExclusiveViolationId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9.2 — owned binding lent as 'frozen' transitions the owner to LentFrozen.
    //       Read is still permitted; mutation is blocked (MIST_OWN_MUTATION_BLOCKED).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_LentAsFrozen_OwnerWriteDuringLoan_RejectedMutationBlocked()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    frozen Widget f = w;
                    w.SetValue(1);
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), MutationBlockedId);
    }

    [Fact]
    public void OwnedBinding_LentAsFrozen_OwnerReadDuringLoan_Permitted()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    frozen Widget f = w;
                    w.GetValue();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNotHas(diagnostics, MutationBlockedId);
        AssertNotHas(diagnostics, ExclusiveViolationId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9.3 — owned binding lent as 'readonly ref' transitions the owner to
    //       LentReadonly. Read AND write are still permitted; a borrow or a move
    //       is rejected (MIST_OWN_EXCLUSIVE_VIOLATION).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_LentAsReadonlyRef_OwnerWriteDuringLoan_Permitted()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    readonly ref Widget r = w;
                    w.SetValue(1);
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNotHas(diagnostics, MutationBlockedId);
        AssertNotHas(diagnostics, ExclusiveViolationId);
    }

    [Fact]
    public void OwnedBinding_LentAsReadonlyRef_BorrowDuringLoan_RejectedExclusive()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    readonly ref Widget r = w;
                    borrowed Widget b = w;
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), ExclusiveViolationId);
    }

    [Fact]
    public void OwnedBinding_LentAsReadonlyRef_MoveDuringLoan_RejectedExclusive()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    readonly ref Widget r = w;
                    owned Widget moved = w;
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), ExclusiveViolationId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9.4 — owned binding passed to an 'owned' parameter is moved; the source
    //       binding is marked Moved, so a subsequent access is a use-after-move.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_PassedToOwnedParam_MarksSourceMoved_AccessAfterIsUseAfterMove()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Consume(owned Widget param)
                {
                }
                public static void Main()
                {
                    owned Widget w = new Widget();
                    Consume(w);
                    w.GetValue();
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), UseAfterMoveId);
    }

    [Fact]
    public void OwnedBinding_PassedToOwnedParam_NoSubsequentAccess_NoUseAfterMove()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Consume(owned Widget param)
                {
                }
                public static void Main()
                {
                    owned Widget w = new Widget();
                    Consume(w);
                }
            }
            """;

        AssertNotHas(CompileAndGetDiagnostics(source), UseAfterMoveId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9.5 — an unrestricted binding passed to a 'readonly ref' parameter is permitted.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnrestrictedBinding_PassedToReadonlyRefParam_Permitted()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Inspect(readonly ref Widget param)
                {
                }
                public static void Main()
                {
                    var w = new Widget();
                    Inspect(w);
                }
            }
            """;

        AssertNotHas(CompileAndGetDiagnostics(source), UnrestrictedToRefId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9.6 — an unrestricted binding passed to a 'borrowed' or 'frozen' parameter is
    //       rejected (MIST_OWN_UNRESTRICTED_TO_REF): it cannot guarantee exclusive access.
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("borrowed")]
    [InlineData("frozen")]
    public void UnrestrictedBinding_PassedToBorrowedOrFrozenParam_Rejected(string paramTier)
    {
        var source = WidgetDecl + $$"""

            class Program
            {
                public static void Take({{paramTier}} Widget param)
                {
                }
                public static void Main()
                {
                    var w = new Widget();
                    Take(w);
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), UnrestrictedToRefId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9.7 — double-lend: while a 'borrowed' loan is outstanding (owner in LentRef),
    //       a second lend of ANY tier is rejected (MIST_OWN_EXCLUSIVE_VIOLATION).
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("borrowed")]
    [InlineData("frozen")]
    [InlineData("readonly ref")]
    public void OwnedBinding_OutstandingBorrowed_SecondLendAnyTier_RejectedExclusive(string secondTier)
    {
        var source = WidgetDecl + $$"""

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    borrowed Widget first = w;
                    {{secondTier}} Widget second = w;
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), ExclusiveViolationId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 9.8 — while a 'frozen' loan is outstanding (owner in LentFrozen): additional
    //       'frozen' or 'readonly ref' loans are permitted, but a 'borrowed' loan is
    //       rejected (MIST_OWN_EXCLUSIVE_VIOLATION) and a move is rejected
    //       (MIST_OWN_MUTATION_BLOCKED).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_OutstandingFrozen_AdditionalFrozen_Permitted()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    frozen Widget f1 = w;
                    frozen Widget f2 = w;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNotHas(diagnostics, ExclusiveViolationId);
        AssertNotHas(diagnostics, MutationBlockedId);
    }

    [Fact]
    public void OwnedBinding_OutstandingFrozen_AdditionalReadonlyRef_Permitted()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    frozen Widget f = w;
                    readonly ref Widget r = w;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        AssertNotHas(diagnostics, ExclusiveViolationId);
        AssertNotHas(diagnostics, MutationBlockedId);
    }

    [Fact]
    public void OwnedBinding_OutstandingFrozen_BorrowedLoan_RejectedExclusive()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    frozen Widget f = w;
                    borrowed Widget b = w;
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), ExclusiveViolationId);
    }

    [Fact]
    public void OwnedBinding_OutstandingFrozen_Move_RejectedMutationBlocked()
    {
        var source = WidgetDecl + """

            class Program
            {
                public static void Main()
                {
                    owned Widget w = new Widget();
                    frozen Widget f = w;
                    owned Widget moved = w;
                }
            }
            """;

        AssertHas(CompileAndGetDiagnostics(source), MutationBlockedId);
    }
}
