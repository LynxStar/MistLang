// Feature: variadics, task 14.5: property test for zip length proof (classic pipeline).
//
// Property 12: Zip requires provably-equal length.
// **Validates: Requirements 8.5, 8.6, 8.7**
//
// A zip is the parallel spread of two operands (`left... = right...`). It expands the operands
// element by element into N paired positions, where N is the common length (Requirement 8.5),
// and is permitted ONLY when the two operands are proven to have equal length -- because they
// originate from the SAME pack, or because equal length is otherwise established (two concrete
// tuples of equal count) (Requirement 8.6). A zip between operands not proven equal length
// emits MIST_VAR_0006 (Requirement 8.7).
//
// The exhaustive accepted/rejected property is driven DIRECTLY against the pure classic-track
// rule TypeCheckerAnalyzer.CheckZipLengthProof, in-process, with ZERO external process spawns.
// Generated operands span the full space: two concrete tuples (equal / unequal count), the
// same symbolic pack vs different packs vs a symbolic pack against a concrete tuple, and the
// deferred not-applicable operands (error-typed or non-tuple). An independent oracle pins the
// expected status without consulting the rule under test. The pure statuses map to the
// observable behavior as: NotProven is exactly the case production surfaces as MIST_VAR_0006,
// while NotApplicable defers with no diagnostic.
//
// Two end-to-end tests exercise the assignment-zip chokepoint (TryCheckZipAssignment on
// CheckAssignmentNode) through the classic pipeline (lex/parse -> stdlib semantic model ->
// TypeCheckerAnalyzer, no backend, no process spawn), confirming that a concrete equal-length
// zip is well-typed (no MIST_VAR_0006) and a concrete unequal-length zip emits MIST_VAR_0006.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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
/// The kind of a generated zip operand: a fully concrete tuple, a sole-element symbolic pack
/// tuple <c>(P...)</c>, a non-tuple type, or an error type.
/// </summary>
public enum ZipOperandKind
{
    ConcreteTuple,
    SymbolicPack,
    NonTuple,
    ErrorTyped,
}

/// <summary>
/// A generated zip operand. <see cref="Value"/> is the element count for a
/// <see cref="ZipOperandKind.ConcreteTuple"/> and the pack origin id for a
/// <see cref="ZipOperandKind.SymbolicPack"/>; it is unused for the other kinds.
/// </summary>
public sealed record ZipOperandSpec(ZipOperandKind Kind, int Value)
{
    public override string ToString()
    {
        return Kind switch
        {
            ZipOperandKind.ConcreteTuple => $"concrete[{Value}]",
            ZipOperandKind.SymbolicPack => $"pack#{Value}",
            ZipOperandKind.NonTuple => "non-tuple",
            ZipOperandKind.ErrorTyped => "error",
            _ => Kind.ToString(),
        };
    }
}

/// <summary>A generated pair of zip operands.</summary>
public sealed record ZipScenario(ZipOperandSpec Left, ZipOperandSpec Right)
{
    public override string ToString()
    {
        return $"{Left} zip {Right}";
    }
}

/// <summary>
/// Property test for <b>Property 12 (Zip requires provably-equal length)</b> over the classic
/// pipeline. Drives <see cref="TypeCheckerAnalyzer.CheckZipLengthProof"/> directly and asserts
/// that a zip is accepted iff the two operands are proven equal length (yielding N paired
/// positions) and otherwise raises the MIST_VAR_0006 condition; two end-to-end tests confirm
/// the assignment-zip diagnostic through the classic pipeline. <b>Validates: Requirements 8.5,
/// 8.6, 8.7.</b>
/// </summary>
public sealed class VariadicsZipLengthProofPropertyTests
{
    private const string ZipLengthNotProvenId = "MIST_VAR_0006";

