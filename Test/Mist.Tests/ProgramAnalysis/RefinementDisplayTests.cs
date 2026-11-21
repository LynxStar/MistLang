using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RefinementDisplayTests
{
    [Fact]
    public void ACallReadsAsItsCalleeAndArguments()
    {
        var h = new HandBuiltRefinements();
        var zeroes = new Method("Zeroes", h.Sequence(), [new Parameter("n", h.Int)], new MemberModifiers());
        var call = new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), zeroes),
            [HandBuiltRefinements.Local(h.Int, "n")], h.Sequence(), zeroes, [h.Int]);

        Assert.Equal("Zeroes(n).Length", RefinementFindings.Display(h.Length(call)));
    }

    [Theory]
    [InlineData("Postfix", "Add", "k++")]
    [InlineData("Prefix", "Subtract", "--k")]
    [InlineData("None", "Modulo", "k %= 1")]
    public void AnIncrementOrCompoundAssignmentReadsAsWritten(string form, string kind, string expected)
    {
        var h = new HandBuiltRefinements();
        var step = new CompoundAssignmentExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(h.Int, "k"), Enum.Parse<BinaryOperatorKind>(kind), h.Literal(1L), h.Int, null,
            Enum.Parse<IncrementForm>(form));

        Assert.Equal(expected, RefinementFindings.Display(step));
    }

    [Theory]
    [InlineData("Array", "new int[3]")]
    [InlineData("Span", "stackalloc int[3]")]
    public void ACreationReadsAsWritten(string sequence, string expected)
    {
        Assert.Equal(expected, RefinementFindings.Display(new HandBuiltRefinements().Array(3, sequence)));
    }

    [Fact]
    public void ADivisionGroupsTheSumItDivides()
    {
        var h = new HandBuiltRefinements();
        var sum = h.Binary(HandBuiltRefinements.Local(h.Int, "x"), BinaryOperatorKind.Add, h.Literal(1L), h.Int);

        Assert.Equal("(x + 1) / 4", RefinementFindings.Display(h.Binary(sum, BinaryOperatorKind.Divide, h.Literal(4L), h.Int)));
    }
}