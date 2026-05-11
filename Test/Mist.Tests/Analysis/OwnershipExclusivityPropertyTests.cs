// Feature: ownership-system, Property 6: Ref exclusivity

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
/// Property-based tests for ref exclusivity.
///
/// Property 6: Ref exclusivity
/// For any owner that has lent a <c>ref</c>, the owner SHALL have no access (read or write)
/// to the object until the ref is returned. Only one <c>ref</c> SHALL be outstanding from
/// an owner at a time. A second <c>ref</c> lend while one is outstanding SHALL produce a
/// diagnostic error.
///
/// **Validates: Requirements 6.3, 6.4, 7.12, 13.3, 13.7, 16.7**
/// </summary>
public class OwnershipExclusivityPropertyTests
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
    /// Scenario where an owner lends a ref and then attempts to access the object
    /// during the loan (should produce MIST_OWN_EXCLUSIVE_VIOLATION).
    /// </summary>
    public sealed record OwnerAccessDuringRefLoanScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owner attempts to lend a second ref while one is already
    /// outstanding (should produce MIST_OWN_EXCLUSIVE_VIOLATION).
    /// </summary>
    public sealed record MultipleRefLoanScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario where an owner lends a ref via a method call and then accesses
    /// the object after the call returns (valid — access restored after scope exit).
    /// </summary>
    public sealed record AccessRestoredAfterRefReturnScenario(string Source, string Label)
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

    private static readonly string[] RefVarNames =
        ["borrow", "handle", "mutRef", "editor", "rw", "access"];

    private static readonly string[] MethodNames =
        ["Modify", "Update", "Process", "Transform", "Mutate", "Apply"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a program where an owner lends a ref to a local variable,
    /// and then attempts to read the object through the owner while the ref
    /// is still outstanding.
    /// </summary>
    private static Gen<OwnerAccessDuringRefLoanScenario> GenOwnerReadDuringRefLoan()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from refName in Gen.Elements(RefVarNames)
               let source = $$"""
                   class {{className}} {
                       int {{fieldName}};
                       public int GetValue() {
                           return {{fieldName}};
                       }
                       public void SetValue(int v) {
                           {{fieldName}} = v;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           ref {{className}} {{refName}} = {{varName}};
                           {{varName}}.GetValue();
                       }
                   }
                   """
               select new OwnerAccessDuringRefLoanScenario(source,
                   $"owner {varName} reads via GetValue while ref {refName} is outstanding");
    }

    /// <summary>
    /// Generates a program where an owner lends a ref to a local variable,
    /// and then attempts to mutate the object through the owner while the ref
    /// is still outstanding.
    /// </summary>
    private static Gen<OwnerAccessDuringRefLoanScenario> GenOwnerWriteDuringRefLoan()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from refName in Gen.Elements(RefVarNames)
               let source = $$"""
                   class {{className}} {
                       int {{fieldName}};
                       public int GetValue() {
                           return {{fieldName}};
                       }
                       public void SetValue(int v) {
                           {{fieldName}} = v;
                       }
                   }
                   class Program {
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           ref {{className}} {{refName}} = {{varName}};
                           {{varName}}.SetValue(42);
                       }
                   }
                   """
               select new OwnerAccessDuringRefLoanScenario(source,
                   $"owner {varName} writes via SetValue while ref {refName} is outstanding");
    }

    /// <summary>
    /// Generates a program where an owner attempts to lend a second ref while
    /// one is already outstanding (two ref local variables from the same owner).
    /// </summary>
    private static Gen<MultipleRefLoanScenario> GenMultipleRefLoans()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $$"""
                   class {{className}} {
                       int {{fieldName}};
                       public void SetValue(int v) {
                           {{fieldName}} = v;
                       }
                   }
                   class Program {
                       public static void {{methodName}}(ref {{className}} a, ref {{className}} b) {
                           a.SetValue(1);
                           b.SetValue(2);
                       }
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           {{methodName}}({{varName}}, {{varName}});
                       }
                   }
                   """
               select new MultipleRefLoanScenario(source,
                   $"owner {varName} lent to two ref params in {methodName}");
    }

    /// <summary>
    /// Generates a program where an owner lends a ref via a method call parameter,
    /// and then accesses the object after the call returns (valid pattern — access
    /// is restored when the ref scope exits).
    /// </summary>
    private static Gen<AccessRestoredAfterRefReturnScenario> GenAccessRestoredAfterRefReturn()
    {
        return from className in Gen.Elements(ClassNames)
               from fieldName in Gen.Elements(FieldNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $$"""
                   class {{className}} {
                       int {{fieldName}};
                       public int GetValue() {
                           return {{fieldName}};
                       }
                       public void SetValue(int v) {
                           {{fieldName}} = v;
                       }
                   }
                   class Program {
                       public static void {{methodName}}(ref {{className}} param) {
                           param.SetValue(99);
                       }
                       public static void Main() {
                           owned {{className}} {{varName}} = new {{className}}();
                           {{methodName}}({{varName}});
                           {{varName}}.SetValue(42);
                       }
                   }
                   """
               select new AccessRestoredAfterRefReturnScenario(source,
                   $"owner {varName} accesses after ref returned from {methodName}");
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<OwnerAccessDuringRefLoanScenario> ArbitraryOwnerAccessDuringRefLoanScenario() =>
        Gen.OneOf(GenOwnerReadDuringRefLoan(), GenOwnerWriteDuringRefLoan()).ToArbitrary();

    public static Arbitrary<MultipleRefLoanScenario> ArbitraryMultipleRefLoanScenario() =>
        GenMultipleRefLoans().ToArbitrary();

    public static Arbitrary<AccessRestoredAfterRefReturnScenario> ArbitraryAccessRestoredAfterRefReturnScenario() =>
        GenAccessRestoredAfterRefReturn().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6a: Owner access during ref loan emits MIST_OWN_EXCLUSIVE_VIOLATION
    // **Validates: Requirements 6.3, 13.3, 16.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipExclusivityPropertyTests) })]
    public Property OwnerAccessDuringRefLoan_EmitsExclusiveViolation(OwnerAccessDuringRefLoanScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasExclusiveViolation = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_EXCLUSIVE_VIOLATION");

        return hasExclusiveViolation.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_EXCLUSIVE_VIOLATION but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6b: Multiple ref loans from same owner emits MIST_OWN_EXCLUSIVE_VIOLATION
    // **Validates: Requirements 6.4, 7.12, 16.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipExclusivityPropertyTests) })]
    public Property MultipleRefLoans_EmitsExclusiveViolation(MultipleRefLoanScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasExclusiveViolation = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_EXCLUSIVE_VIOLATION");

        return hasExclusiveViolation.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected MIST_OWN_EXCLUSIVE_VIOLATION but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6c: Access restored after ref scope exit — no exclusivity violation
    // **Validates: Requirements 13.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipExclusivityPropertyTests) })]
    public Property AccessAfterRefReturned_NoExclusiveViolation(AccessRestoredAfterRefReturnScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasExclusiveViolation = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_EXCLUSIVE_VIOLATION");

        return (!hasExclusiveViolation).ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Expected no MIST_OWN_EXCLUSIVE_VIOLATION but got: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
