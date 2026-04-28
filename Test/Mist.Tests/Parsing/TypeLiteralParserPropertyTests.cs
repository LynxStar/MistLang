// Feature: type-literals, Property 1: Primitive type keywords in expression position produce TypeLiteralExpression
// Feature: type-literals, Property 2: Array type literals in expression position include array rank
// Feature: type-literals, Property 3: Type annotation positions are unaffected by type literal grammar

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for type literal parsing.
/// Validates that primitive type keywords in expression position produce
/// TypeLiteralExpression AST nodes containing the corresponding PrimitiveTypeRef.
///
/// **Validates: Requirements 1.2, 2.2, 2.3, 2.4, 2.5**
/// </summary>
public class TypeLiteralParserPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Primitive type keyword set ──────────────────────────────────────

    private static readonly string[] PrimitiveTypeKeywords =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string"
    ];

    /// <summary>
    /// Maps a primitive type keyword to the expected PrimitiveTypeNode subclass type.
    /// </summary>
    private static readonly Dictionary<string, Type> KeywordToNodeType = new()
    {
        ["int"] = typeof(IntTypePrimitiveTypeNode),
        ["uint"] = typeof(UintTypePrimitiveTypeNode),
        ["long"] = typeof(LongTypePrimitiveTypeNode),
        ["ulong"] = typeof(UlongTypePrimitiveTypeNode),
        ["short"] = typeof(ShortTypePrimitiveTypeNode),
        ["ushort"] = typeof(UshortTypePrimitiveTypeNode),
        ["byte"] = typeof(ByteTypePrimitiveTypeNode),
        ["sbyte"] = typeof(SbyteTypePrimitiveTypeNode),
        ["float"] = typeof(FloatTypePrimitiveTypeNode),
        ["double"] = typeof(DoubleTypePrimitiveTypeNode),
        ["decimal"] = typeof(DecimalTypePrimitiveTypeNode),
        ["bool"] = typeof(BoolTypePrimitiveTypeNode),
        ["char"] = typeof(CharTypePrimitiveTypeNode),
        ["string"] = typeof(StringTypePrimitiveTypeNode),
    };

    // ─── Test input model ────────────────────────────────────────────────

    /// <summary>
    /// Represents a test case: a primitive type keyword placed in a specific expression position.
    /// </summary>
#pragma warning disable CA1034
    public record TypeLiteralTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString() => $"{Keyword} in {PositionLabel}";
    }
