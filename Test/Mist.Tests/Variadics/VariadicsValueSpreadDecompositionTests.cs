// Feature: variadics - Unit tests for value-side spread as general tuple decomposition (Task 14.1)
// Requirements: 7.5, 8.2, 8.3, 8.4, 10.9
//
// The value-side `...` operator decomposes a tuple value into ordered argument slots and is
// well-typed iff the tuple's ordered element types are positionally assignable to the target
// slots, otherwise MIST_VAR_0011 (Requirements 8.2, 8.4, 7.5). The rule applies to EVERY tuple
// value, so `args...` is simply the case where the tuple originated from a pack (Requirement 8.3)
// and a spread-generated aggregate `Item...` is re-spread as the tuple value it names
// (Requirement 10.9).
//
// Two complementary in-process approaches, with ZERO external process spawns:
//   * the pure decomposition well-typedness rule (TypeCheckerAnalyzer.CheckValueSpreadDecomposition)
//     is exercised directly over element/slot type lists -- this is the only way to cover the
//     empty tuple -> zero slots edge, because the grammar has no empty/one-element concrete tuple
//     literal (a tuple literal requires two or more elements);
//   * end-to-end, source is lexed/parsed, a classic semantic model is built with the stdlib
//     (SemanticModelBuilder via StdlibTestHelper), and TypeCheckerAnalyzer runs directly (no
//     backend), so a concrete tuple `(int, string)` decomposition emits MIST_VAR_0011 exactly when
//     the element types do not positionally match the callee's parameter slots.
//
// Scope. Task 14.1 decides only decompositions whose element types are concretely known. A
// symbolic value pack `args...` (its element types unbound in an open generic body) and a
// spread-generated aggregate `Item...` (aggregate type `(TArgs...)`, symbolic) are recognized as
// spreads and deferred -- their well-typedness for every instantiation is the generic-enforcement
// task, and binding a flat argument list that mixes fixed arguments with a spread is the call-site
// binder task -- so those cases emit no MIST_VAR_0011 here.

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
/// Task 14.1 tests for value-side spread as general tuple decomposition (Requirements 7.5, 8.2,
/// 8.3, 8.4, 10.9). Pure-rule tests exercise
/// <see cref="TypeCheckerAnalyzer.CheckValueSpreadDecomposition"/> directly (including the empty
/// tuple -> zero slots edge); end-to-end tests build a classic semantic model with the stdlib and
/// run <see cref="TypeCheckerAnalyzer"/> to assert MIST_VAR_0011 emission on a concrete tuple
/// decomposition.
/// </summary>
public sealed class VariadicsValueSpreadDecompositionTests
{
    private const string SpreadDestinationMismatchId = "MIST_VAR_0011";

    // ═══════════════════════════════════════════════════════════════════════
    // The pure decomposition well-typedness rule (Requirements 8.2, 8.4, 7.5).
    // ═══════════════════════════════════════════════════════════════════════

    // Requirement 8.2: an empty tuple decomposes into ZERO argument slots, so an empty tuple
    // against a zero-parameter target is well-typed. (No concrete empty tuple literal exists in
    // the grammar, so this edge is only reachable through the pure rule.)
    [Fact]
    public void EmptyTuple_AgainstZeroSlots_IsWellTyped()
    {
        var status = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(
            Array.Empty<IType>(),
            Array.Empty<IType>(),
            model: null,
            out var mismatchIndex);

        Assert.Equal(TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.WellTyped, status);
        Assert.Equal(-1, mismatchIndex);
    }

    // Requirement 8.2 / 8.4: an empty tuple against one slot is a count mismatch (zero slots
    // produced, one expected).
    [Fact]
    public void EmptyTuple_AgainstOneSlot_IsSlotCountMismatch()
    {
        var status = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(
            Array.Empty<IType>(),
            new IType[] { LegacyPrimitiveTypes.Int },
            model: null,
            out _);

        Assert.Equal(TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.SlotCountMismatch, status);
    }

