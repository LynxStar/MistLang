using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class LengthReadingDecisionTests
{
    private readonly HandBuiltRefinements h = new();

    [Theory]
    [InlineData("2", 3L)]
    [InlineData("0", 5L)]
    [InlineData("5", 0L)]
    [InlineData("6", null)]
    [InlineData("n", null)]
    [InlineData("2 of other", null)]
    [InlineData("1,2", 2L)]
    [InlineData("1,n", null)]
    public void ASpanSlice_IsTheRestOfItsSourceOrItsCount(string arguments, long? expected)
    {
        var parts = arguments.Split(' ');
        var source = HandBuiltRefinements.Local(h.Sequence("Span"), parts is [_, "of", var name] ? name : "values");
        var passed = parts[0].Split(',').Select(Argument).ToArray();

        Assert.Equal(expected is { } length ? Admitted.Exactly(length) : null, Lengths.Of(Slice(source, h.Sequence("Span"), passed), Tracked));
    }

    [Theory]
    [InlineData("2", 4L)]
    [InlineData("1,n", 4L)]
    public void ASliceWhoseLengthCannotBeComputed_KeepsTheLengthItsResultTypeStates(string arguments, long declared)
    {
        var passed = arguments.Split(',').Select(Argument).ToArray();
        var untracked = HandBuiltRefinements.Local(h.Sequence("Span"), "other");

        Assert.Equal(Admitted.Exactly(declared), Lengths.Of(Slice(untracked, h.Sequence("Span", declared), passed), Tracked));
    }

    [Fact]
    public void AClone_HasTheLengthOfWhatItCopies()
    {
        var clone = new CloneExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(h.Sequence("Array"), "values"));

        Assert.Equal(Admitted.Exactly(5), Lengths.Of(clone, Tracked));
    }

    [Theory]
    [InlineData(@"a\tb", 3L)]
    [InlineData(@"AB", 2L)]
    [InlineData(@"\q", null)]
    public void AStringLiteral_IsAsLongAsItsDecodedText(string body, long? expected)
    {
        Assert.Equal(expected is { } length ? Admitted.Exactly(length) : null, Lengths.Of(h.Literal(body), Tracked));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADirectRangeSlice_IsReadWhenNoStoredRangeIsKnown(bool rangesSupplied)
    {
        var slice = h.Sliced(HandBuiltRefinements.Local(h.Sequence("Array"), "values"), h.Slice(h.Literal(1L), h.Literal(4L)));

        Assert.Equal(Admitted.Exactly(3), Lengths.Of(slice, Tracked, rangesSupplied ? _ => null : null));
    }

    private Expression Argument(string text)
    {
        return text == "n" ? HandBuiltRefinements.Read(new LocalVariable("n", h.Int)) : h.Literal(long.Parse(text));
    }

    private static Admitted? Tracked(Expression expression)
    {
        return expression is LocalReferenceExpression { Local.Name: "values" } ? Admitted.Exactly(5) : null;
    }

    private InvocationExpression Slice(Expression receiver, IMistType returns, Expression[] arguments)
    {
        var method = new Method("Slice", returns, arguments.Select((_, i) => new Parameter($"p{i}", h.Int)).ToList(), new MemberModifiers { IsClrProjected = true })
        {
            SequenceLengthOperation = SequenceLengthOperation.Slice,
        };
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Slice", returns, member: new MethodGroupResult([method]));
        return new InvocationExpression(HandBuiltModel.Node(), access, arguments, returns, method, method.ParametersList.ConvertAll(parameter => parameter.Type));
    }
}