// Feature: as-operator, Property 1: As expression round-trip
// **Validates: Requirements 1.1, 1.2, 1.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

public static class MistAsExpressionGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private static readonly string[] IdentifierNames = ["x", "y", "z", "obj", "item"];
    private static readonly string[] TypeNames = ["Animal", "Dog", "Shape", "Circle", "Widget"];

    public static Gen<TypeReferenceNode> GenNamedTypeRef()
    {
        return Gen.Elements(TypeNames).Select(name =>
            new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
                },
            });
    }

    public static Gen<TypeReferenceNode> GenAsTargetType()
    {
        return Gen.OneOf(
            GenNamedTypeRef(),
            MistTypeGenerator.GenPrimitiveTypeRef());
    }

    private static AdditiveNode MakeIdentifierAdditive(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        return new AdditiveNode { Left = mult };
    }

    private static AdditiveNode MakeIntLiteralAdditive(string value)
    {
        var primary = new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(value) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        return new AdditiveNode { Left = mult };
    }

    private static AdditiveNode MakeBinaryAdditive(string leftName, string rightName)
    {
        var leftPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(leftName) };
        var leftPostfix = new PostfixNode
        {
            Target = leftPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var leftUnary = new PostfixExpressionUnaryNode { Primary = leftPostfix };
        var leftMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = leftUnary } };

        var rightPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(rightName) };
        var rightPostfix = new PostfixNode
        {
            Target = rightPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var rightUnary = new PostfixExpressionUnaryNode { Primary = rightPostfix };
        var rightMult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = rightUnary } };

        return new AdditiveNode { Left = leftMult, Group0 = ImmutableArray.Create(new AdditiveNodeGroup0 { Operator = Tok("+"), Right = rightMult }) };
    }

    public static Gen<AdditiveNode> GenLeftOperand()
    {
        var identifier = Gen.Elements(IdentifierNames).Select(MakeIdentifierAdditive);
        var intLiteral = Gen.Choose(0, 999).Select(n => MakeIntLiteralAdditive(n.ToString()));
        var binaryAdd = from left in Gen.Elements(IdentifierNames)
                        from right in Gen.Elements(IdentifierNames)
                        select MakeBinaryAdditive(left, right);

        return Gen.OneOf(identifier, intLiteral, binaryAdd);
    }

    private static ExpressionNode WrapAsExpression(AdditiveNode left, TypeReferenceNode targetType)
    {
        var rel = new AsExpressionRelationalNode { Left = left, TargetType = targetType };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    public static Gen<StatementNode> GenAsExpressionStatement()
    {
        return from left in GenLeftOperand()
               from targetType in GenAsTargetType()
               select MistAstGenerator.MakeExpressionStatement(WrapAsExpression(left, targetType));
    }

    public static Gen<CompilationUnitNode> GenSingleAsExpressionProgram()
    {
        return from stmt in GenAsExpressionStatement()
               select MistProgramGenerator.MakeProgram(ImmutableArray.Create(stmt));
    }

    public static Gen<CompilationUnitNode> GenMultipleAsExpressionProgram()
    {
        return from stmts in GenAsExpressionStatement().ListOf()
                   .Where(list => list.Count is >= 2 and <= 4)
               select MistProgramGenerator.MakeProgram(stmts.ToImmutableArray());
    }

    public static Gen<CompilationUnitNode> GenAsExpressionProgram()
    {
        return Gen.OneOf(
            GenSingleAsExpressionProgram(),
            GenMultipleAsExpressionProgram());
    }
}