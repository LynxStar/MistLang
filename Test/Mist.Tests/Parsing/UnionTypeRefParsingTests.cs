// Feature: type-unions — Unit tests for parser UnionTypeRef production
// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests verifying that the parser produces a union <see cref="TypeReferenceNode"/>
/// (one with non-empty <c>SingleTypeRefs</c>) for union type annotations, with correct
/// constituent count and ordering.
/// </summary>
public class UnionTypeRefParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// Parses a source string and asserts no parse errors occurred.
    /// Returns the <see cref="CompilationUnitNode"/> root.
    /// </summary>
    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    /// <summary>
    /// Wraps a union type annotation in a variable declaration inside a class method,
    /// parses it, and returns the <see cref="TypeReferenceNode"/> for the variable's type.
    /// </summary>
    private static TypeReferenceNode GetVariableTypeRef(string typeAnnotation)
    {
        var source = $"class T {{ public void M() {{ {typeAnnotation} x = default; }} }}";
        var cu = ParseOrFail(source);

        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(
            typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(block!.Statements[0]);
        var varDecl = Assert.IsType<TypedVariableDeclarationVariableDeclarationStatementNode>(
            stmt.VariableDeclaration);
        return varDecl.Type;
    }

    /// <summary>
    /// Collects all constituent <see cref="SingleTypeRefNode"/> from a union
    /// <see cref="TypeReferenceNode"/>.
    /// The order is: First, then all elements from SingleTypeRefs (which are the rest constituents in source order).
    /// </summary>
    private static List<SingleTypeRefNode> GetAllConstituents(TypeReferenceNode typeRef)
    {
        var constituents = new List<SingleTypeRefNode> { typeRef.First };
        constituents.AddRange(typeRef.SingleTypeRefs);
        return constituents;
    }

    // ─── Test: int | string → 2 constituents (int, string) ──────────────

    [Fact]
    public void IntOrString_ProducesUnionTypeRefWithTwoConstituents()
    {
        var typeRef = GetVariableTypeRef("int | string");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        Assert.Equal(2, constituents.Count);
    }

    [Fact]
    public void IntOrString_FirstConstituentIsInt()
    {
        var typeRef = GetVariableTypeRef("int | string");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        var first = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[0]);
        _ = Assert.IsType<IntTypePrimitiveTypeNode>(first.PrimitiveType);
    }

    [Fact]
    public void IntOrString_SecondConstituentIsString()
    {
        var typeRef = GetVariableTypeRef("int | string");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        var second = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[1]);
        _ = Assert.IsType<StringTypePrimitiveTypeNode>(second.PrimitiveType);
    }

    // ─── Test: int | string | bool → 3 constituents ─────────────────────

    [Fact]
    public void IntOrStringOrBool_ProducesUnionTypeRefWithThreeConstituents()
    {
        var typeRef = GetVariableTypeRef("int | string | bool");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        Assert.Equal(3, constituents.Count);
    }

    [Fact]
    public void IntOrStringOrBool_ConstituentsAreInSourceOrder()
    {
        var typeRef = GetVariableTypeRef("int | string | bool");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        var first = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[0]);
        _ = Assert.IsType<IntTypePrimitiveTypeNode>(first.PrimitiveType);

        var second = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[1]);
        _ = Assert.IsType<StringTypePrimitiveTypeNode>(second.PrimitiveType);

        var third = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[2]);
        _ = Assert.IsType<BoolTypePrimitiveTypeNode>(third.PrimitiveType);
    }

    // ─── Test: int[] | string → array rank binds tighter than | ─────────

    [Fact]
    public void IntArrayOrString_ProducesUnionTypeRefWithTwoConstituents()
    {
        var typeRef = GetVariableTypeRef("int[] | string");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        Assert.Equal(2, constituents.Count);
    }

    [Fact]
    public void IntArrayOrString_FirstConstituentIsIntArray()
    {
        var typeRef = GetVariableTypeRef("int[] | string");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        // First constituent should be int[] (PrimitiveSingleTypeRef with ArrayRank set)
        var first = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[0]);
        _ = Assert.IsType<IntTypePrimitiveTypeNode>(first.PrimitiveType);
        Assert.NotNull(first.ArrayRank);
    }

    [Fact]
    public void IntArrayOrString_SecondConstituentIsString()
    {
        var typeRef = GetVariableTypeRef("int[] | string");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        var second = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[1]);
        _ = Assert.IsType<StringTypePrimitiveTypeNode>(second.PrimitiveType);
        Assert.Null(second.ArrayRank);
    }

    // ─── Test: (int | string)[] → parenthesized union with array rank ───

    [Fact]
    public void ParenthesizedUnionArray_ParsesAsParenthesizedTypeRefWithArrayRank()
    {
        // (int | string)[] is a ParenthesizedTypeRef SingleTypeRef with array rank.
        // Since it's a single SingleTypeRef without a top-level PIPE, it cannot be
        // a union at the top level. We need to use it within a union to test
        // the parenthesized form: (int | string)[] | bool
        var typeRef = GetVariableTypeRef("(int | string)[] | bool");

        Assert.NotEmpty(typeRef.SingleTypeRefs);
        var constituents = GetAllConstituents(typeRef);

        Assert.Equal(2, constituents.Count);

        // First constituent: (int | string)[] — a ParenthesizedTypeRef with array rank
        var parenRef = Assert.IsType<ParenthesizedTypeRefSingleTypeRefNode>(constituents[0]);
        Assert.NotNull(parenRef.ArrayRank);

        // The inner type of the parenthesized ref should be a union: int | string
        Assert.NotEmpty(parenRef.Inner.SingleTypeRefs);
        var innerConstituents = GetAllConstituents(parenRef.Inner);
        Assert.Equal(2, innerConstituents.Count);

        var innerFirst = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(innerConstituents[0]);
        _ = Assert.IsType<IntTypePrimitiveTypeNode>(innerFirst.PrimitiveType);

        var innerSecond = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(innerConstituents[1]);
        _ = Assert.IsType<StringTypePrimitiveTypeNode>(innerSecond.PrimitiveType);

        // Second constituent: bool
        var boolRef = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(constituents[1]);
        _ = Assert.IsType<BoolTypePrimitiveTypeNode>(boolRef.PrimitiveType);
    }
}