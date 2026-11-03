using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class AssignedLocalsScanTests
{
    private static ExpressionNode Name(string name)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(name));
    }

    private static ExpressionNode Write(string name)
    {
        var expression = (AssignmentFormExpressionNode)Name(name);
        expression.Assignment.Group0 = new AssignmentNodeGroup0
        {
            Operator = BindingFragments.Token("="),
            Value = Name("other"),
        };
        return expression;
    }

    [Fact]
    public void ReadsDoNotInvalidateProof()
    {
        Assert.Empty(OperandFacts.AssignedNames(Name("d")));
    }

    [Fact]
    public void ARefCallExposesTheOriginalLocation()
    {
        var reference = new RefArgumentArgumentNode { Expression = Name("d") };
        Assert.Contains("d", OperandFacts.AssignedNames(reference));
    }

    [Fact]
    public void ARefAliasExposesBothItsTargetAndWritesThroughTheAlias()
    {
        var alias = new TypedVariableDeclarationVariableDeclarationStatementNode
        {
            Name = BindingFragments.Token("alias"),
            Type = BindingFragments.TypeName("Int32"),
            OwnershipSpecifier = new RefSpecifierOwnershipSpecifierNode(),
            Initializer = Name("d"),
        };
        Assert.Contains("d", OperandFacts.AssignedNames(alias));
        Assert.Contains("alias", OperandFacts.AssignedNames(Write("alias")));
    }

    [Fact]
    public void AWriteInANestedInitializerIsSeen()
    {
        var declaration = new InferredVariableDeclarationVariableDeclarationStatementNode
        {
            Name = BindingFragments.Token("nested"),
            Initializer = Write("d"),
        };
        Assert.Contains("d", OperandFacts.AssignedNames(declaration));
    }

    [Fact]
    public void ALoopBackedgeWriteIsSeen()
    {
        var loop = new WhileStatementNode
        {
            Condition = Name("condition"),
            Body = new BlockNode
            {
                Statements = [new ExpressionStatementStatementNode
                {
                    ExpressionStatement = new ExpressionStatementNode { Expression = Write("d") },
                }],
            },
        };
        Assert.Contains("d", OperandFacts.AssignedNames(loop));
    }

    [Fact]
    public void ABranchWriteIsSeen()
    {
        var branch = new IfStatementNode
        {
            Condition = Name("condition"),
            ThenBranch = new BlockNode
            {
                Statements = [new ExpressionStatementStatementNode
                {
                    ExpressionStatement = new ExpressionStatementNode { Expression = Write("d") },
                }],
            },
        };
        Assert.Contains("d", OperandFacts.AssignedNames(branch));
    }
}