#pragma warning restore CA1034

    // ─── Expression position source builders ─────────────────────────────

    private static string BuildVarInitializer(string keyword) =>
        $"class Program {{ public static void Main() {{ var x = {keyword}; }} }}";

    private static string BuildMethodArgument(string keyword) =>
        $"class Program {{ public static void Main() {{ Foo({keyword}); }} static void Foo(Type t) {{ }} }}";

    private static string BuildReturnValue(string keyword) =>
        $"class Program {{ public static Type GetType() {{ return {keyword}; }} public static void Main() {{ }} }}";

    private static string BuildAssignmentRhs(string keyword) =>
        $"class Program {{ public static void Main() {{ var x = bool; x = {keyword}; }} }}";

    private delegate string SourceBuilder(string keyword);

    private static readonly (string Label, SourceBuilder Builder)[] ExpressionPositions =
    [
        ("var initializer", BuildVarInitializer),
        ("method argument", BuildMethodArgument),
        ("return value", BuildReturnValue),
        ("assignment RHS", BuildAssignmentRhs),
    ];

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    /// <summary>
    /// Generates random TypeLiteralTestCase values by combining a random primitive
    /// type keyword with a random expression position.
    /// </summary>
    public static Arbitrary<TypeLiteralTestCase> ArbitraryTypeLiteralTestCase()
    {
        var gen =
            from keyword in Gen.Elements(PrimitiveTypeKeywords)
            from posIndex in Gen.Choose(0, ExpressionPositions.Length - 1)
            let pos = ExpressionPositions[posIndex]
            select new TypeLiteralTestCase(keyword, pos.Label, pos.Builder(keyword));

        return gen.ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);
        return result.Root as CompilationUnitNode;
    }

    /// <summary>
    /// Recursively finds all TypeLiteralExpressionPrimaryNode instances in the AST.
    /// </summary>
    private static List<TypeLiteralExpressionPrimaryNode> FindTypeLiterals(Storm.Runtime.SyntaxNode node)
    {
        var results = new List<TypeLiteralExpressionPrimaryNode>();
        CollectTypeLiterals(node, results);
        return results;
    }

    private static void CollectTypeLiterals(Storm.Runtime.SyntaxNode? node, List<TypeLiteralExpressionPrimaryNode> results)
    {
        if (node is null)
            return;

        if (node is TypeLiteralExpressionPrimaryNode typeLiteral)
        {
            results.Add(typeLiteral);
        }

        try
        {
            foreach (var child in node.Children())
            {
                CollectTypeLiterals(child, results);
            }
        }
        catch (NullReferenceException)
        {
            // Some AST nodes may have null children that Children() doesn't guard against.
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 1: Primitive type keywords in expression position produce
    //             TypeLiteralExpression
    //
    // **Validates: Requirements 1.2, 2.2, 2.3, 2.4, 2.5**
    //
    // For any primitive type keyword in any expression position, the parser
    // SHALL produce a TypeLiteralExpression AST node containing the
    // corresponding PrimitiveTypeRef.
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralParserPropertyTests) })]
    public bool PrimitiveTypeKeyword_InExpressionPosition_ProducesTypeLiteralExpression(
        TypeLiteralTestCase testCase)
    {
        var root = ParseSource(testCase.Source);

        // Parser must produce a non-null root
        if (root is null)
            return false;

        // Find TypeLiteralExpression nodes in the AST
        var typeLiterals = FindTypeLiterals(root);

        // There should be at least one TypeLiteralExpression
        if (typeLiterals.Count == 0)
            return false;

        // Find the type literal that matches our keyword's expected PrimitiveTypeNode
        var expectedNodeType = KeywordToNodeType[testCase.Keyword];

        var matchingLiteral = typeLiterals.FirstOrDefault(tl =>
            tl.TypeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef &&
            primitiveRef.PrimitiveType.GetType() == expectedNodeType);

        if (matchingLiteral is null)
            return false;

        // Verify the TypeRef is a PrimitiveTypeRef with the correct PrimitiveTypeNode
        var typeRef = matchingLiteral.TypeRef as PrimitiveTypeRefTypeReferenceNode;
        return typeRef is not null && typeRef.PrimitiveType.GetType() == expectedNodeType;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 2: Array type literals in expression position include
    //             array rank
    //
    // **Validates: Requirements 1.4**
    //
    // For any valid base type followed by [] in expression position, the
    // parser SHALL produce a TypeLiteralExpression AST node whose
    // TypeReference has the array rank set.
    // ═════════════════════════════════════════════════════════════════════

    // ─── Array test input model ──────────────────────────────────────────

    /// <summary>
    /// Represents a test case: a primitive type keyword followed by [] placed in a specific expression position.
    /// </summary>
#pragma warning disable CA1034
    public record ArrayTypeLiteralTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString() => $"{Keyword}[] in {PositionLabel}";
    }
