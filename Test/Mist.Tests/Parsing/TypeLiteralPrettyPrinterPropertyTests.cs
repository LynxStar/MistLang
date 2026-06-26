// Feature: type-literals, Property 9: Pretty printer round-trip for type literals

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for pretty printer round-trip with type literals.
/// Validates that parse → print → parse produces an equivalent AST for
/// programs containing type literal expressions.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
/// </summary>
public class TypeLiteralPrettyPrinterPropertyTests
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

    // ─── Test input model ────────────────────────────────────────────────

    /// <summary>
    /// Represents a test case: a Mist program containing a type literal in a specific position.
    /// </summary>
#pragma warning disable CA1034
    public record RoundTripTestCase(string Keyword, string PositionLabel, string Source)
    {
        public override string ToString()
        {
            return $"{Keyword} in {PositionLabel}";
        }
    }
#pragma warning restore CA1034

    // ─── Source builders for expression positions ─────────────────────────

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

    // ─── FsCheck Arbitrary ───────────────────────────────────────────────

    /// <summary>
    /// Generates random RoundTripTestCase values by combining a random primitive
    /// type keyword with a random expression position.
    /// </summary>
    public static Arbitrary<RoundTripTestCase> ArbitraryRoundTripTestCase()
    {
        var gen =
            from keyword in Gen.Elements(PrimitiveTypeKeywords)
            from posIndex in Gen.Choose(0, ExpressionPositions.Length - 1)
            let pos = ExpressionPositions[posIndex]
            select new RoundTripTestCase(keyword, pos.Label, pos.Builder(keyword));

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

    /// <summary>
    /// Extracts a comparable descriptor from a TypeLiteralExpressionPrimaryNode:
    /// the primitive type node type name and whether it has array rank.
    /// </summary>
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

    // ═════════════════════════════════════════════════════════════════════
    // Property 9: Pretty printer round-trip for type literals
    //
    // **Validates: Requirements 6.1, 6.2, 6.3, 6.4**
    //
    // For any valid Mist source with type literals, parse → print → parse
    // SHALL produce an equivalent AST (same TypeLiteralExpression nodes
    // with same type references).
    // ═════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TypeLiteralPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_RoundTrip_PreservesTypeLiterals(RoundTripTestCase testCase)
    {
        // Step 1: Parse the original source to get AST1
        var ast1 = ParseSource(testCase.Source);
        if(ast1 is null)
        {
            return false;
        }

        // Step 2: Pretty-print AST1 to get new source text
        var printed = MistPrettyPrinter.Print(ast1);
        if(string.IsNullOrWhiteSpace(printed))
        {
            return false;
        }

        // Step 3: Parse the pretty-printed source to get AST2
        var ast2 = ParseSource(printed);
        if(ast2 is null)
        {
            return false;
        }

        // Step 4: Find TypeLiteralExpression nodes in both ASTs
        var literals1 = FindTypeLiterals(ast1);
        var literals2 = FindTypeLiterals(ast2);

        // Both ASTs must have the same number of type literal nodes
        if(literals1.Count != literals2.Count)
        {
            return false;
        }

        // Step 5: Verify each TypeLiteralExpression is structurally equivalent
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