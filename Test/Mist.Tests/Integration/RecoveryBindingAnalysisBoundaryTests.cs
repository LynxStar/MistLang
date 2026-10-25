using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Integration;

[Trait("Tier", "4")]
public sealed class RecoveryBindingAnalysisBoundaryTests
{
    [Fact]
    public void AnUnreportedOperatorResultSurvivesBindingForItsAnalysisRefusal()
    {
        var f = new BindingFragments();
        _ = f.Local("box", HandBuiltModel.Class("Box"));
        _ = f.Local("one", f.Built.Type("Int32"));
        var initializer = MistAstGenerator.MakeAdditive(
            BindingFragments.Expression(BindingFragments.Identifier("box")),
            BindingFragments.Expression(BindingFragments.Identifier("one")));
        var additive = ((RelationalComparisonRelationalNode)((AssignmentFormExpressionNode)initializer)
            .Assignment.Target.Left.Left.Left.Left).Left;
        var location = HandBuiltModel.Node(37);
        additive.Span = location.Span;
        additive.Origin = location.Origin;

        var statement = Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(
            new VariableDeclarationStatementStatementNode
            {
                VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
                {
                    Name = BindingFragments.Token("result"),
                    Initializer = initializer,
                },
            }, f.Binding, f.Builder));

        _ = Assert.IsType<BinaryExpression>(statement.Initializer);
        _ = Assert.IsType<ErrorType>(statement.Local.Type);
        Assert.False(statement.Local.IsFailed);
        Assert.False(PriorFailure.Of(f.Bind(BindingFragments.Identifier("result"))));
        Assert.Empty(f.Diagnostics.ToImmutableArray());

        var diagnostic = Assert.Single(f.Built.Analyze(new OperatorAnalyzer(), statement));
        Assert.Equal("MIST_PA_OPERATOR_NOT_APPLICABLE", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }
}