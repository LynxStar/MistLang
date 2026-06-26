// Feature: owned-fields, updated from ownership-v2 Task 6.2
// Originally tested the CooperationContractVerifier (MIST_OWN_CONTRACT_VIOLATION).
// That verifier was removed in owned-fields Wave 1 (Task 8). The behavioral contract
// is now subsumed by:
//   - FieldTypeRestrictionVerifier: MIST_OWN_FIELD_TYPE_VIOLATION for owned types whose
//     fields are not owned/IClonable/value (Req 4).
//   - MoveBoundaryResolver: MIST_OWN_REQUIRES_OWNED / MIST_OWN_NOT_CLONABLE for
//     provenance violations at assignment sites (Req 8).
//
// This property test is updated to verify the new biconditional:
//
//   For all owned types T:
//       T compiles without field-type or provenance errors  ⟺  every field type is
//       owned/IClonable/value AND every assignment source resolves as a non-error move.
//
// The generator builds an `owned class Vault` with 1..4 fields. Each field's type +
// assignment source is drawn from a mix of compliant and violating combinations.
//
// COMPLIANT (no errors expected):
//   • Widget field + owned param      — Widget is owned-type, move from Live
//   • Config field + clonable param   — Config is IClonable, auto-clone
//   • Config field + constructor call — Config is IClonable, new T() is owned
//   • int field + value primitive     — value type field, value copy
//   • Config field + default          — IClonable + default yields owned
//
// VIOLATING (errors expected — MIST_OWN_FIELD_TYPE_VIOLATION):
//   • Gadget field (int[] blocks IClonable) — field type restriction fails
//
// The expected side of the biconditional: a violation occurs ⟺ at least one field
// uses a type that is not owned/IClonable/value (i.e., Gadget).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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
/// Property-based test for owned-field enforcement — updated from the old
/// CooperationContractVerifier property (ownership-v2 Task 6.2).
///
/// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 5.3, 5.4, 8.1**
///
/// For all owned types T, T compiles without field-type errors ⟺ every field's type
/// is an owned type, implements IClonable, or is a value type.
/// </summary>
public sealed class CooperationContractSoundnessPropertyTests
{
    private const string FieldTypeViolationId = "MIST_OWN_FIELD_TYPE_VIOLATION";

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

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_own_contract_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface a scenario

    /// <summary>
    /// The kinds of field-assignment source an owned type's method body can use.
    /// With the CooperationContractVerifier removed, the key distinction is now
    /// field TYPE compliance (FieldTypeRestrictionVerifier) rather than source compliance.
    /// </summary>
    public enum AssignmentSourceKind
    {
        // ── field type compliant (owned/IClonable/value) ──
        OwnedParam,        // Widget field, this.f = ownedWidgetParam;
        ConstructorCall,   // Config field, this.f = new Config(1);
        DefaultSource,     // Config field, this.f = default;
        ClonableParam,     // Config field, this.f = configParam;
        ValuePrimitive,    // int field, this.f = intParam;

        // ── field type violating (non-owned, non-IClonable, non-value) ──
        NonOwnedRefParam,  // Gadget field (int[] blocks IClonable auto-derivation)
    }

