// Feature: ownership-v2, Task 6.2 — Property 8: Cooperation contract soundness
// (design Component 3, CooperationContractVerifier).
//
// Property 8 (design.md):
//   For all owned types T:
//       T compiles  ⟺  every field assignment in T's methods/constructors
//                       resolves as a non-error owned move.
//
// Operationally, "T compiles" is read as "T compiles without
// MIST_OWN_CONTRACT_VIOLATION" — that diagnostic is precisely the verifier's report
// that a `this.Field = <source>` assignment in an owned type's body did NOT resolve
// as a non-error owned move (design LD-2, Req 5 AC 3–4, Req 13 AC 4–5).
//
// The generator builds an `owned class Vault` whose single mutable method `Configure`
// performs 1..4 field assignments, each `this.Field = <source>` drawn from a mix of:
//
//   COMPLIANT sources (resolve as a non-error owned move):
//     • owned parameter        — move from a Live owned binding (Req 6 AC 3)
//     • constructor call new T — fresh owned graph                (Req 6 AC 9)
//     • default                — fresh owned graph                (Req 6 AC 10)
//     • clonable param         — non-owned src auto-deriving IClonable → ImplicitClone
//                                (Req 8 AC 2–3, AC 6)
//     • value/primitive        — value copy, no provenance gate   (Req 4 AC 3)
//
//   VIOLATING sources (resolve as Error):
//     • non-owned reference param (non-clonable: an int[] field blocks auto-derivation)
//                                (Req 13 AC 4)
//     • static/global read        — global state is never owned   (design LD-2)
//
// The EXPECTED side of the biconditional is computed independently of the compiler:
// a generated program contains a contract violation ⟺ at least one of its assignment
// sources is a VIOLATING kind. The program is then compiled in-process via a single
// shared CompilerDriver (test-process-spawning policy: 0 external process spawns,
// EmitToDisk:false), and the presence/absence of MIST_OWN_CONTRACT_VIOLATION is
// asserted to match the expectation exactly.

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
/// Property-based test for the <see cref="CooperationContractVerifier"/> — Property 8
/// (ownership-v2 design Component 3, Task 6.2).
///
/// **Validates: Requirements 5.3, 5.4**
///
/// For all owned types T, T compiles (no <c>MIST_OWN_CONTRACT_VIOLATION</c>) ⟺ every
/// field assignment in T's methods/constructors resolves as a non-error owned move.
/// </summary>
public sealed class CooperationContractSoundnessPropertyTests
{
    private const string ContractViolationId = "MIST_OWN_CONTRACT_VIOLATION";

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
    /// The kinds of field-assignment source an owned type's method body can use, split
    /// into those that resolve as a non-error owned move (compliant) and those that
    /// resolve as <see cref="MoveOutcome.Error"/> (violating).
    /// </summary>
    public enum AssignmentSourceKind
    {
        // ── compliant ──
        OwnedParam,        // this.f = ownedWidgetParam;     (move from Live owned binding)
        ConstructorCall,   // this.f = new Widget(1);        (fresh owned graph)
        DefaultSource,     // this.f = default;              (fresh owned graph)
        ClonableParam,     // this.f = configParam;          (non-owned, auto-IClonable → clone)
        ValuePrimitive,    // this.f = intParam;             (value copy)

        // ── violating ──
        NonOwnedRefParam,  // this.f = gadgetParam;          (non-owned, non-clonable → Error)
        StaticRead,        // this.f = Registry.Shared;      (global state → Error)
    }

    /// <summary>
    /// A generated owned-type program plus the independently-computed expectation of
    /// whether it should emit <c>MIST_OWN_CONTRACT_VIOLATION</c>.
    /// </summary>
    public sealed record ContractScenario(string Source, bool ExpectViolation, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    private static bool IsViolating(AssignmentSourceKind kind) =>
        kind is AssignmentSourceKind.NonOwnedRefParam or AssignmentSourceKind.StaticRead;

    // ─── Per-assignment fragment construction ─────────────────────────────

    /// <summary>
    /// The Mist fragments contributed by a single field assignment: its field
    /// declaration, an optional method parameter, and the assignment statement.
    /// </summary>
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
                FieldDecl: $"    public Widget {field};",
                Param: null,
                Statement: $"        this.{field} = new Widget(1);"),

            AssignmentSourceKind.DefaultSource => new AssignmentFragments(
                FieldDecl: $"    public Widget {field};",
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

            AssignmentSourceKind.StaticRead => new AssignmentFragments(
                FieldDecl: $"    public Config {field};",
                Param: null,
                Statement: $"        this.{field} = Registry.Shared;"),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    private static ContractScenario Build(ImmutableArray<AssignmentSourceKind> kinds)
    {
        var fragments = kinds.Select((k, i) => BuildFragments(k, i)).ToImmutableArray();

        var fieldDecls = string.Join("\n", fragments.Select(f => f.FieldDecl));
        var parameters = string.Join(", ", fragments.Where(f => f.Param is not null).Select(f => f.Param));
        var statements = string.Join("\n", fragments.Select(f => f.Statement));

        // Supporting types (declared unconditionally; unused ones are harmless):
        //   Widget  — plain owned-movable / constructable class
        //   Config  — auto-derives IClonable<Config> (only a primitive field)
        //   Gadget  — NON-clonable (int[] field blocks auto-derivation)
        //   Registry — exposes a static Config for the global-read violation
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

            class Registry
            {
                public static Config Shared { get; set; }
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
        AssignmentSourceKind.StaticRead,
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
    // Property 8: cooperation contract soundness — the biconditional.
    //
    //   no MIST_OWN_CONTRACT_VIOLATION  ⟺  every field assignment source is a
    //                                       non-error owned move (no VIOLATING kind).
    //
    // **Validates: Requirements 5.3, 5.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(CooperationContractSoundnessPropertyTests) })]
    public Property CooperationContract_CompilesIffEveryAssignmentResolvesAsOwnedMove(ContractScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasViolation = diagnostics.Any(d => d.Id.Value == ContractViolationId);

        if (hasViolation != scenario.ExpectViolation)
        {
            var ids = string.Join(", ", diagnostics.Select(d => d.Id.Value));
            return false.ToProperty().Label(
                $"Biconditional broken: expected violation={scenario.ExpectViolation} but observed={hasViolation}.\n"
                + $"Scenario: {scenario.Label}\nDiagnostics: [{ids}]\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}
