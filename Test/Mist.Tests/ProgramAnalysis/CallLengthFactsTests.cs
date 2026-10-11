// Tier 1: a call's declared result type carries its length as a typed local's does, and an unsized result invents none.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class CallLengthFactsTests
{
    [Theory]
    [InlineData("Array", 3L, 3L)]
    [InlineData("Span", 4L, 4L)]
    [InlineData("ReadOnlySpan", 2L, 2L)]
    [InlineData("Array", null, null)]
    [InlineData("Span", null, null)]
    public void ADirectCallKeepsOnlyItsDeclaredLength(string sequence, long? declared, long? expected)
    {
        var h = new HandBuiltRefinements();

        Assert.Equal(expected is { } length ? Admitted.Exactly(length) : null, Lengths.Of(Call(h.Sequence(sequence, declared)), _ => null));
    }

    [Fact]
    public void AnAliasOfASizedResultKeepsItsLength()
    {
        var h = new HandBuiltRefinements();
        var alias = new TypeAlias("Three", "Probe", null, null, null!) { AliasedType = h.Sequence("Array", 3L) };

        Assert.Equal(Admitted.Exactly(3), Lengths.Of(Call(alias), _ => null));
    }

    private static InvocationExpression Call(IMistType returns)
    {
        var method = new Method("Make", returns, [], new MemberModifiers());
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], returns, method, []);
    }
}