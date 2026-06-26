// Feature: deep-ownership-types, Property 7: Control flow move tracking

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
/// Property-based tests for control flow move tracking (Property 7).
///
/// Property 7: Control Flow Move Tracking
/// For any owned binding that is moved in one branch of a conditional but not the other,
/// the binding SHALL be treated as Moved at the join point. For any owned binding moved
/// inside a loop body without re-assignment in the same iteration, the type checker SHALL
/// report an error.
///
/// **Validates: Requirements 5.5, 5.6**
/// </summary>
public class DeepOwnershipControlFlowMovePropertyTests
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

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario where an owned binding is moved in one branch of a conditional
    /// (if-branch) but not the other, and then accessed after the conditional.
    /// Expected: MIST_OWN_USE_AFTER_MOVE at the join point.
    /// </summary>
    public sealed record ConditionalMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned binding is moved inside a while loop without
    /// re-assignment in the same iteration.
    /// Expected: MIST_OWN_LOOP_MOVE.
    /// </summary>
    public sealed record LoopMoveScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owned binding is moved inside a loop but re-assigned
    /// a new owned value in the same iteration after the move.
    /// Expected: No error (re-assignment restores liveness for next iteration).
    /// </summary>
    public sealed record LoopMoveWithReassignmentScenario(string Source, string Label)
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
    /// Generates a program where an owned binding is moved in the if-branch
    /// but not the else-branch, and then accessed after the conditional.
    /// The conservative merge at the join point treats the variable as Moved.
    /// Validates: Requirement 5.5
    /// </summary>
    private static Gen<ConditionalMoveScenario> GenConditionalMoveIfBranchOnly()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"    public int GetValue() {{\n" +
                   $"        return 1;\n" +
                   $"    }}\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        bool cond = true;\n" +
                   $"        if (cond) {{\n" +
                   $"            owned {className} {destName} = {varName};\n" +
                   $"        }}\n" +
                   $"        {varName}.GetValue();\n" +
                   $"    }}\n" +
                   $"}}"
               select new ConditionalMoveScenario(source,
                   $"if-branch move {varName}->{destName}, no else, access after join");
    }

    /// <summary>
    /// Generates a program where an owned binding is moved in the if-branch
    /// with an explicit else-branch that does NOT move, and then accessed after.
    /// Validates: Requirement 5.5
    /// </summary>
    private static Gen<ConditionalMoveScenario> GenConditionalMoveIfWithElse()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"    public int GetValue() {{\n" +
                   $"        return 1;\n" +
                   $"    }}\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        bool cond = true;\n" +
                   $"        int dummy = 0;\n" +
                   $"        if (cond) {{\n" +
                   $"            owned {className} {destName} = {varName};\n" +
                   $"        }} else {{\n" +
                   $"            dummy = 1;\n" +
                   $"        }}\n" +
                   $"        {varName}.GetValue();\n" +
                   $"    }}\n" +
                   $"}}"
               select new ConditionalMoveScenario(source,
                   $"if-branch move {varName}->{destName}, else no-op, access after join");
    }

    /// <summary>
    /// Generates a program where an owned binding is moved inside a while loop
    /// via assignment to another owned variable, without re-assignment.
    /// Validates: Requirement 5.6
    /// </summary>
    private static Gen<LoopMoveScenario> GenWhileLoopMoveNoReassignment()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        int i = 0;\n" +
                   $"        while (i < 3) {{\n" +
                   $"            owned {className} {destName} = {varName};\n" +
                   $"            i = i + 1;\n" +
                   $"        }}\n" +
                   $"    }}\n" +
                   $"}}"
               select new LoopMoveScenario(source,
                   $"while-loop move {varName}->{destName} without re-assignment");
    }

    /// <summary>
    /// Generates a program where an owned binding is moved inside a while loop
    /// via passing to an owned parameter, without re-assignment.
    /// Validates: Requirement 5.6
    /// </summary>
    private static Gen<LoopMoveScenario> GenWhileLoopMoveViaParameter()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Consume(owned {className} param) {{\n" +
                   $"    }}\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        int i = 0;\n" +
                   $"        while (i < 3) {{\n" +
                   $"            Consume({varName});\n" +
                   $"            i = i + 1;\n" +
                   $"        }}\n" +
                   $"    }}\n" +
                   $"}}"
               select new LoopMoveScenario(source,
                   $"while-loop move {varName} via owned param without re-assignment");
    }

    /// <summary>
    /// Generates a program where an owned binding is moved inside a while loop
    /// but re-assigned a new owned value in the same iteration after the move.
    /// This should NOT produce an error because re-assignment restores liveness.
    /// Validates: Requirement 5.6
    /// </summary>
    private static Gen<LoopMoveWithReassignmentScenario> GenLoopMoveWithReassignment()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from destName in Gen.Elements(DestVarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        int i = 0;\n" +
                   $"        while (i < 3) {{\n" +
                   $"            owned {className} {destName} = {varName};\n" +
                   $"            {varName} = new {className}();\n" +
                   $"            i = i + 1;\n" +
                   $"        }}\n" +
                   $"    }}\n" +
                   $"}}"
               select new LoopMoveWithReassignmentScenario(source,
                   $"while-loop move {varName}->{destName} with re-assignment in same iteration");
    }

    /// <summary>
    /// Generates a program where an owned binding is moved via parameter inside a loop
    /// but re-assigned a new owned value in the same iteration after the move.
    /// This should NOT produce an error because re-assignment restores liveness.
    /// Validates: Requirement 5.6
    /// </summary>
    private static Gen<LoopMoveWithReassignmentScenario> GenLoopMoveViaParamWithReassignment()
    {
        return from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               let source =
                   $"owned class {className} {{\n" +
                   $"}}\n" +
                   $"class Program {{\n" +
                   $"    public static void Consume(owned {className} param) {{\n" +
                   $"    }}\n" +
                   $"    public static void Main() {{\n" +
                   $"        owned {className} {varName} = new {className}();\n" +
                   $"        int i = 0;\n" +
                   $"        while (i < 3) {{\n" +
                   $"            Consume({varName});\n" +
                   $"            {varName} = new {className}();\n" +
                   $"            i = i + 1;\n" +
                   $"        }}\n" +
                   $"    }}\n" +
                   $"}}"
               select new LoopMoveWithReassignmentScenario(source,
                   $"while-loop move {varName} via param with re-assignment in same iteration");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<ConditionalMoveScenario> ArbitraryConditionalMoveScenario() =>
        Gen.OneOf(GenConditionalMoveIfBranchOnly(), GenConditionalMoveIfWithElse()).ToArbitrary();

    public static Arbitrary<LoopMoveScenario> ArbitraryLoopMoveScenario() =>
        Gen.OneOf(GenWhileLoopMoveNoReassignment(), GenWhileLoopMoveViaParameter()).ToArbitrary();

    public static Arbitrary<LoopMoveWithReassignmentScenario> ArbitraryLoopMoveWithReassignmentScenario() =>
        Gen.OneOf(GenLoopMoveWithReassignment(), GenLoopMoveViaParamWithReassignment()).ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7a: Conditional move — access after join reports use-after-move
    //
    // When an owned binding is moved in one branch of a conditional but not
    // the other, the binding is treated as Moved at the join point. Accessing
    // the binding after the conditional produces MIST_OWN_USE_AFTER_MOVE.
    //
    // **Validates: Requirement 5.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DeepOwnershipControlFlowMovePropertyTests) })]
    public Property ConditionalMove_ReportsUseAfterMoveAtJoin(ConditionalMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMoveError = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE");

        return hasMoveError.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_USE_AFTER_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7b: Loop move without re-assignment — reports MIST_OWN_LOOP_MOVE
    //
    // When an owned binding is moved inside a loop body without re-assignment
    // in the same iteration, the type checker reports MIST_OWN_LOOP_MOVE.
    //
    // **Validates: Requirement 5.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DeepOwnershipControlFlowMovePropertyTests) })]
    public Property LoopMove_ReportsLoopMoveDiagnostic(LoopMoveScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasLoopMove = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_LOOP_MOVE");

        return hasLoopMove.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_LOOP_MOVE but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7c: Loop move with re-assignment — no error
    //
    // When an owned binding is moved inside a loop but re-assigned a new owned
    // value in the same iteration after the move, no error is reported because
    // re-assignment restores liveness for the next iteration.
    //
    // **Validates: Requirement 5.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(DeepOwnershipControlFlowMovePropertyTests) })]
    public Property LoopMoveWithReassignment_NoError(LoopMoveWithReassignmentScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMoveDiagnostic = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_USE_AFTER_MOVE" ||
            d.Id.Value == "MIST_OWN_LOOP_MOVE");

        return (!hasMoveDiagnostic).ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected no move diagnostics but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}