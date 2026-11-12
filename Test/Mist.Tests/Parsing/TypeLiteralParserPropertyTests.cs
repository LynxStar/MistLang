// Feature: type-literals, Property 1: Primitive type keywords in expression position produce TypeLiteralExpression
// Feature: type-literals, Property 2: Array type literals in expression position include array rank
// Feature: type-literals, Property 3: Type annotation positions are unaffected by type literal grammar

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;

namespace Mist.Tests.Parsing;

public class TypeLiteralParserPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static readonly string[] PrimitiveTypeKeywords =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string"
    ];

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

#pragma warning disable CA1034
    public record TypeLiteralTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString()
        {
            return $"{Keyword} in {PositionLabel}";
        }
    }
#pragma warning restore CA1034

    private static string BuildVarInitializer(string keyword)
    {
        return $"class Program {{ public static void Main() {{ var x = {keyword}; }} }}";
    }

    private static string BuildMethodArgument(string keyword)
    {
        return $"class Program {{ public static void Main() {{ Foo({keyword}); }} static void Foo(Type t) {{ }} }}";
    }

    private static string BuildReturnValue(string keyword)
    {
        return $"class Program {{ public static Type GetType() {{ return {keyword}; }} public static void Main() {{ }} }}";
    }

    private static string BuildAssignmentRhs(string keyword)
    {
        return $"class Program {{ public static void Main() {{ var x = bool; x = {keyword}; }} }}";
    }

    private delegate string SourceBuilder(string keyword);

    private static readonly (string Label, SourceBuilder Builder)[] ExpressionPositions =
    [
        ("var initializer", BuildVarInitializer),
        ("method argument", BuildMethodArgument),
        ("return value", BuildReturnValue),
        ("assignment RHS", BuildAssignmentRhs),
    ];

    public static Arbitrary<TypeLiteralTestCase> ArbitraryTypeLiteralTestCase()
    {
        var gen =
            from keyword in Gen.Elements(PrimitiveTypeKeywords)
            from posIndex in Gen.Choose(0, ExpressionPositions.Length - 1)
            let pos = ExpressionPositions[posIndex]
            select new TypeLiteralTestCase(keyword, pos.Label, pos.Builder(keyword));

        return gen.ToArbitrary();
    }

    private static CompilationUnitNode? ParseSource(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);
        return result.Root as CompilationUnitNode;
    }

    private static List<TypeLiteralExpressionPrimaryNode> FindTypeLiterals(Storm.Runtime.SyntaxNode node)
    {
        var results = new List<TypeLiteralExpressionPrimaryNode>();
        CollectTypeLiterals(node, results);
        return results;
    }

    private static void CollectTypeLiterals(Storm.Runtime.SyntaxNode? node, List<TypeLiteralExpressionPrimaryNode> results)
    {
        if(node is null)
        {
            return;
        }

        if(node is TypeLiteralExpressionPrimaryNode typeLiteral)
        {
            results.Add(typeLiteral);
        }

        try
        {
            foreach(var child in node.Children())
            {
                CollectTypeLiterals(child, results);
            }
        }
        catch(NullReferenceException)
        {
            // Some AST nodes may have null children that Children() doesn't guard against.
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 1: Primitive type keywords in expression position produce
    //             TypeLiteralExpression
    //
    // **Validates: Requirements 1.2, 2.2, 2.3, 2.4, 2.5**
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralParserPropertyTests) })]
    public bool PrimitiveTypeKeyword_InExpressionPosition_ProducesTypeLiteralExpression(
        TypeLiteralTestCase testCase)
    {
        var root = ParseSource(testCase.Source);

        if(root is null)
        {
            return false;
        }

        var typeLiterals = FindTypeLiterals(root);

        if(typeLiterals.Count == 0)
        {
            return false;
        }

        var expectedNodeType = KeywordToNodeType[testCase.Keyword];

        var matchingLiteral = typeLiterals.FirstOrDefault(tl =>
            tl.TypeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef &&
            primitiveRef.PrimitiveType.GetType() == expectedNodeType);

        if(matchingLiteral is null)
        {
            return false;
        }

        var typeRef = matchingLiteral.TypeRef.First as PrimitiveSingleTypeRefSingleTypeRefNode;
        return typeRef is not null && typeRef.PrimitiveType.GetType() == expectedNodeType;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 2: Array type literals in expression position include
    //             array rank
    //
    // **Validates: Requirements 1.4**
    // ═════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034
    public record ArrayTypeLiteralTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString()
        {
            return $"{Keyword}[] in {PositionLabel}";
        }
    }
