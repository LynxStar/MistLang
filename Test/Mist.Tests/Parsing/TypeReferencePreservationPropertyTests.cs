// Bugfix: parser-type-argument-backtracking — Property 2: Preservation
//
// Observation-first preservation baseline. These tests encode the parser
// behavior for type references so that the grammar restructure
// (TypeReference → first:SingleTypeRef (PIPE rest:SingleTypeRef)*) is verified
// to preserve the same AST information content.
//
// After the grammar restructure, TypeReferenceNode is FLAT: it exposes
// First (the first/only constituent, a SingleTypeRefNode) and SingleTypeRefs
// (the PIPE-separated continuations). A union is detected via
// SingleTypeRefs.Length > 0. The single-constituent variants are the
// SingleTypeRefNode variants (Primitive/Void/Unknown/Named/Tuple/Parenthesized).
//
// Process-spawning policy: parsing is performed IN-PROCESS via MistLexer +
// MistParser only. No external process is spawned.
//
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// The category of type reference a scenario exercises. Each kind maps to a
/// distinct shape of the flat <see cref="TypeReferenceNode"/> (a union, or one
/// of the <see cref="SingleTypeRefNode"/> variants accessed via First).
/// </summary>
public enum TypeRefKind
{
    /// <summary><c>int | string</c> → a union TypeReferenceNode (SingleTypeRefs non-empty).</summary>
    Union,

    /// <summary><c>List&lt;int&gt;</c> → <see cref="NamedSingleTypeRefSingleTypeRefNode"/>.</summary>
    Named,

    /// <summary><c>int</c>, <c>int[]</c> → <see cref="PrimitiveSingleTypeRefSingleTypeRefNode"/>.</summary>
    Primitive,

    /// <summary><c>(int, string)</c> → <see cref="TupleSingleTypeRefSingleTypeRefNode"/>.</summary>
    Tuple,

    /// <summary><c>void</c> → <see cref="VoidSingleTypeRefSingleTypeRefNode"/>.</summary>
    Void,

    /// <summary><c>unknown</c> → <see cref="UnknownSingleTypeRefSingleTypeRefNode"/>.</summary>
    Unknown,
}

/// <summary>
/// A single preservation scenario: a type annotation written in source, the
/// expected AST variant it produces, and the structural metadata the
/// produced node must match (constituent / type-argument / element count, and
/// whether an array rank suffix is present).
/// </summary>
public sealed class TypeRefScenario
{
    /// <summary>The type annotation as written in Mist source (e.g. "int | string").</summary>
    public string Annotation { get; }

    /// <summary>Which <see cref="TypeReferenceNode"/> shape this should produce.</summary>
    public TypeRefKind Kind { get; }

    /// <summary>
    /// For <see cref="TypeRefKind.Union"/>: number of constituents.
    /// For <see cref="TypeRefKind.Named"/>: number of type arguments.
    /// For <see cref="TypeRefKind.Tuple"/>: number of tuple elements.
    /// Unused (0) for Primitive / Void / Unknown.
    /// </summary>
    public int ExpectedCount { get; }

    /// <summary>For <see cref="TypeRefKind.Primitive"/>: whether an array rank suffix is expected.</summary>
    public bool ExpectArrayRank { get; }

    public TypeRefScenario(string annotation, TypeRefKind kind, int expectedCount = 0, bool expectArrayRank = false)
    {
        Annotation = annotation;
        Kind = kind;
        ExpectedCount = expectedCount;
        ExpectArrayRank = expectArrayRank;
    }

