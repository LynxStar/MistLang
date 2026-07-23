// Feature: variadics - Unit tests for zip parallel expansion (Task 14.2)
// Requirements: 8.5, 8.6, 8.7
//
// A zip is the parallel spread of two operands on an assignment, canonically `left... = right...`
// where BOTH sides carry the value-side `...` suffix (for example a variadic constructor's
// `Item... = values...`, pairing a value pack into its spread-generated aggregate). It expands the
// operands element by element into N paired positions, where N is the common length, so each
// left-hand slot receives exactly one right-hand value (Requirement 8.5). A zip is permitted ONLY
// when the two operands are proven to have equal length -- either because they originate from the
// SAME pack (both are the tuple `(P...)` of one pack `P`, hence the same symbolic length N for every
// instantiation) or because equal length is otherwise established (two concrete tuples of equal
// element count) (Requirement 8.6). A zip between operands not proven equal length emits
// MIST_VAR_0006 (Requirement 8.7).
//
// Two complementary in-process approaches, with ZERO external process spawns:
//   * the pure length-proof rule (TypeCheckerAnalyzer.CheckZipLengthProof) is exercised directly
//     over the two operand types -- this covers the symbolic same-pack, different-pack, and
//     concrete-count cases uniformly and reaches edges (a lone concrete operand pair) that have no
//     natural assignable surface form;
//   * end-to-end, source is lexed/parsed, a classic semantic model is built with the stdlib
//     (SemanticModelBuilder via StdlibTestHelper), and TypeCheckerAnalyzer runs directly (no
//     backend), so the same-pack constructor zip `Item... = values...` is well-typed and a symbolic
//     pack zipped with a concrete tuple emits MIST_VAR_0006.

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Variadics;

/// <summary>
/// Task 14.2 tests for zip parallel expansion (Requirements 8.5, 8.6, 8.7). Pure-rule tests exercise
/// <see cref="TypeCheckerAnalyzer.CheckZipLengthProof"/> directly (same-pack origin, different packs,
/// concrete equal/unequal counts, and the deferred not-applicable cases); end-to-end tests build a
/// classic semantic model with the stdlib and run <see cref="TypeCheckerAnalyzer"/> to assert that a
/// same-pack constructor zip is well-typed and a not-provably-equal zip emits MIST_VAR_0006.
/// </summary>
public sealed class VariadicsZipTests
{
    private const string ZipLengthNotProvenId = "MIST_VAR_0006";

    // ═══════════════════════════════════════════════════════════════════════
    // The pure zip length-proof rule (Requirements 8.5, 8.6, 8.7).
    // ═══════════════════════════════════════════════════════════════════════

    // Requirement 8.6: two fully concrete tuples of equal element count are proven equal length.
    [Fact]
    public void ConcreteEqualCountTuples_AreProvenEqual()
    {
        var left = new TupleType(new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String });
        var right = new TupleType(new IType[] { LegacyPrimitiveTypes.Bool, LegacyPrimitiveTypes.Int });

