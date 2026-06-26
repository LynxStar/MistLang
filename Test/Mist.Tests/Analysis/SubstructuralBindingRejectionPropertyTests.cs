// Feature: ownership-v2, Task 9.2 — Property 13: Owned binding rejects substructural types
// (design.md, OwnershipAnalyzer Pass B2: RejectOwnedBindingOnSubstructuralTypes).
//
// Property 13 (design.md):
//   For all types T declared `linear`, `affine`, or `relevant`, an `owned` binding of T
//   is rejected with `MIST_OWN_SUBSTRUCTURAL_BINDING`.
//   For all non-substructural types (unrestricted or `owned`), an `owned` binding is
//   permitted (subject to provenance).
//
// The generator builds a single type with a given SubstructuralDiscipline (None/Affine/
// Linear/Relevant) or with the `owned` modifier (cooperation contract), and a Main
// method that declares `owned var x = new T()`. The EXPECTED side is computed
// independently: MIST_OWN_SUBSTRUCTURAL_BINDING fires ⟺ the type's discipline is
// linear, affine, or relevant.
//
// Process-spawning policy: compilation is fully in-process via CompilerDriver.Compile()
// with EmitToDisk:false — ZERO external process spawns per FsCheck iteration.

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
/// Property-based test for <c>RejectOwnedBindingOnSubstructuralTypes</c> — Property 13
/// (ownership-v2 design, Task 9.2).
///
/// **Validates: Requirements 1.3, 1.4**
///
/// For all types T:
///   <c>owned</c> binding of T emits <c>MIST_OWN_SUBSTRUCTURAL_BINDING</c>
///     ⟺ T is declared <c>linear</c>, <c>affine</c>, or <c>relevant</c>.
/// </summary>
public sealed class SubstructuralBindingRejectionPropertyTests
{
    private const string SubstructuralBindingId = "MIST_OWN_SUBSTRUCTURAL_BINDING";

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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_own_substruct_pbt_" + Guid.NewGuid().ToString("N"));
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

#pragma warning disable CA1034 // nested types — FsCheck-friendly scenario + enum

    /// <summary>
    /// The kind of type modifier applied to the generated type declaration.
    /// Maps to the substructural discipline or ownership modifier in Mist.
    /// </summary>
    public enum TypeModifierKind
    {
        /// <summary>No modifier — unrestricted type (non-substructural).</summary>
        Unrestricted,

        /// <summary><c>owned class</c> — cooperation contract (non-substructural).</summary>
        Owned,

        /// <summary><c>linear class</c> — substructural (exactly-once drop).</summary>
        Linear,

        /// <summary><c>affine class</c> — substructural (at-most-once drop).</summary>
        Affine,

        /// <summary><c>relevant class</c> — substructural (at-least-once drop).</summary>
        Relevant,
    }

    /// <summary>
    /// A generated scenario: a type with a given modifier and the independently-computed
    /// expectation of whether <c>MIST_OWN_SUBSTRUCTURAL_BINDING</c> should fire.
    /// </summary>
    public sealed record SubstructuralScenario(string Source, bool ExpectRejection, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    /// <summary>
    /// Returns true if the type modifier is a substructural discipline (linear/affine/relevant).
    /// </summary>
    private static bool IsSubstructural(TypeModifierKind kind) =>
        kind is TypeModifierKind.Linear or TypeModifierKind.Affine or TypeModifierKind.Relevant;

    /// <summary>
    /// Returns the Mist modifier keyword(s) for the given kind.
    /// </summary>
    private static string ModifierKeyword(TypeModifierKind kind) => kind switch
    {
        TypeModifierKind.Unrestricted => "",
        TypeModifierKind.Owned => "owned ",
        TypeModifierKind.Linear => "linear ",
        TypeModifierKind.Affine => "affine ",
        TypeModifierKind.Relevant => "relevant ",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Builds a complete Mist program with a type of the given modifier kind and an
    /// <c>owned var</c> binding of that type in Main.
    /// </summary>
    private static SubstructuralScenario Build(TypeModifierKind kind)
    {
        var modifier = ModifierKeyword(kind);

        // Substructural types require a Drop() method (linear/affine need exactly-once,
        // relevant needs at-least-once). Include it for substructural cases so the
        // program is otherwise well-formed and the ONLY diagnostic should be the
        // substructural binding rejection.
        var dropMethod = IsSubstructural(kind)
            ? """

                public void Drop()
                {
                }
            """
            : "";

        var source = $$"""
            namespace TestProgram;

            {{modifier}}class TestType
            {
                public int Value;

                public TestType(int value)
                {
                    this.Value = value;
                }{{dropMethod}}
            }

            class Program
            {
                public static void Main()
                {
                    owned var x = new TestType(42);
                }
            }
            """;

        var expectRejection = IsSubstructural(kind);
        var label = $"{kind} => {(expectRejection ? "REJECTED" : "PERMITTED")}";
        return new SubstructuralScenario(source, expectRejection, label);
    }

    // ─── Generator ───────────────────────────────────────────────────────

    private static readonly TypeModifierKind[] AllKinds =
    [
        TypeModifierKind.Unrestricted,
        TypeModifierKind.Owned,
        TypeModifierKind.Linear,
        TypeModifierKind.Affine,
        TypeModifierKind.Relevant,
    ];

    private static Gen<SubstructuralScenario> GenScenario()
    {
        return from kind in Gen.Elements(AllKinds)
               select Build(kind);
    }

    public static Arbitrary<SubstructuralScenario> ArbitrarySubstructuralScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: Owned binding rejects substructural types — the biconditional.
    //
    //   MIST_OWN_SUBSTRUCTURAL_BINDING fires  ⟺  T is linear/affine/relevant.
    //
    // **Validates: Requirements 1.3, 1.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SubstructuralBindingRejectionPropertyTests) })]
    public Property OwnedBinding_RejectsSubstructuralTypes_PermitsNonSubstructural(SubstructuralScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasRejection = diagnostics.Any(d => d.Id.Value == SubstructuralBindingId);

        if(hasRejection != scenario.ExpectRejection)
        {
            var ids = string.Join(", ", diagnostics.Select(d => d.Id.Value));
            return false.ToProperty().Label(
                $"Biconditional broken: expected rejection={scenario.ExpectRejection} but observed={hasRejection}.\n"
                + $"Scenario: {scenario.Label}\nDiagnostics: [{ids}]\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}