#pragma warning restore CA1034

    private static string BuildArrayVarInitializer(string keyword)
    {
        return $"class Program {{ public static void Main() {{ var x = {keyword}[]; }} }}";
    }

    private static string BuildArrayMethodArgument(string keyword)
    {
        return $"class Program {{ public static void Main() {{ Foo({keyword}[]); }} static void Foo(Type t) {{ }} }}";
    }

    private static string BuildArrayReturnValue(string keyword)
    {
        return $"class Program {{ public static Type GetType() {{ return {keyword}[]; }} public static void Main() {{ }} }}";
    }

    private static string BuildArrayAssignmentRhs(string keyword)
    {
        return $"class Program {{ public static void Main() {{ var x = bool[]; x = {keyword}[]; }} }}";
    }

    private static readonly (string Label, SourceBuilder Builder)[] ArrayExpressionPositions =
    [
        ("var initializer", BuildArrayVarInitializer),
        ("method argument", BuildArrayMethodArgument),
        ("return value", BuildArrayReturnValue),
        ("assignment RHS", BuildArrayAssignmentRhs),
    ];

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

        if(root is null)
        {
            return false;
        }

        var typeLiterals = FindTypeLiterals(root);

        if(typeLiterals.Count == 0)
        {
            return false;
        }

        var expectedNodeType = KeywordToNodeType[testCase.Keyword];

        var matchingLiteral = typeLiterals.FirstOrDefault(tl =>
            tl.TypeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef &&
            primitiveRef.PrimitiveType.GetType() == expectedNodeType &&
            primitiveRef.ArrayRank is not null);

        return matchingLiteral is not null;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Property 3: Type annotation positions are unaffected by type literal
    //             grammar
    //
    // **Validates: Requirements 1.5**
    // ═════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034
    public record TypeAnnotationTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString()
        {
            return $"{Keyword} as {PositionLabel}";
        }
    }
#pragma warning restore CA1034

    private static string BuildParameterType(string keyword)
    {
        return $"class Program {{ public static void Foo({keyword} x) {{ }} public static void Main() {{ }} }}";
    }

    private static string BuildFieldType(string keyword)
    {
        return $"class Program {{ {keyword} myField; public static void Main() {{ }} }}";
    }

    private static string BuildReturnType(string keyword)
    {
        return $"class Program {{ public static {keyword} GetValue() {{ return 0; }} public static void Main() {{ }} }}";
    }

    private static string BuildTypedVarDecl(string keyword)
    {
        return $"class Program {{ public static void Main() {{ {keyword} x = 0; }} }}";
    }

    private static readonly (string Label, SourceBuilder Builder)[] AnnotationPositions =
    [
        ("parameter type", BuildParameterType),
        ("field type", BuildFieldType),
        ("return type", BuildReturnType),
        ("typed variable declaration", BuildTypedVarDecl),
    ];

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

        if(root is null)
        {
            return false;
        }

        var typeLiterals = FindTypeLiterals(root);

        var expectedNodeType = KeywordToNodeType[testCase.Keyword];

        var matchingLiteral = typeLiterals.FirstOrDefault(tl =>
            tl.TypeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef &&
            primitiveRef.PrimitiveType.GetType() == expectedNodeType);

        return matchingLiteral is null;
    }
}