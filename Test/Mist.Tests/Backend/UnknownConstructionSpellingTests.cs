using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class UnknownConstructionSpellingTests
{
    [Fact]
    public void AnIntrinsicListClosedWithUnknownIsDynamic()
    {
        Assert.Equal("dynamic", TypeNameEmitter.Emit(ListOf(UnknownType.Instance)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ATwoArgumentIntrinsicWithEitherArgumentUnknownIsDynamic(bool unknownFirst)
    {
        var dictionary = Intrinsic("Dictionary<,>");
        var integer = new HandBuiltModel().Type("Int32");
        IMistType[] arguments = unknownFirst ? [UnknownType.Instance, integer] : [integer, UnknownType.Instance];

        Assert.Equal("dynamic", TypeNameEmitter.Emit(new ConstructedType(dictionary, arguments)));
    }

    [Fact]
    public void AnIntrinsicListContainingAListClosedWithUnknownIsDynamic()
    {
        Assert.Equal("dynamic", TypeNameEmitter.Emit(ListOf(ListOf(UnknownType.Instance))));
    }

    [Fact]
    public void ANonIntrinsicConstructionClosedWithUnknownIsDynamic()
    {
        var box = HandBuiltModel.Class("Box<>");

        Assert.Equal("dynamic", TypeNameEmitter.Emit(new ConstructedType(box, [UnknownType.Instance])));
    }

    [Fact]
    public void AnIntrinsicListWithoutUnknownKeepsItsClrSpelling()
    {
        var integer = new HandBuiltModel().Type("Int32");

        Assert.Equal("System.Collections.Generic.List<int>", TypeNameEmitter.Emit(ListOf(integer)));
    }

    [Fact]
    public void ABareUnknownIsObject()
    {
        Assert.Equal("object", TypeNameEmitter.Emit(UnknownType.Instance));
    }

    private static ConstructedType ListOf(IMistType element)
    {
        return new ConstructedType(Intrinsic("List<>"), [element]);
    }

    private static ClassType Intrinsic(string name)
    {
        return new ClassType(name, "Standard", new Scope(null), null, null)
        {
            Modifiers = new() { IsIntrinsic = true },
        };
    }
}