        var status = TypeCheckerAnalyzer.CheckZipLengthProof(left, right);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.ProvenEqual, status);
    }

    // Requirement 8.7: two concrete tuples of differing element count are NOT proven equal length.
    [Fact]
    public void ConcreteDifferingCountTuples_AreNotProven()
    {
        var left = new TupleType(new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String });
        var right = new TupleType(new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String, LegacyPrimitiveTypes.Bool });

        var status = TypeCheckerAnalyzer.CheckZipLengthProof(left, right);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.NotProven, status);
    }

    // Requirement 8.6: two operands that spread the SAME pack `(P...)` (the same symbolic pack
    // instance) are proven equal length -- same origin, same length N for every instantiation.
    [Fact]
    public void SamePackOrigin_SameInstance_IsProvenEqual()
    {
        var pack = new TypeParameterType("TArgs", 0) { IsPack = true };
        var left = new TupleType(new IType[] { pack });
        var right = new TupleType(new IType[] { pack });

        var status = TypeCheckerAnalyzer.CheckZipLengthProof(left, right);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.ProvenEqual, status);
    }

    // Requirement 8.6: the same pack referenced through distinct but equivalent pack parameters
    // (same name, ordinal, arity) is still the same origin -- proven equal length. This is the
    // motivating case: a value pack `args : (TArgs...)` and its spread-generated aggregate
    // `Item : (TArgs...)` both name the one pack `TArgs`.
    [Fact]
    public void SamePackOrigin_EquivalentPackParameters_IsProvenEqual()
    {
        var aggregatePack = new TypeParameterType("TArgs", 0) { IsPack = true };
        var valuePack = new TypeParameterType("TArgs", 0) { IsPack = true };
        var left = new TupleType(new IType[] { aggregatePack });
        var right = new TupleType(new IType[] { valuePack });

        var status = TypeCheckerAnalyzer.CheckZipLengthProof(left, right);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.ProvenEqual, status);
    }

    // Requirement 8.7: two DIFFERENT packs are not proven to have equal length (nothing relates
    // their symbolic lengths), so their zip is rejected.
    [Fact]
    public void DifferentPacks_AreNotProven()
    {
        var first = new TypeParameterType("TArgs", 0) { IsPack = true };
        var second = new TypeParameterType("TOther", 1) { IsPack = true };
        var left = new TupleType(new IType[] { first });
        var right = new TupleType(new IType[] { second });

        var status = TypeCheckerAnalyzer.CheckZipLengthProof(left, right);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.NotProven, status);
    }

    // Requirement 8.7: a symbolic pack `(TArgs...)` zipped against an unrelated concrete tuple is
    // not proven equal length (the symbolic length is unknown), so the zip is rejected.
    [Fact]
    public void SymbolicPackAgainstConcreteTuple_IsNotProven()
    {
        var pack = new TypeParameterType("TArgs", 0) { IsPack = true };
        var left = new TupleType(new IType[] { pack });
        var right = new TupleType(new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Int });

        var status = TypeCheckerAnalyzer.CheckZipLengthProof(left, right);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.NotProven, status);
    }

    // An operand carrying an error type yields NotApplicable: the zip is deferred and no diagnostic
    // is emitted (error types suppress cascading diagnostics).
    [Fact]
    public void ErrorTypeOperand_IsNotApplicable()
    {
        var right = new TupleType(new IType[] { LegacyPrimitiveTypes.Int });

        var status = TypeCheckerAnalyzer.CheckZipLengthProof(ErrorType.Instance, right);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.NotApplicable, status);
    }

    // A non-tuple operand yields NotApplicable: without a tuple shape there is nothing to zip, so
    // the length proof does not apply and no diagnostic is emitted.
    [Fact]
    public void NonTupleOperand_IsNotApplicable()
    {
        var status = TypeCheckerAnalyzer.CheckZipLengthProof(LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Int);

        Assert.Equal(TypeCheckerAnalyzer.ZipLengthProofStatus.NotApplicable, status);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-end: a zip `left... = right...` on an assignment (Requirements 8.5, 8.6, 8.7).
    // ═══════════════════════════════════════════════════════════════════════

    // Requirement 8.5 / 8.6: the motivating same-pack-origin zip. In a variadic constructor the
    // spread-generated aggregate `Item : (TArgs...)` and the value pack `values : (TArgs...)` both
    // name the one pack `TArgs`, so `Item... = values...` is proven equal length and well-typed --
    // no MIST_VAR_0006.
    [Fact]
    public void SamePackOriginZipInConstructor_IsWellTyped()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Bundle<params TArgs>
            {
                public TArgs... Item;

                public Bundle(params TArgs values)
                {
                    Item... = values...;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == ZipLengthNotProvenId);
    }

    // Requirement 8.7: a symbolic pack zipped against a concrete tuple is not proven equal length.
    // The spread-generated aggregate `this.Item : (TArgs...)` is symbolic while `pair` is the
    // concrete tuple `(int, int)`, so `this.Item... = pair...` raises MIST_VAR_0006.
    [Fact]
    public void SymbolicPackZippedWithConcreteTuple_EmitsMistVar0006()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Bad<params TArgs>
            {
                public TArgs... Item;

                public void Set()
                {
                    var pair = (1, 2);
                    this.Item... = pair...;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id.Value == ZipLengthNotProvenId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, builds a classic semantic model with the stdlib
    /// (<see cref="StdlibTestHelper.BuildModelWithStdlib"/>), runs <see cref="TypeCheckerAnalyzer"/>
    /// against it, and returns the analyzer diagnostics. No backend runs and no external process is
    /// spawned. Asserts the source parses without a lex/parse error.
    /// </summary>
    private static ImmutableArray<Diagnostic> AnalyzeClassic(string source)
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var sourceText = new SourceText("test.mist", source);
        var tokens = lexer.Lex(sourceText, parseDiagnostics);
        var parseResult = parser.Parse(tokens, parseDiagnostics);

        Assert.NotNull(parseResult.Root);

        var parseErrors = parseDiagnostics.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(
            parseErrors.Count == 0,
            $"Unexpected parse errors: {string.Join("; ", parseErrors.Select(d => $"{d.Id.Value} {d.Message}"))}");

        var (model, _) = StdlibTestHelper.BuildModelWithStdlib(parseResult.Root!, source: source);

        var analyzerDiagnostics = new DiagnosticBag();
        new TypeCheckerAnalyzer().Analyze(model, analyzerDiagnostics);
        return analyzerDiagnostics.ToImmutableArray();
    }
}