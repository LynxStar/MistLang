// Feature: ownership-system, Property 5: Mutation blocking under frozen ref

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for mutation blocking under frozen ref.
///
/// Property 5: Mutation blocking under frozen ref
/// For any owner (or ref holder) that has lent a <c>frozen ref</c>, any attempt to
/// mutate the object through the lender SHALL produce a diagnostic error while the
/// frozen ref is outstanding. When all frozen refs are returned (scope exit), mutation
/// access SHALL be restored.
///
/// **Validates: Requirements 5.3, 5.4, 5.6, 6.7, 7.8, 7.11, 13.1, 13.2, 13.6**
/// </summary>
public class OwnershipFrozenBlockingPropertyTests
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
    /// Scenario where an owner lends a frozen ref and then attempts to mutate
    /// during the loan (should produce MIST_OWN_MUTATION_BLOCKED).
    /// </summary>
    public sealed record OwnerMutationBlockedScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where a ref holder lends a frozen ref and then attempts to mutate
    /// during the loan (should produce MIST_OWN_MUTATION_BLOCKED).
    /// </summary>
    public sealed record RefHolderMutationBlockedScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owner lends a frozen ref in a nested scope and mutates
    /// after the scope exits (valid — mutation restored).
    /// </summary>
    public sealed record MutationRestoredScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Widget", "Buffer", "Counter", "State", "Context", "Handler"];

    private static readonly string[] FieldNames =
        ["value", "count", "data", "status", "level", "index"];

    private static readonly string[] VarNames =
        ["x", "y", "obj", "item", "src", "target"];

    private static readonly string[] FrozenVarNames =
        ["view", "snapshot", "reader", "observer", "fv", "peek"];

    private static readonly string[] MethodNames =
        ["Observe", "Inspect", "ReadData", "PeekAt", "Examine", "Review"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a program where an owner lends a frozen ref to a method parameter,
    /// and then attempts to mutate the object while the frozen ref is outstanding.
    /// The mutation occurs inside the same scope as the frozen ref loan via a method
    /// that takes a frozen parameter — the owner tries to mutate after the call
    /// but within a scope where a frozen local is still alive.
    /// </summary>
    private static Gen<OwnerMutationBlockedScenario> GenOwnerMutationDuringFrozenLoan()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from frozenName in Gen.Elements(FrozenVarNames)
               let source = $$"""
                   owned class {{className}} {
                       int {{fieldName}};
                       public void SetValue(int v) {
                           {{fieldName}} = v;
                       }
                       public int GetValue() {
                           return {{fieldName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           frozen {{className}} {{frozenName}} = {{varName}};
                           {{varName}}.SetValue(42);
                       }
                   }
                   """
               select new OwnerMutationBlockedScenario(source,
                   $"owner {varName} mutates via SetValue while frozen {frozenName} is outstanding");
    }

    /// <summary>
    /// Generates a program where an owner lends a frozen ref and then attempts
    /// to assign to a field through the owner while the frozen borrow is outstanding.
    /// </summary>
    private static Gen<OwnerMutationBlockedScenario> GenOwnerFieldAssignDuringFrozenLoan()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from frozenName in Gen.Elements(FrozenVarNames)
               let source = $$"""
                   owned class {{className}} {
                       public int {{fieldName}};
                       public int GetValue() {
                           return {{fieldName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           frozen {{className}} {{frozenName}} = {{varName}};
                           {{varName}}.{{fieldName}} = 99;
                       }
                   }
                   """
               select new OwnerMutationBlockedScenario(source,
                   $"owner {varName} assigns field {fieldName} while frozen {frozenName} is outstanding");
    }

    /// <summary>
    /// Generates a program where a borrowed holder lends a frozen ref and then attempts
    /// to mutate through the borrowed holder while the frozen borrow is outstanding.
    /// </summary>
    private static Gen<RefHolderMutationBlockedScenario> GenRefHolderMutationDuringFrozenLoan()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from frozenName in Gen.Elements(FrozenVarNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $$"""
                   owned class {{className}} {
                       int {{fieldName}};
                       public void SetValue(int v) {
                           {{fieldName}} = v;
                       }
                       public int GetValue() {
                           return {{fieldName}};
                       }
                   }
                   class Program {
                       public static void {{methodName}}(borrowed {{className}} {{varName}}) {
                           frozen {{className}} {{frozenName}} = {{varName}};
                           {{varName}}.SetValue(42);
                       }
                       public static void Main() {
                           owned {{className}} src = new {{className}}();
                           {{methodName}}(src);
                       }
                   }
                   """
               select new RefHolderMutationBlockedScenario(source,
                   $"borrowed holder {varName} mutates via SetValue while frozen {frozenName} is outstanding in {methodName}");
    }

    /// <summary>
    /// Generates a program where an owner lends a frozen ref in a nested scope
    /// (via a method call with frozen parameter), and then mutates after the
    /// call returns (scope exit restores mutation access).
    /// </summary>
    private static Gen<MutationRestoredScenario> GenMutationRestoredAfterScopeExit()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $$"""
                   owned class {{className}} {
                       int {{fieldName}};
                       public void SetValue(int v) {
                           {{fieldName}} = v;
                       }
                       public int GetValue() {
                           return {{fieldName}};
                       }
                   }
                   class Program {
                       public static void {{methodName}}(frozen {{className}} param) {
                           param.GetValue();
                       }
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           {{methodName}}({{varName}});
                           {{varName}}.SetValue(42);
                       }
                   }
                   """
               select new MutationRestoredScenario(source,
                   $"owner {varName} mutates after frozen returned from {methodName}");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<OwnerMutationBlockedScenario> ArbitraryOwnerMutationBlockedScenario() =>
        Gen.OneOf(GenOwnerMutationDuringFrozenLoan(), GenOwnerFieldAssignDuringFrozenLoan()).ToArbitrary();

    public static Arbitrary<RefHolderMutationBlockedScenario> ArbitraryRefHolderMutationBlockedScenario() =>
        GenRefHolderMutationDuringFrozenLoan().ToArbitrary();

    public static Arbitrary<MutationRestoredScenario> ArbitraryMutationRestoredScenario() =>
        GenMutationRestoredAfterScopeExit().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5a: Owner mutation blocked during frozen ref loan
    // **Validates: Requirements 5.3, 7.8, 7.11, 13.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipFrozenBlockingPropertyTests) })]
    public Property OwnerMutationDuringFrozenLoan_EmitsMutationBlocked(OwnerMutationBlockedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMutationBlocked = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_MUTATION_BLOCKED");

        return hasMutationBlocked.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_MUTATION_BLOCKED but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5b: Ref holder mutation blocked during frozen ref loan
    // **Validates: Requirements 5.4, 6.7, 13.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipFrozenBlockingPropertyTests) })]
    public Property RefHolderMutationDuringFrozenLoan_EmitsMutationBlocked(RefHolderMutationBlockedScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMutationBlocked = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_MUTATION_BLOCKED");

        return hasMutationBlocked.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_MUTATION_BLOCKED but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5c: Mutation restored after frozen ref scope exit
    // **Validates: Requirements 5.6, 13.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipFrozenBlockingPropertyTests) })]
    public Property MutationAfterFrozenRefReturned_NoMutationBlocked(MutationRestoredScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMutationBlocked = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_MUTATION_BLOCKED");

        return (!hasMutationBlocked).ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected no MIST_OWN_MUTATION_BLOCKED but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}