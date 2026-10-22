using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ForExpressionInitializerBindingTests
{
    private readonly BindingFragments fragments = new();

    public ForExpressionInitializerBindingTests()
    {
        fragments.Register(Int);
        fragments.Register(Bool);
    }

    private IMistType Int => fragments.Built.Type("Int32");

    private IMistType Bool => fragments.Built.Type("Boolean");

    [Fact]
    public void AnExpressionInitializerIsBoundOnceAsAStatementAheadOfTheCondition()
    {
        var counter = Assert.IsType<LocalReferenceExpression>(fragments.Local("i", Int)).Local;
        var running = Assert.IsType<LocalReferenceExpression>(fragments.Local("running", Bool)).Local;
        var initializer = new ForInitializerExpressionForInitializerNode { Expression = Assign("i", Integer("0")) };

        var loop = Assert.IsType<ForStatement>(StatementBinder.BindStatement(new ForStatementStatementNode
        {
            ForStatement = new ForStatementNode
            {
                Initializer = initializer,
                Condition = BindingFragments.Expression(BindingFragments.Identifier("running")),
                Body = new BlockNode(),
            },
        }, fragments.Binding, fragments.Builder));

        var statement = Assert.IsType<ExpressionStatement>(loop.Initializer);
        var assignment = Assert.IsType<AssignmentExpression>(statement.Expression);
        Assert.Same(counter, Assert.IsType<LocalReferenceExpression>(assignment.Target).Local);
        Assert.Equal(0, Convert.ToInt32(Assert.IsType<LiteralExpression>(assignment.Value).Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Same(running, Assert.IsType<LocalReferenceExpression>(loop.Condition).Local);
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnExpressionInitializerDeclaresNoLoopLocal()
    {
        var counter = Assert.IsType<LocalReferenceExpression>(fragments.Local("i", Int)).Local;
        var read = BindingFragments.Expression(BindingFragments.Identifier("i"));
        var body = new BlockNode { Statements = [new ExpressionStatementStatementNode { ExpressionStatement = new ExpressionStatementNode { Expression = read } }] };

        var loop = Assert.IsType<ForStatement>(StatementBinder.BindStatement(new ForStatementStatementNode
        {
            ForStatement = new ForStatementNode
            {
                Initializer = new ForInitializerExpressionForInitializerNode { Expression = Assign("i", Integer("3")) },
                Body = body,
            },
        }, fragments.Binding, fragments.Builder));

        var inBody = Assert.IsType<ExpressionStatement>(Assert.Single(loop.Body.Statements));
        Assert.Same(counter, Assert.IsType<LocalReferenceExpression>(inBody.Expression).Local);
    }

    private static ExpressionNode Assign(string local, ExpressionNode value)
    {
        var assignment = BindingFragments.Expression(BindingFragments.Identifier(local)).AsAssignmentOrNull()!;
        assignment.Group0 = new AssignmentNodeGroup0 { Operator = BindingFragments.Token("="), Value = value };
        return new AssignmentFormExpressionNode { Assignment = assignment };
    }

    private static ExpressionNode Integer(string lexeme)
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token(lexeme);
        return BindingFragments.Expression(literal);
    }
}