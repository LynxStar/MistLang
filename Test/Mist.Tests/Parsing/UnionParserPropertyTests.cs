// Feature: type-unions, Property 2: Union Type Parsing Correctness
// **Validates: Requirements 2.2, 2.3, 2.4, 3.1, 3.2, 3.3, 3.4, 3.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a generated union type parsing scenario with N constituents.
/// </summary>
public sealed class UnionParseScenario
{
    public ImmutableArray<string> Constituents { get; }

    public UnionParseScenario(ImmutableArray<string> constituents)
    {
        Constituents = constituents;
    }

    public override string ToString() =>
        string.Join(" | ", Constituents);
}

/// <summary>
/// Property-based tests for union type parsing correctness.
///
/// Property 2: For any valid union type source string composed of N constituent type
/// references separated by |, parsing SHALL produce a UnionTypeRefNode whose constituent
/// list has exactly N elements in source order, with array rank binding tighter than |.
/// </summary>
public class UnionParserPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known types for generation ─────────────────────────────────────────

    /// <summary>
    /// Valid primitive type names that the parser recognizes as SingleTypeRef constituents.
    /// Includes both plain types and array types (array rank binds tighter than |).
    /// </summary>
    private static readonly string[] KnownTypes =
        ["int", "string", "bool", "float", "double", "int[]", "string[]"];

    // ─── Generator ──────────────────────────────────────────────────────────

    public static Arbitrary<UnionParseScenario> ArbitraryUnionParseScenario()
    {
        var gen =
            from count in Gen.Choose(2, 5)
            from types in Gen.ArrayOf(Gen.Elements(KnownTypes), count)
            select new UnionParseScenario(types.ToImmutableArray());

        return gen.ToArbitrary();
    }

    // ─── Parsing helpers ────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    private static TypeReferenceNode GetVariableTypeRef(string typeAnnotation)
    {
        var source = $"class T {{ public void M() {{ {typeAnnotation} x = default; }} }}";
        var cu = ParseOrFail(source);

        var typeDecl = (ClassTypeTypeDeclarationNode)cu.GetTypes()[0];
        var methodMember = (MethodMemberMemberDeclarationNode)typeDecl.ClassDeclaration.Members[0];
        var block = methodMember.Method.Body.GetBlock();
        if (block is null)
            throw new InvalidOperationException("Method body block is null");
        var stmt = (VariableDeclarationStatementStatementNode)block.Statements[0];
        var varDecl = (TypedVariableDeclarationVariableDeclarationStatementNode)stmt.VariableDeclaration;
        return varDecl.Type;
    }

    private static List<SingleTypeRefNode> GetAllConstituents(UnionTypeRefNode unionNode)
    {
        var constituents = new List<SingleTypeRefNode> { unionNode.First };
        constituents.AddRange(unionNode.SingleTypeRefs);
        return constituents;
    }

    /// <summary>
    /// Extracts the type name string from a SingleTypeRefNode for source order verification.
    /// </summary>
    private static string GetTypeName(SingleTypeRefNode node)
    {
        if (node is PrimitiveSingleTypeRefSingleTypeRefNode prim)
        {
            var name = EmitPrimitiveName(prim.PrimitiveType);
            return prim.ArrayRank.HasValue ? name + "[]" : name;
        }

        return "unknown";
    }

    private static string EmitPrimitiveName(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
        {
            IntTypePrimitiveTypeNode => "int",
            StringTypePrimitiveTypeNode => "string",
            BoolTypePrimitiveTypeNode => "bool",
            FloatTypePrimitiveTypeNode => "float",
            DoubleTypePrimitiveTypeNode => "double",
            _ => "unknown",
        };
    }

    // ─── Property Tests ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 2a: A union type with N constituents produces a UnionTypeRefNode
    /// with exactly N elements.
    ///
    /// **Validates: Requirements 2.2, 2.3, 2.4, 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionParserPropertyTests) })]
    public bool UnionType_HasExactlyNConstituents(UnionParseScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var typeRef = GetVariableTypeRef(typeAnnotation);

        var unionTypeRef = (UnionTypeRefTypeReferenceNode)typeRef;
        var unionNode = unionTypeRef.UnionType;
        var constituents = GetAllConstituents(unionNode);

        return constituents.Count == scenario.Constituents.Length;
    }

    /// <summary>
    /// Property 2b: The constituents of the parsed UnionTypeRefNode are in source order —
    /// the i-th constituent in the AST corresponds to the i-th type in the source string.
    ///
    /// **Validates: Requirements 3.3, 3.4, 3.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionParserPropertyTests) })]
    public bool UnionType_ConstituentsAreInSourceOrder(UnionParseScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var typeRef = GetVariableTypeRef(typeAnnotation);

        var unionTypeRef = (UnionTypeRefTypeReferenceNode)typeRef;
        var unionNode = unionTypeRef.UnionType;
        var constituents = GetAllConstituents(unionNode);

        if (constituents.Count != scenario.Constituents.Length)
            return false;

        for (var i = 0; i < scenario.Constituents.Length; i++)
        {
            var expected = scenario.Constituents[i];
            var actual = GetTypeName(constituents[i]);
            if (actual != expected)
                return false;
        }

        return true;
    }
}
