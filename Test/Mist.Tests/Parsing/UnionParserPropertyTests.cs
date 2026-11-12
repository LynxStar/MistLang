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

public sealed class UnionParseScenario
{
    public ImmutableArray<string> Constituents { get; }

    public UnionParseScenario(ImmutableArray<string> constituents)
    {
        Constituents = constituents;
    }

    public override string ToString()
    {
        return string.Join(" | ", Constituents);
    }
}

public class UnionParserPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    /// <summary>
    /// Includes both plain types and array types (array rank binds tighter than |).
    /// </summary>
    private static readonly string[] KnownTypes =
        ["int", "string", "bool", "float", "double", "int[]", "string[]"];

    public static Arbitrary<UnionParseScenario> ArbitraryUnionParseScenario()
    {
        var gen =
            from count in Gen.Choose(2, 5)
            from types in Gen.ArrayOf(Gen.Elements(KnownTypes), count)
            select new UnionParseScenario(types.ToImmutableArray());

        return gen.ToArbitrary();
    }

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

    private static TypeReferenceNode GetVariableTypeRef(string typeAnnotation)
    {
        var source = $"class T {{ public void M() {{ {typeAnnotation} x = default; }} }}";
        var cu = ParseOrFail(source);

        var typeDecl = (BodyTypeTypeDeclarationNode)cu.GetTypes()[0];
        var methodMember = (MethodMemberMemberDeclarationNode)typeDecl.TypeBody.Members[0];
        var block = methodMember.Method.Body.GetBlock() ?? throw new InvalidOperationException("Method body block is null");
        var stmt = (VariableDeclarationStatementStatementNode)block.Statements[0];
        var varDecl = (TypedVariableDeclarationVariableDeclarationStatementNode)stmt.VariableDeclaration;
        return varDecl.Type;
    }

    private static List<SingleTypeRefNode> GetAllConstituents(TypeReferenceNode typeRef)
    {
        var constituents = new List<SingleTypeRefNode> { typeRef.First };
        constituents.AddRange(typeRef.SingleTypeRefs);
        return constituents;
    }

    private static string GetTypeName(SingleTypeRefNode node)
    {
        if(node is PrimitiveSingleTypeRefSingleTypeRefNode prim)
        {
            var name = EmitPrimitiveName(prim.PrimitiveType);
            return prim.ArrayRank is not null ? name + "[]" : name;
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionParserPropertyTests) })]
    public bool UnionType_HasExactlyNConstituents(UnionParseScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var typeRef = GetVariableTypeRef(typeAnnotation);

        var constituents = GetAllConstituents(typeRef);

        return constituents.Count == scenario.Constituents.Length;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionParserPropertyTests) })]
    public bool UnionType_ConstituentsAreInSourceOrder(UnionParseScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var typeRef = GetVariableTypeRef(typeAnnotation);

        var constituents = GetAllConstituents(typeRef);

        if(constituents.Count != scenario.Constituents.Length)
        {
            return false;
        }

        for(var i = 0; i < scenario.Constituents.Length; i++)
        {
            var expected = scenario.Constituents[i];
            var actual = GetTypeName(constituents[i]);
            if(actual != expected)
            {
                return false;
            }
        }

        return true;
    }
}