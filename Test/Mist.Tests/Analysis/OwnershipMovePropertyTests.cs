// Feature: ownership-system, Property 4: Move tracking and use-after-move detection

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for move tracking and use-after-move detection.
///
/// Property 4: Move tracking and use-after-move detection
/// For any owned variable that is moved (assigned to another owned variable or passed
/// to an owned parameter), any subsequent access without an intervening re-assignment
/// SHALL produce a use-after-move diagnostic identifying the variable name, the move
/// location, and the invalid access location. In conditional branches, if a variable
/// is moved in one branch, access in a subsequent branch where it was moved SHALL
/// produce an error.
///
/// **Validates: Requirements 7.4, 7.5, 7.9, 10.1, 10.2, 10.3, 10.4, 10.5, 10.7, 16.1**
/// </summary>
public class OwnershipMovePropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(
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

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario where an owned variable is moved and then accessed (use-after-move).
    /// </summary>
    public sealed record UseAfterMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned variable is moved but not accessed afterwards (valid).
    /// </summary>
    public sealed record ValidMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned variable is moved in one branch of a conditional,
    /// and accessed after the conditional (conditional move → use-after-move at join).
    /// </summary>
    public sealed record ConditionalMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned variable is moved inside a loop without re-assignment
    /// (loop move error).
    /// </summary>
    public sealed record LoopMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned variable is moved and then re-assigned before access (valid).
    /// </summary>
    public sealed record ReassignmentScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Resource", "Buffer", "Handle", "Connection", "Stream", "Channel"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "item", "res"];

    private static readonly string[] DestVarNames =
        ["dest", "target", "other", "sink", "receiver", "output"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a program where an owned variable is moved via assignment to another
    /// owned variable, and then accessed (use-after-move).
    /// </summary>
    private static Gen<UseAfterMoveScenario> GenSimpleAssignmentUseAfterMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source = $$"""
                   owned class {{className}} {
                       public int GetValue() {
                           return 1;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           owned {{className}} {{destName}} = {{varName}};
                           {{varName}}.GetValue();
                       }
                   }
                   """
               select new UseAfterMoveScenario(source,
                   $"assign move {varName}->{destName} then access {varName}");
    }

    /// <summary>
    /// Generates a program where an owned variable is moved via re-assignment to
    /// another owned variable, and then accessed (use-after-move).
    /// </summary>
    private static Gen<UseAfterMoveScenario> GenReassignmentMoveUseAfterMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source = $$"""
                   owned class {{className}} {
                       public int GetValue() {
                           return 1;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           owned {{className}} {{destName}} = new {{className}}();
                           {{destName}} = {{varName}};
                           {{varName}}.GetValue();
                       }
                   }
                   """
               select new UseAfterMoveScenario(source,
                   $"reassignment move {varName}->{destName} then access {varName}");
    }

    /// <summary>
    /// Generates a program where an owned variable is moved but never accessed
    /// afterwards (valid — no diagnostic expected).
    /// </summary>
    private static Gen<ValidMoveScenario> GenValidSimpleMove()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source = $$"""
                   owned class {{className}} {
                       public int GetValue() {
                           return 1;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           owned {{className}} {{destName}} = {{varName}};
                           {{destName}}.GetValue();
                       }
                   }
                   """
               select new ValidMoveScenario(source,
                   $"valid move {varName}->{destName}, access only {destName}");
    }

    /// <summary>
    /// Generates a program where an owned variable is moved in one branch of an if
    /// statement, and then accessed after the if (conditional move → use-after-move).
    /// </summary>
    private static Gen<ConditionalMoveScenario> GenConditionalMoveScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source = $$"""
                   owned class {{className}} {
                       public int GetValue() {
                           return 1;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           bool cond = true;
                           if (cond) {
                               owned {{className}} {{destName}} = {{varName}};
                           }
                           {{varName}}.GetValue();
                       }
                   }
                   """
               select new ConditionalMoveScenario(source,
                   $"conditional move {varName} in if-branch, access after if");
    }

    /// <summary>
    /// Generates a program where an owned variable is moved inside a while loop
    /// via assignment without re-assignment (loop move error).
    /// </summary>
    private static Gen<LoopMoveScenario> GenWhileLoopMoveScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source = $$"""
                   owned class {{className}} {
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           int i = 0;
                           while (i < 3) {
                               owned {{className}} {{destName}} = {{varName}};
                               i = i + 1;
                           }
                       }
                   }
                   """
               select new LoopMoveScenario(source,
                   $"loop move {varName}->{destName} in while without re-assignment");
    }

    /// <summary>
    /// Generates a program where an owned variable is moved and then re-assigned
    /// before being accessed again (valid — re-assignment restores liveness).
    /// </summary>
    private static Gen<ReassignmentScenario> GenReassignmentAfterMoveScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source = $$"""
                   owned class {{className}} {
                       public int GetValue() {
                           return 1;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           owned {{className}} {{destName}} = {{varName}};
                           {{varName}} = new {{className}}();
                           {{varName}}.GetValue();
                       }
                   }
                   """
               select new ReassignmentScenario(source,
                   $"move {varName}->{destName} then re-assign {varName} then access");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<UseAfterMoveScenario> ArbitraryUseAfterMoveScenario() =>
        Gen.OneOf(GenSimpleAssignmentUseAfterMove(), GenReassignmentMoveUseAfterMove()).ToArbitrary();

    public static Arbitrary<ValidMoveScenario> ArbitraryValidMoveScenario() =>
        GenValidSimpleMove().ToArbitrary();

    public static Arbitrary<ConditionalMoveScenario> ArbitraryConditionalMoveScenario() =>
        GenConditionalMoveScenario().ToArbitrary();

    public static Arbitrary<LoopMoveScenario> ArbitraryLoopMoveScenario() =>
        GenWhileLoopMoveScenario().ToArbitrary();

    public static Arbitrary<ReassignmentScenario> ArbitraryReassignmentScenario() =>
        GenReassignmentAfterMoveScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4a: Use-after-move produces MIST_OWN_USE_AFTER_MOVE
    // **Validates: Requirements 7.4, 7.9, 10.1, 10.2, 10.3, 16.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipMovePropertyTests) })]
    public Property UseAfterMove_EmitsUseAfterMoveDiagnostic(UseAfterMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasUseAfterMove = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE");

        return hasUseAfterMove.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_USE_AFTER_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4b: Valid moves (no access after move) produce no move diagnostics
    // **Validates: Requirements 7.4, 7.5, 10.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipMovePropertyTests) })]
    public Property ValidMove_NoMoveDiagnostics(ValidMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMoveDiagnostic = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE" ||
            d.Id.Value == "MIST_OWN_CONDITIONAL_MOVE" ||
            d.Id.Value == "MIST_OWN_LOOP_MOVE");

        return (!hasMoveDiagnostic).ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected no move diagnostics but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4c: Conditional moves produce use-after-move at join point
    // **Validates: Requirements 10.4, 10.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipMovePropertyTests) })]
    public Property ConditionalMove_EmitsUseAfterMoveAfterJoin(ConditionalMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // After a conditional move, the conservative merge marks the variable as Moved.
        // Subsequent access produces MIST_OWN_USE_AFTER_MOVE.
        var hasMoveError = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE" ||
            d.Id.Value == "MIST_OWN_CONDITIONAL_MOVE");

        return hasMoveError.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected move error after conditional but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4d: Loop moves produce MIST_OWN_LOOP_MOVE
    // **Validates: Requirements 10.5, 10.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipMovePropertyTests) })]
    public Property LoopMove_EmitsLoopMoveDiagnostic(LoopMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasLoopMove = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_LOOP_MOVE");

        return hasLoopMove.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_LOOP_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4e: Re-assignment after move restores liveness (no error on access)
    // **Validates: Requirements 7.5, 10.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipMovePropertyTests) })]
    public Property ReassignmentAfterMove_NoMoveDiagnostics(ReassignmentScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMoveDiagnostic = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE" ||
            d.Id.Value == "MIST_OWN_CONDITIONAL_MOVE" ||
            d.Id.Value == "MIST_OWN_LOOP_MOVE");

        return (!hasMoveDiagnostic).ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected no move diagnostics after re-assignment but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
