// Tier 1: a sequence length survives a call only through a resolved builtin operation, never through the spelling of a user method.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class SequenceLengthDecisionTests
{
    [Theory]
    [InlineData("AsSpan", "Array", "Span")]
    [InlineData("ToArray", "Array", "Array")]
    [InlineData("AsSpan", "String", "ReadOnlySpan")]
    public void AResolvedClrExtensionPreservesTheCapturedLength(string name, string source, string result)
    {
        var h = new HandBuiltRefinements();
        var call = Call(Receiver(h, source), Method(name, h.Sequence(result), SequenceLengthOperation.Preserved));

        Assert.Equal(Admitted.Exactly(3), Lengths.Of(call, Tracked));
    }

    [Theory]
    [InlineData("Slice", "Custom", "Array", 2)]
    [InlineData("ToArray", "Array", "Array", 0)]
    [InlineData("AsSpan", "Array", "Span", 0)]
    [InlineData("ToCharArray", "Array", "Array", 0)]
    public void AUserMethodNamedLikeABuiltinManufacturesNoLengthFact(string name, string source, string result, int arity)
    {
        var h = new HandBuiltRefinements();
        var arguments = Enumerable.Range(0, arity).Select(i => h.Literal(i == 0 ? 0L : 3L)).ToArray();
        var call = Call(Receiver(h, source), Method(name, h.Sequence(result), SequenceLengthOperation.None, parameters: [.. arguments.Select(_ => h.Int)]), arguments);

        Assert.Null(Lengths.Of(call, Tracked));
    }

    [Theory]
    [InlineData("Span")]
    [InlineData("ReadOnlySpan")]
    public void AnIntrinsicSpanOverAnArrayLiteralKeepsItsLength(string host)
    {
        var h = new HandBuiltRefinements();
        var span = Intrinsic(h, host);
        var literal = new ArrayLiteralExpression(HandBuiltModel.Node(), h.Int, [h.Literal(10L), h.Literal(20L), h.Literal(30L)], h.Sequence("Array", 3L));
        var constructor = new Constructor(host, span, [new Parameter("array", literal.Type)], new MemberModifiers { IsIntrinsic = true })
        {
            DeclaringType = (MemberType)span.Definition,
        };
        var creation = new NewExpression(HandBuiltModel.Node(), span, [literal], constructor, [], [literal.Type]);

        Assert.Equal(Admitted.Exactly(3), Lengths.Of(creation, Tracked));
    }

    [Theory]
    [InlineData("Span")]
    [InlineData("ReadOnlySpan")]
    public void AnIntrinsicSliceIsAsLongAsItsCount(string host)
    {
        var h = new HandBuiltRefinements();
        var span = Intrinsic(h, host);
        var slice = Method("Slice", span, SequenceLengthOperation.None, (MemberType)span.Definition, h.Int, h.Int);
        var call = Call(HandBuiltRefinements.Local(span, "values"), slice, h.Literal(1L), h.Literal(2L));

        Assert.Equal(Admitted.Exactly(2), Lengths.Of(call, Tracked));
    }

    [Fact]
    public void AnIntrinsicToCharArrayIsAsLongAsItsString()
    {
        var h = new HandBuiltRefinements();
        var text = (ClassType)h.String;
        text.Modifiers = text.Modifiers with { IsIntrinsic = true };
        var call = Call(h.Literal("abc"), Method("ToCharArray", h.Sequence("Array"), SequenceLengthOperation.None, text));

        Assert.Equal(Admitted.Exactly(3), Lengths.Of(call, Tracked));
    }

    private static Admitted? Tracked(Expression expression)
    {
        return expression is LocalReferenceExpression { Local.Name: "values" } ? Admitted.Exactly(3) : null;
    }

    private static Expression Receiver(HandBuiltRefinements h, string source)
    {
        return source switch
        {
            "String" => h.Literal("abc"),
            "Custom" => HandBuiltRefinements.Local(HandBuiltModel.Class("Custom"), "custom"),
            _ => HandBuiltRefinements.Local(h.Sequence("Array"), "values"),
        };
    }

    private static ConstructedType Intrinsic(HandBuiltRefinements h, string host)
    {
        var span = (ConstructedType)h.Sequence(host);
        var definition = (ClassType)span.Definition;
        definition.Modifiers = definition.Modifiers with { IsIntrinsic = true };
        return span;
    }

    private static Method Method(
        string name, IMistType returns, SequenceLengthOperation operation, MemberType? intrinsicHost = null, params IMistType[] parameters)
    {
        return new Method(
            name, returns, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(),
            new MemberModifiers { IsIntrinsic = intrinsicHost is not null, IsClrProjected = intrinsicHost is null })
        {
            SequenceLengthOperation = operation,
            DeclaringType = intrinsicHost,
        };
    }

    private static InvocationExpression Call(Expression receiver, Method method, params Expression[] arguments)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType, member: new MethodGroupResult([method]));
        return new InvocationExpression(HandBuiltModel.Node(), access, arguments, method.ReturnType, method, method.ParametersList.ConvertAll(parameter => parameter.Type));
    }
}