    public override string ToString()
    {
        return $"{Kind}: '{Annotation}' (count={ExpectedCount}, array={ExpectArrayRank})";
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Preservation property tests for type reference parsing. They observe and
/// lock in the parser behavior:
///
/// - <c>int | string</c> → union type AST with 2 constituents
/// - <c>List&lt;int&gt;</c> → named type with 1 type argument
/// - <c>int[]</c> → primitive type with array rank 1
/// - <c>(int, string)</c> → tuple type with 2 elements
/// - <c>void</c> → void type ref node
/// - <c>unknown</c> → unknown type ref node
/// - <c>(int | string)[]</c> → parenthesized type with inner union and array rank
///
/// Property: for all valid type reference inputs, the parser produces a correct
/// AST node matching the expected structure (per the Preservation Requirements
/// in design.md).
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9, 3.10**
/// </summary>
public sealed class TypeReferencePreservationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Parsing helpers ────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    /// <summary>
    /// Wraps a type annotation in a local variable declaration inside a class
    /// method, parses it, and returns the variable's <see cref="TypeReferenceNode"/>.
    /// </summary>
    private static TypeReferenceNode GetVariableTypeRef(string typeAnnotation)
    {
        var source = $"class T {{ public void M() {{ {typeAnnotation} x = default; }} }}";
        var cu = ParseOrFail(source);

        var typeDecl = (ClassTypeTypeDeclarationNode)cu.GetTypes()[0];
        var methodMember = (MethodMemberMemberDeclarationNode)typeDecl.ClassDeclaration.Members[0];
        var block = methodMember.Method.Body.GetBlock()
            ?? throw new InvalidOperationException("Method body block is null");
        var stmt = (VariableDeclarationStatementStatementNode)block.Statements[0];
        var varDecl = (TypedVariableDeclarationVariableDeclarationStatementNode)stmt.VariableDeclaration;
        return varDecl.Type;
    }

    // ─── Structural-count helpers ───────────────────────────────────────

    /// <summary>True iff the type reference is a union (has PIPE-separated continuations).</summary>
    private static bool IsUnion(TypeReferenceNode typeRef)
    {
        return typeRef.SingleTypeRefs.Length > 0;
    }

    /// <summary>Number of union constituents: First plus the PIPE-separated rest.</summary>
    private static int UnionConstituentCount(TypeReferenceNode typeRef)
    {
        return 1 + typeRef.SingleTypeRefs.Length;
    }

    /// <summary>Number of type arguments: first plus the comma-separated rest, or 0 if none.</summary>
    private static int TypeArgumentCount(NamedSingleTypeRefSingleTypeRefNode named)
    {
        return named.TypeArgs is null ? 0 : 1 + named.TypeArgs.TypeArguments.Length;
    }

    /// <summary>Number of tuple elements: First, Rest, plus the comma-separated additional elements.</summary>
    private static int TupleElementCount(TupleSingleTypeRefSingleTypeRefNode tuple)
    {
        return 2 + tuple.TupleTypeElements.Length;
    }

    // ─── Scenario evaluation ────────────────────────────────────────────

    /// <summary>
    /// Parses the scenario annotation and verifies the produced AST node matches
    /// the expected variant and structural metadata. Returns a success flag plus
    /// a human-readable detail string for failure labeling.
    /// </summary>
    private static (bool Ok, string Detail) Evaluate(TypeRefScenario scenario)
    {
        TypeReferenceNode typeRef;
        try
        {
            typeRef = GetVariableTypeRef(scenario.Annotation);
        }
        catch(Exception ex)
        {
            return (false, $"parse failed: {ex.Message}");
        }

        switch(scenario.Kind)
        {
            case TypeRefKind.Union:
                if(!IsUnion(typeRef))
                {
                    return (false, $"expected a union (SingleTypeRefs non-empty), got single {typeRef.First.GetType().Name}");
                }

                var uCount = UnionConstituentCount(typeRef);
                return (uCount == scenario.ExpectedCount,
                    $"union constituents: expected {scenario.ExpectedCount}, got {uCount}");

            case TypeRefKind.Named:
                if(typeRef.First is not NamedSingleTypeRefSingleTypeRefNode named)
                {
                    return (false, $"expected NamedSingleTypeRefSingleTypeRefNode, got {typeRef.First.GetType().Name}");
                }

                var aCount = TypeArgumentCount(named);
                return (aCount == scenario.ExpectedCount,
                    $"type arguments: expected {scenario.ExpectedCount}, got {aCount}");

            case TypeRefKind.Primitive:
                if(typeRef.First is not PrimitiveSingleTypeRefSingleTypeRefNode prim)
                {
                    return (false, $"expected PrimitiveSingleTypeRefSingleTypeRefNode, got {typeRef.First.GetType().Name}");
                }

                var hasRank = prim.ArrayRank is not null;
                return (hasRank == scenario.ExpectArrayRank,
                    $"array rank present: expected {scenario.ExpectArrayRank}, got {hasRank}");

            case TypeRefKind.Tuple:
                if(typeRef.First is not TupleSingleTypeRefSingleTypeRefNode tuple)
                {
                    return (false, $"expected TupleSingleTypeRefSingleTypeRefNode, got {typeRef.First.GetType().Name}");
                }

                var tCount = TupleElementCount(tuple);
                return (tCount == scenario.ExpectedCount,
                    $"tuple elements: expected {scenario.ExpectedCount}, got {tCount}");

            case TypeRefKind.Void:
                return (typeRef.First is VoidSingleTypeRefSingleTypeRefNode,
                    $"expected VoidSingleTypeRefSingleTypeRefNode, got {typeRef.First.GetType().Name}");

            case TypeRefKind.Unknown:
                return (typeRef.First is UnknownSingleTypeRefSingleTypeRefNode,
                    $"expected UnknownSingleTypeRefSingleTypeRefNode, got {typeRef.First.GetType().Name}");

            default:
                return (false, $"unhandled kind {scenario.Kind}");
        }
    }

    // ─── Arbitrary registration ─────────────────────────────────────────

    private static readonly TypeRefScenario[] UnionScenarios =
    [
        new("int | string", TypeRefKind.Union, expectedCount: 2),
        new("int | string | bool", TypeRefKind.Union, expectedCount: 3),
        new("int | string | bool | float", TypeRefKind.Union, expectedCount: 4),
        new("long | short", TypeRefKind.Union, expectedCount: 2),
        new("byte | char | double", TypeRefKind.Union, expectedCount: 3),
    ];

    private static readonly TypeRefScenario[] NamedScenarios =
    [
        new("List<int>", TypeRefKind.Named, expectedCount: 1),
        new("Map<string, int>", TypeRefKind.Named, expectedCount: 2),
        new("Result<bool>", TypeRefKind.Named, expectedCount: 1),
        new("Wrapper<int, string, bool>", TypeRefKind.Named, expectedCount: 3),
        new("Foo", TypeRefKind.Named, expectedCount: 0),
    ];

    private static readonly TypeRefScenario[] PrimitiveScenarios =
    [
        new("int", TypeRefKind.Primitive, expectArrayRank: false),
        new("string[]", TypeRefKind.Primitive, expectArrayRank: true),
        new("bool", TypeRefKind.Primitive, expectArrayRank: false),
        new("double[]", TypeRefKind.Primitive, expectArrayRank: true),
        new("long", TypeRefKind.Primitive, expectArrayRank: false),
    ];

    private static readonly TypeRefScenario[] TupleScenarios =
    [
        new("(int, string)", TypeRefKind.Tuple, expectedCount: 2),
        new("(int, string, bool)", TypeRefKind.Tuple, expectedCount: 3),
        new("(long, short, byte, char)", TypeRefKind.Tuple, expectedCount: 4),
        new("(float, double)", TypeRefKind.Tuple, expectedCount: 2),
    ];

    private static readonly TypeRefScenario[] AtomScenarios =
    [
        new("void", TypeRefKind.Void),
        new("unknown", TypeRefKind.Unknown),
    ];

    public static Arbitrary<TypeRefScenario> ArbitraryTypeRefScenario()
    {
        var gen = Gen.OneOf(
            Gen.Elements(UnionScenarios),
            Gen.Elements(NamedScenarios),
            Gen.Elements(PrimitiveScenarios),
            Gen.Elements(TupleScenarios),
            Gen.Elements(AtomScenarios));
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Preservation — Type Reference Parsing Semantic Equivalence
    //
    // For all valid type reference inputs, the parser produces a correct AST
    // node matching the expected structure.
    //
    // **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 200, Arbitrary = new[] { typeof(TypeReferencePreservationPropertyTests) })]
    public Property TypeReference_ProducesExpectedAstStructure(TypeRefScenario scenario)
    {
        var (ok, detail) = Evaluate(scenario);
        return ok.ToProperty()
            .Label($"Scenario: {scenario}")
            .Label($"Detail: {detail}");
    }

    // ─── Observation unit tests (the seven baseline cases) ───────────────

    [Fact]
    public void IntOrString_ProducesUnionWithTwoConstituents()
    {
        var typeRef = GetVariableTypeRef("int | string");
        Assert.NotEmpty(typeRef.SingleTypeRefs);
        Assert.Equal(2, UnionConstituentCount(typeRef));
    }

    [Fact]
    public void ListOfInt_ProducesNamedTypeWithOneTypeArgument()
    {
        var typeRef = GetVariableTypeRef("List<int>");
        var named = Assert.IsType<NamedSingleTypeRefSingleTypeRefNode>(typeRef.First);
        Assert.Equal(1, TypeArgumentCount(named));
    }

    [Fact]
    public void IntArray_ProducesPrimitiveTypeWithArrayRank()
    {
        var typeRef = GetVariableTypeRef("int[]");
        var prim = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(typeRef.First);
        _ = Assert.IsType<IntTypePrimitiveTypeNode>(prim.PrimitiveType);
        Assert.NotNull(prim.ArrayRank);
    }

    [Fact]
    public void IntStringTuple_ProducesTupleTypeWithTwoElements()
    {
        var typeRef = GetVariableTypeRef("(int, string)");
        var tuple = Assert.IsType<TupleSingleTypeRefSingleTypeRefNode>(typeRef.First);
        Assert.Equal(2, TupleElementCount(tuple));
    }

    [Fact]
    public void Void_ProducesVoidTypeRefNode()
    {
        var typeRef = GetVariableTypeRef("void");
        _ = Assert.IsType<VoidSingleTypeRefSingleTypeRefNode>(typeRef.First);
    }

    [Fact]
    public void Unknown_ProducesUnknownTypeRefNode()
    {
        var typeRef = GetVariableTypeRef("unknown");
        _ = Assert.IsType<UnknownSingleTypeRefSingleTypeRefNode>(typeRef.First);
    }

    [Fact]
    public void ParenthesizedUnionArray_ProducesParenthesizedTypeWithInnerUnionAndArrayRank()
    {
        // `(int | string)[]` is a ParenthesizedTypeRef SingleTypeRef with an array
        // rank. Because a top-level TypeReference has no direct parenthesized
        // alternative, the parenthesized form is only reachable as a union
        // constituent, so we observe it via `(int | string)[] | bool`.
        var typeRef = GetVariableTypeRef("(int | string)[] | bool");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = new List<SingleTypeRefNode> { typeRef.First };
        constituents.AddRange(typeRef.SingleTypeRefs);
        Assert.Equal(2, constituents.Count);

        // First constituent: (int | string)[] — a ParenthesizedTypeRef with array rank.
        var parenRef = Assert.IsType<ParenthesizedTypeRefSingleTypeRefNode>(constituents[0]);
        Assert.NotNull(parenRef.ArrayRank);

        // Inner type is itself a union: int | string with 2 constituents.
        Assert.NotEmpty(parenRef.Inner.SingleTypeRefs);
        Assert.Equal(2, UnionConstituentCount(parenRef.Inner));

        // Second constituent: bool.
        var boolRef = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[1]);
        _ = Assert.IsType<BoolTypePrimitiveTypeNode>(boolRef.PrimitiveType);
    }
}