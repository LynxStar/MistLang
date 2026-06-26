// Generators for Mist `as` expression AST nodes.
// Produces random but structurally valid programs containing `as` expressions
// with varying operand types and target type combinations for property-based testing.
// Feature: as-operator, Property 1: As expression round-trip
// **Validates: Requirements 1.1, 1.2, 1.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for Mist programs containing <c>as</c> expressions with
/// varying left operands (identifiers, literals, binary expressions) and target
/// types (primitives, named types). Each generated program is a complete
/// <see cref="CompilationUnitNode"/> with a class, a <c>static void Main</c>
/// method, and expression statements using <c>as</c>.
/// </summary>
public static class MistAsExpressionGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] IdentifierNames = ["x", "y", "z", "obj", "item"];
    private static readonly string[] TypeNames = ["Animal", "Dog", "Shape", "Circle", "Widget"];

    // ═══════════════════════════════════════════════════════════════════════
    // Target type generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a named type reference from the type name pool.
    /// </summary>
    public static Gen<TypeReferenceNode> GenNamedTypeRef()
    {
        return Gen.Elements(TypeNames).Select(name =>
            (TypeReferenceNode)new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
                },
            });
    }

    /// <summary>
    /// Generates a type reference suitable for `as` target types.
    /// Includes both named types and primitive types.
    /// </summary>
    public static Gen<TypeReferenceNode> GenAsTargetType()
    {
        return Gen.OneOf(
            GenNamedTypeRef(),
            MistTypeGenerator.GenPrimitiveTypeRef());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AdditiveNode operand generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates an AdditiveNode wrapping a simple identifier.
    /// </summary>
    private static AdditiveNode MakeIdentifierAdditive(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        return new AdditiveNode { Left = mult };
    }

    /// <summary>
    /// Creates an AdditiveNode wrapping an integer literal.
    /// </summary>
    private static AdditiveNode MakeIntLiteralAdditive(string value)
    {
        var primary = new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(value) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        return new AdditiveNode { Left = mult };
    }

    /// <summary>
    /// Creates an AdditiveNode representing a binary addition: left + right.
    /// </summary>
    private static AdditiveNode MakeBinaryAdditive(string leftName, string rightName)
    {
        var leftPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(leftName) };
        var leftPostfix = new PostfixNode
        {
            Target = leftPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var leftUnary = new PostfixExpressionUnaryNode { Primary = leftPostfix };
        var leftMult = new MultiplicativeNode { Left = leftUnary };

        var rightPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(rightName) };
        var rightPostfix = new PostfixNode
        {
            Target = rightPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var rightUnary = new PostfixExpressionUnaryNode { Primary = rightPostfix };
        var rightMult = new MultiplicativeNode { Left = rightUnary };

        return new AdditiveNode { Left = leftMult, Operator = Tok("+"), Right = rightMult };
    }

    /// <summary>
    /// Generates a random AdditiveNode operand for the left side of an `as` expression.
    /// </summary>
    public static Gen<AdditiveNode> GenLeftOperand()
    {
        var identifier = Gen.Elements(IdentifierNames).Select(MakeIdentifierAdditive);
        var intLiteral = Gen.Choose(0, 999).Select(n => MakeIntLiteralAdditive(n.ToString()));
        var binaryAdd = from left in Gen.Elements(IdentifierNames)
                        from right in Gen.Elements(IdentifierNames)
                        select MakeBinaryAdditive(left, right);

        return Gen.OneOf(identifier, intLiteral, binaryAdd);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // As expression wrapping
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps an <see cref="AdditiveNode"/> and a target type into a full
    /// <see cref="ExpressionNode"/> using an <see cref="AsExpressionRelationalNode"/>.
    /// </summary>
    private static ExpressionNode WrapAsExpression(AdditiveNode left, TypeReferenceNode targetType)
    {
        var rel = new AsExpressionRelationalNode { Left = left, TargetType = targetType };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Statement generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates an expression statement containing an `as` expression.
    /// </summary>
    public static Gen<StatementNode> GenAsExpressionStatement()
    {
        return from left in GenLeftOperand()
               from targetType in GenAsTargetType()
               select MistAstGenerator.MakeExpressionStatement(WrapAsExpression(left, targetType));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Complete program generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a program with a single `as` expression statement.
    /// </summary>
    public static Gen<CompilationUnitNode> GenSingleAsExpressionProgram()
    {
        return from stmt in GenAsExpressionStatement()
               select MistProgramGenerator.MakeProgram(ImmutableArray.Create(stmt));
    }

    /// <summary>
    /// Generates a program with multiple `as` expression statements.
    /// </summary>
    public static Gen<CompilationUnitNode> GenMultipleAsExpressionProgram()
    {
        return from stmts in GenAsExpressionStatement().ListOf()
                   .Where(list => list.Count >= 2 && list.Count <= 4)
               select MistProgramGenerator.MakeProgram(stmts.ToImmutableArray());
    }

    /// <summary>
    /// Generates a random CompilationUnitNode with `as` expression-related statements.
    /// </summary>
    public static Gen<CompilationUnitNode> GenAsExpressionProgram()
    {
        return Gen.OneOf(
            GenSingleAsExpressionProgram(),
            GenMultipleAsExpressionProgram());
    }
}