    // Requirement 8.2 / 8.4: N ordered elements against N positionally-assignable slots is
    // well-typed (order preserved).
    [Fact]
    public void MatchingElementsInOrder_AreWellTyped()
    {
        var status = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(
            new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String },
            new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String },
            model: null,
            out var mismatchIndex);

        Assert.Equal(TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.WellTyped, status);
        Assert.Equal(-1, mismatchIndex);
    }

    // Requirement 8.4: a differing element count is a slot-count mismatch.
    [Fact]
    public void DifferingElementCount_IsSlotCountMismatch()
    {
        var status = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(
            new IType[] { LegacyPrimitiveTypes.Int },
            new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Int },
            model: null,
            out _);

        Assert.Equal(TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.SlotCountMismatch, status);
    }

    // Requirement 8.4 / 7.5: an element not assignable to its positional slot is a slot-type
    // mismatch, and the offending 0-based position is reported.
    [Fact]
    public void ElementNotAssignableToSlot_IsSlotTypeMismatchAtThatPosition()
    {
        var status = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(
            new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.String },
            new IType[] { LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Int },
            model: null,
            out var mismatchIndex);

        Assert.Equal(TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.SlotTypeMismatch, status);
        Assert.Equal(1, mismatchIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-end: a concrete tuple `x...` decomposed at a call site (Requirements 8.2, 8.4, 7.5).
    // ═══════════════════════════════════════════════════════════════════════

    // Requirement 8.4 / 7.5: a concrete tuple whose ordered element types positionally match the
    // callee's parameter slots is well-typed -- no MIST_VAR_0011.
    [Fact]
    public void ConcreteTupleMatchingParameters_IsWellTyped()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class C
            {
                public void Sum2(int a, int b) { }

                public void Run()
                {
                    var x = (1, 2);
                    this.Sum2(x...);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // Requirement 8.4 / 7.5: a concrete tuple with an element not assignable to its positional
    // slot (`string` into an `int` slot) emits MIST_VAR_0011.
    [Fact]
    public void ConcreteTupleWithMismatchedElementType_EmitsMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class C
            {
                public void Sum2(int a, int b) { }

                public void Run()
                {
                    var x = (1, "foo");
                    this.Sum2(x...);
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    // Requirement 8.2 / 8.4: a concrete tuple whose element count differs from the callee's slot
    // count emits MIST_VAR_0011 (three elements decomposed into two slots).
    [Fact]
    public void ConcreteTupleWithWrongElementCount_EmitsMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class C
            {
                public void Sum2(int a, int b) { }

                public void Run()
                {
                    var x = (1, 2, 3);
                    this.Sum2(x...);
                }
            }
            """);

        _ = Assert.Single(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 8.3: decomposition applies to EVERY tuple value; `args...` is simply the case
    // where the tuple originated from a pack, handled by the same general rule.
    // ═══════════════════════════════════════════════════════════════════════

    // A tuple value spread named `args` decomposes exactly like any other tuple value: when its
    // concrete element types match the callee, it is well-typed (no MIST_VAR_0011). Packs are not
    // special-cased.
    [Fact]
    public void ConcreteTupleNamedArgs_DecomposesLikeAnyTuple()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class C
            {
                public void Sink2(int a, int b) { }

                public void Fwd()
                {
                    var args = (10, 20);
                    this.Sink2(args...);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // A symbolic value pack `args...` in an open generic body has unbound element types, so its
    // decomposition is deferred to the generic-enforcement task -- no MIST_VAR_0011 is emitted here,
    // and the spread argument is never mis-reported as an ordinary single argument.
    [Fact]
    public void SymbolicValuePackForward_IsDeferred_NoMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class C
            {
                public void Sink<params TArgs>(params TArgs xs) { }

                public void Fwd<params TArgs>(params TArgs args)
                {
                    this.Sink(args...);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 10.9: a spread-generated aggregate may be re-spread. `Item` has the value-pack
    // tuple type `(TArgs...)`, so `Item...` is ordinary tuple decomposition of that aggregate.
    // Requirements 12.7, 12.8: the provable-forwarding rule applies to spread-generated aggregates
    // just as it does to `args...` -- spreading into a fixed-arity destination emits MIST_VAR_0011;
    // spreading into a provably-(TArgs...)-compatible destination is accepted.
    // ═══════════════════════════════════════════════════════════════════════

    // The spread-generated aggregate `Item` is spreadable like any other tuple value: `this.Item...`
    // into a fixed-arity destination that is not provably (TArgs...)-compatible emits MIST_VAR_0011
    // (Requirement 12.8). The pack is symbolic, so it cannot be proven to match `Sink(int a)`.
    [Fact]
    public void SpreadGeneratedAggregate_IntoFixedArity_EmitsMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Bundle<params TArgs>
            {
                public TArgs... Item;

                public void Sink(int a) { }

                public void Dump()
                {
                    this.Sink(this.Item...);
                }
            }
            """);

        _ = Assert.Single(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
    }

    // A spread-generated aggregate re-spread into a provably-compatible destination (another
    // `params TArgs` parameter on the same class) is accepted -- no MIST_VAR_0011.
    [Fact]
    public void SpreadGeneratedAggregate_IntoParamsTArgs_NoMistVar0011()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class Bundle<params TArgs>
            {
                public TArgs... Item;

                public void Forward(params TArgs other) { }

                public void Dump()
                {
                    this.Forward(this.Item...);
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == SpreadDestinationMismatchId);
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