    // Primitive singletons whose resolution / DisplayName are stable without the stdlib. Only
    // the concrete tuples' element COUNT matters to the length proof; the element types are
    // drawn from this palette purely for stable, readable operands.
    private static readonly IType[] Palette =
        [LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool, LegacyPrimitiveTypes.Char];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: accepted IFF proven equal length (N paired positions), else the
    // MIST_VAR_0006 condition; error-typed / non-tuple operands defer (not applicable).
    // **Validates: Requirements 8.5, 8.6, 8.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsZipLengthProofPropertyTests) }, MaxTest = 200)]
    public bool Zip_AcceptedIffProvenEqualLength(ZipScenario scenario)
    {
        var left = BuildOperand(scenario.Left);
        var right = BuildOperand(scenario.Right);

        var expected = ExpectedZipStatus(scenario.Left, scenario.Right);
        var actual = TypeCheckerAnalyzer.CheckZipLengthProof(left, right);

        Assert.Equal(expected, actual);

        // "accepted iff proven equal length".
        var accepted = actual == TypeCheckerAnalyzer.ZipLengthProofStatus.ProvenEqual;
        Assert.Equal(expected == TypeCheckerAnalyzer.ZipLengthProofStatus.ProvenEqual, accepted);

        // "yielding N paired positions": when two concrete tuples are accepted, N is their
        // common element count.
        if(accepted
            && scenario.Left.Kind == ZipOperandKind.ConcreteTuple
            && scenario.Right.Kind == ZipOperandKind.ConcreteTuple)
        {
            Assert.Equal(scenario.Left.Value, scenario.Right.Value);
        }

        // "otherwise raises MIST_VAR_0006": production emits the diagnostic exactly on a
        // NotProven result; a NotApplicable result defers with no diagnostic.
        var raisesMistVar0006 = actual == TypeCheckerAnalyzer.ZipLengthProofStatus.NotProven;
        Assert.Equal(expected == TypeCheckerAnalyzer.ZipLengthProofStatus.NotProven, raisesMistVar0006);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // End-to-end: the assignment-zip chokepoint over the classic pipeline
    // (Requirements 8.5, 8.6, 8.7).
    // ═══════════════════════════════════════════════════════════════════════

    // Requirement 8.5 / 8.6: two concrete tuples of equal element count are proven equal
    // length, so `a... = b...` is a well-typed zip -- no MIST_VAR_0006.
    [Fact]
    public void ConcreteEqualLengthZip_IsWellTyped()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class C
            {
                public void Run()
                {
                    var a = (1, 2);
                    var b = (3, 4);
                    a... = b...;
                }
            }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Id.Value == ZipLengthNotProvenId);
    }

    // Requirement 8.7: two concrete tuples of differing element count are not proven equal
    // length, so `a... = b...` raises MIST_VAR_0006.
    [Fact]
    public void ConcreteUnequalLengthZip_EmitsMistVar0006()
    {
        var diagnostics = AnalyzeClassic(
            """
            public class C
            {
                public void Run()
                {
                    var a = (1, 2);
                    var b = (3, 4, 5);
                    a... = b...;
                }
            }
            """);

        var diagnostic = Assert.Single(diagnostics, d => d.Id.Value == ZipLengthNotProvenId);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<ZipScenario> ZipScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<ZipScenario> GenScenario()
    {
        return
            from left in GenOperand()
            from right in GenOperand()
            select new ZipScenario(left, right);
    }

    private static Gen<ZipOperandSpec> GenOperand()
    {
        return Gen.OneOf(
            from count in Gen.Choose(0, 4) select new ZipOperandSpec(ZipOperandKind.ConcreteTuple, count),
            from id in Gen.Choose(0, 2) select new ZipOperandSpec(ZipOperandKind.SymbolicPack, id),
            Gen.Constant(new ZipOperandSpec(ZipOperandKind.NonTuple, 0)),
            Gen.Constant(new ZipOperandSpec(ZipOperandKind.ErrorTyped, 0)));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Independent oracle + operand materialization
    // ═══════════════════════════════════════════════════════════════════════

    private static TypeCheckerAnalyzer.ZipLengthProofStatus ExpectedZipStatus(ZipOperandSpec left, ZipOperandSpec right)
    {
        // Error-typed or non-tuple operands defer: the length proof does not apply.
        if(left.Kind == ZipOperandKind.ErrorTyped || right.Kind == ZipOperandKind.ErrorTyped)
        {
            return TypeCheckerAnalyzer.ZipLengthProofStatus.NotApplicable;
        }

        if(left.Kind == ZipOperandKind.NonTuple || right.Kind == ZipOperandKind.NonTuple)
        {
            return TypeCheckerAnalyzer.ZipLengthProofStatus.NotApplicable;
        }

        // Both operands are tuples (concrete or a symbolic pack).
        var bothConcrete = left.Kind == ZipOperandKind.ConcreteTuple && right.Kind == ZipOperandKind.ConcreteTuple;
        if(bothConcrete)
        {
            return left.Value == right.Value
                ? TypeCheckerAnalyzer.ZipLengthProofStatus.ProvenEqual
                : TypeCheckerAnalyzer.ZipLengthProofStatus.NotProven;
        }

        // At least one symbolic pack: equal length is proven only when BOTH operands are the
        // SAME pack (same origin id), never a pack against a concrete tuple or a different pack.
        var sameSymbolicPack = left.Kind == ZipOperandKind.SymbolicPack
            && right.Kind == ZipOperandKind.SymbolicPack
            && left.Value == right.Value;
        return sameSymbolicPack
            ? TypeCheckerAnalyzer.ZipLengthProofStatus.ProvenEqual
            : TypeCheckerAnalyzer.ZipLengthProofStatus.NotProven;
    }

    private static IType BuildOperand(ZipOperandSpec spec)
    {
        switch(spec.Kind)
        {
            case ZipOperandKind.ConcreteTuple:
                var elements = new IType[spec.Value];
                for(var i = 0; i < spec.Value; i++)
                {
                    elements[i] = Palette[i % Palette.Length];
                }

                return new TupleType(elements);

            case ZipOperandKind.SymbolicPack:
                // A fresh, distinct pack parameter per call so equal-origin is decided by
                // value (same name/ordinal/arity), not reference identity.
                var pack = new TypeParameterType($"TPack{spec.Value}", spec.Value) { IsPack = true };
                return new TupleType(new IType[] { pack });

            case ZipOperandKind.NonTuple:
                return LegacyPrimitiveTypes.Int;

            case ZipOperandKind.ErrorTyped:
                return ErrorType.Instance;

            default:
                throw new InvalidOperationException($"Unhandled operand kind: {spec.Kind}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Classic-pipeline harness (in-process; no backend; ZERO external process spawns)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, builds a classic semantic model with the
    /// stdlib (<see cref="StdlibTestHelper.BuildModelWithStdlib"/>), runs
    /// <see cref="TypeCheckerAnalyzer"/> against it, and returns the analyzer diagnostics. No
    /// backend runs and no external process is spawned. Asserts the source parses without a
    /// lex/parse error.
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