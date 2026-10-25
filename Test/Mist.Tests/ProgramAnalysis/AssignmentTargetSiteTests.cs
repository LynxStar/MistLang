using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AssignmentTargetSiteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAssignmentToAValueIsRefusedAtItsTarget(bool coalescing)
    {
        var h = new HandBuiltRefinements();
        var assignment = new AssignmentExpression(HandBuiltModel.Node(3), Abs(h, offset: 37), h.Literal(3L), h.Int, coalescing);

        var diagnostic = Assert.Single(h.Built.Analyze(new PositionAnalyzer(), HandBuiltRefinements.Evaluate(assignment)));

        Assert.Equal("MIST0210", diagnostic.Number.Value);
        Assert.StartsWith("Cannot assign to the result of 'Abs':", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }

    [Fact]
    public void AnAssignmentToStorageIsNotRefusedForItsTarget()
    {
        var h = new HandBuiltRefinements();
        var assignment = HandBuiltRefinements.Assign(HandBuiltRefinements.Local(h.Int), h.Literal(3L));

        Assert.Empty(h.Built.Analyze(new PositionAnalyzer(), HandBuiltRefinements.Evaluate(assignment)));
    }

    [Fact]
    public void AValueTargetOwesTheValueNoConversion()
    {
        var h = new HandBuiltRefinements();
        var assignment = HandBuiltRefinements.Assign(Abs(h), h.Literal("text", h.String));

        Assert.Empty(h.Built.Analyze(new AssignabilityAnalyzer(), HandBuiltRefinements.Evaluate(assignment)));
    }

    [Fact]
    public void AValueTargetOwesTheValueNoProof()
    {
        var h = new HandBuiltRefinements();
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var next = new InvocationExpression(
            HandBuiltModel.Node(), HandBuiltModel.Literal(positive), [], positive, new Method("Next", positive, [], default), null);

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(next, h.Literal(-1L)))));
    }

    private static InvocationExpression Abs(HandBuiltRefinements h, int offset = 0)
    {
        return new InvocationExpression(
            HandBuiltModel.Node(offset), HandBuiltModel.Literal(h.Int), [], h.Int, new Method("Abs", h.Int, [], default), null);
    }
}