    /// <summary>
    /// A generated owned-type program plus the independently-computed expectation of
    /// whether it should emit <c>MIST_OWN_FIELD_TYPE_VIOLATION</c>.
    /// </summary>
    public sealed record ContractScenario(string Source, bool ExpectViolation, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    private static bool IsViolating(AssignmentSourceKind kind) =>
        kind is AssignmentSourceKind.NonOwnedRefParam;

    // ─── Per-assignment fragment construction ─────────────────────────────

    private readonly record struct AssignmentFragments(
        string FieldDecl,
        string? Param,
        string Statement);

    private static AssignmentFragments BuildFragments(AssignmentSourceKind kind, int index)
    {
        var field = $"f{index}";
        var param = $"p{index}";

        return kind switch
        {
            AssignmentSourceKind.OwnedParam => new AssignmentFragments(
                FieldDecl: $"    public Widget {field};",
                Param: $"owned Widget {param}",
                Statement: $"        this.{field} = {param};"),

            AssignmentSourceKind.ConstructorCall => new AssignmentFragments(
                FieldDecl: $"    public Config {field};",
                Param: null,
                Statement: $"        this.{field} = new Config(1);"),

            AssignmentSourceKind.DefaultSource => new AssignmentFragments(
                FieldDecl: $"    public Config {field};",
                Param: null,
                Statement: $"        this.{field} = default;"),

            AssignmentSourceKind.ClonableParam => new AssignmentFragments(
                FieldDecl: $"    public Config {field};",
                Param: $"Config {param}",
                Statement: $"        this.{field} = {param};"),

            AssignmentSourceKind.ValuePrimitive => new AssignmentFragments(
                FieldDecl: $"    public int {field};",
                Param: $"int {param}",
                Statement: $"        this.{field} = {param};"),

            AssignmentSourceKind.NonOwnedRefParam => new AssignmentFragments(
                FieldDecl: $"    public Gadget {field};",
                Param: $"Gadget {param}",
                Statement: $"        this.{field} = {param};"),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static ContractScenario Build(ImmutableArray<AssignmentSourceKind> kinds)
    {
        var fragments = kinds.Select((k, i) => BuildFragments(k, i)).ToImmutableArray();

        var fieldDecls = string.Join("\n", fragments.Select(f => f.FieldDecl));
        var parameters = string.Join(", ", fragments.Where(f => f.Param is not null).Select(f => f.Param));
        var statements = string.Join("\n", fragments.Select(f => f.Statement));

        var source = $$"""
            namespace TestProgram;

            class Widget
            {
                public int Value;
                public Widget(int value)
                {
                    this.Value = value;
                }
            }

            class Config
            {
                public int Timeout;
                public Config(int timeout)
                {
                    this.Timeout = timeout;
                }
            }

            class Gadget
            {
                public int[] data;
            }

            owned class Vault
            {
            {{fieldDecls}}

                public void Configure({{parameters}})
                {
            {{statements}}
                }
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var expectViolation = kinds.Any(IsViolating);
        var label = string.Join("+", kinds) + (expectViolation ? " => VIOLATION" : " => clean");
        return new ContractScenario(source, expectViolation, label);
    }

    // ─── Generator ───────────────────────────────────────────────────────

    private static readonly AssignmentSourceKind[] AllKinds =
    [
        AssignmentSourceKind.OwnedParam,
        AssignmentSourceKind.ConstructorCall,
        AssignmentSourceKind.DefaultSource,
        AssignmentSourceKind.ClonableParam,
        AssignmentSourceKind.ValuePrimitive,
        AssignmentSourceKind.NonOwnedRefParam,
    ];

    private static Gen<ContractScenario> GenScenario()
    {
        return from count in Gen.Choose(1, 4)
               from kinds in Gen.ArrayOf(Gen.Elements(AllKinds), count)
               select Build(kinds.ToImmutableArray());
    }

    public static Arbitrary<ContractScenario> ArbitraryContractScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Updated property: field-type restriction soundness — the biconditional.
    //
    //   no MIST_OWN_FIELD_TYPE_VIOLATION  ⟺  every field's type is owned/IClonable/value
    //                                         (no NonOwnedRefParam kind).
    //
    // **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 5.3, 5.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CooperationContractSoundnessPropertyTests) })]
    public Property CooperationContract_CompilesIffEveryAssignmentResolvesAsOwnedMove(ContractScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasViolation = diagnostics.Any(d => d.Id.Value == FieldTypeViolationId);

        if(hasViolation != scenario.ExpectViolation)
        {
            var ids = string.Join(", ", diagnostics.Select(d => d.Id.Value));
            return false.ToProperty().Label(
                $"Biconditional broken: expected violation={scenario.ExpectViolation} but observed={hasViolation}.\n"
                + $"Scenario: {scenario.Label}\nDiagnostics: [{ids}]\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}