#pragma warning restore CA1034

    // ─── Array expression position source builders ───────────────────────

    private static string BuildArrayVarInitializer(string keyword) =>
        $"class Program {{ public static void Main() {{ var x = {keyword}[]; }} }}";

    private static string BuildArrayMethodArgument(string keyword) =>
        $"class Program {{ public static void Main() {{ Foo({keyword}[]); }} static void Foo(Type t) {{ }} }}";

    private static string BuildArrayReturnValue(string keyword) =>
        $"class Program {{ public static Type GetType() {{ return {keyword}[]; }} public static void Main() {{ }} }}";

    private static string BuildArrayAssignmentRhs(string keyword) =>
        $"class Program {{ public static void Main() {{ var x = bool[]; x = {keyword}[]; }} }}";

    private static readonly (string Label, SourceBuilder Builder)[] ArrayExpressionPositions =
    [
        ("var initializer", BuildArrayVarInitializer),
        ("method argument", BuildArrayMethodArgument),
        ("return value", BuildArrayReturnValue),
        ("assignment RHS", BuildArrayAssignmentRhs),
    ];

    // ─── FsCheck Arbitrary for array type literals ───────────────────────

    /// <summary>
    /// Generates random ArrayTypeLiteralTestCase values by combining a random primitive
    /// type keyword with [] and a random expression position.
    /// </summary>
    public static Arbitrary<ArrayTypeLiteralTestCase> ArbitraryArrayTypeLiteralTestCase()
    {
        var gen =
            from keyword in Gen.Elements(PrimitiveTypeKeywords)
            from posIndex in Gen.Choose(0, ArrayExpressionPositions.Length - 1)
            let pos = ArrayExpressionPositions[posIndex]
            select new ArrayTypeLiteralTestCase(keyword, pos.Label, pos.Builder(keyword));

        return gen.ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralParserPropertyTests) })]
    public bool ArrayTypeLiteral_InExpressionPosition_IncludesArrayRank(
        ArrayTypeLiteralTestCase testCase)
    {
        var root = ParseSource(testCase.Source);

        // Parser must produce a non-null root
        if (root is null)
            return false;

        // Find TypeLiteralExpression nodes in the AST
        var typeLiterals = FindTypeLiterals(root);

        // There should be at least one TypeLiteralExpression
        if (typeLiterals.Count == 0)
            return false;

        // Find the type literal that matches our keyword's expected PrimitiveTypeNode AND has array rank
        var expectedNodeType = KeywordToNodeType[testCase.Keyword];

        var matchingLiteral = typeLiterals.FirstOrDefault(tl =>
            tl.TypeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef &&
            primitiveRef.PrimitiveType.GetType() == expectedNodeType &&
            primitiveRef.ArrayRank.HasValue);

        // The matching literal must exist and have ArrayRank set
        return matchingLiteral is not null;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 3: Type annotation positions are unaffected by type literal
    //             grammar
    //
    // **Validates: Requirements 1.5**
    //
    // For any type name in annotation position (parameter type, field type,
    // return type, typed variable declaration), the parser SHALL produce a
    // TypeReference node, NOT a TypeLiteralExpression.
    // ═════════════════════════════════════════════════════════════════════

    // ─── Annotation position test input model ────────────────────────────

    /// <summary>
    /// Represents a test case: a primitive type keyword placed in a type annotation position.
    /// </summary>
#pragma warning disable CA1034
    public record TypeAnnotationTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString() => $"{Keyword} as {PositionLabel}";
    }
#pragma warning restore CA1034

    // ─── Annotation position source builders ─────────────────────────────

    private static string BuildParameterType(string keyword) =>
        $"class Program {{ public static void Foo({keyword} x) {{ }} public static void Main() {{ }} }}";

    private static string BuildFieldType(string keyword) =>
        $"class Program {{ {keyword} myField; public static void Main() {{ }} }}";

    private static string BuildReturnType(string keyword) =>
        $"class Program {{ public static {keyword} GetValue() {{ return 0; }} public static void Main() {{ }} }}";

    private static string BuildTypedVarDecl(string keyword) =>
        $"class Program {{ public static void Main() {{ {keyword} x = 0; }} }}";

    private static readonly (string Label, SourceBuilder Builder)[] AnnotationPositions =
    [
        ("parameter type", BuildParameterType),
        ("field type", BuildFieldType),
        ("return type", BuildReturnType),
        ("typed variable declaration", BuildTypedVarDecl),
    ];

    // ─── FsCheck Arbitrary for annotation position tests ─────────────────

    /// <summary>
    /// Generates random TypeAnnotationTestCase values by combining a random primitive
    /// type keyword with a random type annotation position.
    /// </summary>
    public static Arbitrary<TypeAnnotationTestCase> ArbitraryTypeAnnotationTestCase()
    {
        var gen =
            from keyword in Gen.Elements(PrimitiveTypeKeywords)
            from posIndex in Gen.Choose(0, AnnotationPositions.Length - 1)
            let pos = AnnotationPositions[posIndex]
            select new TypeAnnotationTestCase(keyword, pos.Label, pos.Builder(keyword));

        return gen.ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralParserPropertyTests) })]
    public bool TypeAnnotationPosition_DoesNotProduceTypeLiteralExpression(
        TypeAnnotationTestCase testCase)
    {
        var root = ParseSource(testCase.Source);

        // Parser must produce a non-null root
        if (root is null)
            return false;

        // The AST should NOT contain any TypeLiteralExpression for the type
        // in annotation position. The type keyword should be parsed as a
        // TypeReference within the declaration node, not as a TypeLiteralExpression.
        var typeLiterals = FindTypeLiterals(root);

        // Find any TypeLiteralExpression that matches our keyword's PrimitiveTypeNode
        var expectedNodeType = KeywordToNodeType[testCase.Keyword];

        var matchingLiteral = typeLiterals.FirstOrDefault(tl =>
            tl.TypeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef &&
            primitiveRef.PrimitiveType.GetType() == expectedNodeType);

        // There should be NO TypeLiteralExpression for the keyword in annotation position
        return matchingLiteral is null;
    }
}
