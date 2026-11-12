// Feature: type-literals, Property 9: Pretty printer round-trip for type literals

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
/// </summary>
public class TypeLiteralPrettyPrinterPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    private static readonly string[] PrimitiveTypeKeywords =
    [
        "int", "uint", "long", "ulong", "short", "ushort",
        "byte", "sbyte", "float", "double", "decimal",
        "bool", "char", "string"
    ];

#pragma warning disable CA1034
    public record RoundTripTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString()
        {
            return $"{Keyword} in {PositionLabel}";
        }
    }
#pragma warning restore CA1034

    private static string BuildVarInitializer(string keyword)
    {
        return $"class Program {{\n    public static void Main() {{\n        var x = {keyword};\n    }}\n}}";
    }

    private static string BuildMethodArgument(string keyword)
    {
        return $"class Program {{\n    public static void Main() {{\n        Foo({keyword});\n    }}\n    static void Foo(int t) {{\n    }}\n}}";
    }

    private static string BuildReturnValue(string keyword)
    {
        return $"class Program {{\n    public static int GetValue() {{\n        return {keyword};\n    }}\n    public static void Main() {{\n    }}\n}}";
    }

    private static string BuildAssignmentRhs(string keyword)
    {
        return $"class Program {{\n    public static void Main() {{\n        var x = int;\n        x = {keyword};\n    }}\n}}";
    }

    private delegate string SourceBuilder(string keyword);

    private static readonly (string Label, SourceBuilder Builder)[] ExpressionPositions =
    [
        ("var initializer", BuildVarInitializer),
        ("method argument", BuildMethodArgument),
        ("return value", BuildReturnValue),
        ("assignment RHS", BuildAssignmentRhs),
    ];

    public static Arbitrary<RoundTripTestCase> ArbitraryRoundTripTestCase()
    {
        var gen =
            from keyword in Gen.Elements(PrimitiveTypeKeywords)
            from posIndex in Gen.Choose(0, ExpressionPositions.Length - 1)
            let pos = ExpressionPositions[posIndex]
            select new RoundTripTestCase(keyword, pos.Label, pos.Builder(keyword));

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

    private static (string TypeNodeName, bool IsArray) DescribeTypeLiteral(TypeLiteralExpressionPrimaryNode tl)
    {
        if(tl.TypeRef.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef)
        {
            return (primitiveRef.PrimitiveType.GetType().Name, primitiveRef.ArrayRank is not null);
        }

        if(tl.TypeRef.First is NamedSingleTypeRefSingleTypeRefNode namedRef)
        {
            return (namedRef.Name.IDENTIFIER.Lexeme, namedRef.ArrayRank is not null);
        }

        if(tl.TypeRef.First is VoidSingleTypeRefSingleTypeRefNode)
        {
            return ("VoidTypeRef", false);
        }

        return ("Unknown", false);
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_RoundTrip_PreservesTypeLiterals(RoundTripTestCase testCase)
    {
        var ast1 = ParseSource(testCase.Source);
        if(ast1 is null)
        {
            return false;
        }

        var printed = MistPrettyPrinter.Print(ast1);
        if(string.IsNullOrWhiteSpace(printed))
        {
            return false;
        }

        var ast2 = ParseSource(printed);
        if(ast2 is null)
        {
            return false;
        }

        var literals1 = FindTypeLiterals(ast1);
        var literals2 = FindTypeLiterals(ast2);

        if(literals1.Count != literals2.Count)
        {
            return false;
        }

        for(var i = 0; i < literals1.Count; i++)
        {
            var desc1 = DescribeTypeLiteral(literals1[i]);
            var desc2 = DescribeTypeLiteral(literals2[i]);

            if(desc1 != desc2)
            {
                return false;
            }
        }

        return true;
    }
}