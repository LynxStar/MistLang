using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Construction;

// Tier 1: storage typed by an element of a fixed-size pack (`TArgs[0] First;`) is defaulted as storage
// typed by a plain type parameter is: the declaration for some closing, a construction for its own, and
// each element the storage zeroes owing a zero of its own. `Pair` declares `Second` before `First`.
[Trait("Tier", "1")]
public sealed class PackElementDefaultabilityTests
{
    private readonly GenericDefaultShapes shapes = new();

    [Fact]
    public void EachElementStorageZeroes_OwesAZeroInIndexOrder()
    {
        var (pair, pack) = Pair();

        Assert.True(Defaultability.IsDefaultable(pair));
        Assert.Equal([pack.ElementAt(0), pack.ElementAt(1)], Defaultability.ZeroNeeded(pair));
    }

    [Fact]
    public void AnElementNoStorageZeroes_OwesNone()
    {
        var first = shapes.Type("First");
        var pack = Pack(first, "TArgs");
        _ = Slot(first, "Count", shapes.Integer);
        _ = Slot(first, "Head", pack.ElementAt(0));

        Assert.Equal([pack.ElementAt(0)], Defaultability.ZeroNeeded(first));
    }

    [Theory]
    [InlineData("intrinsic", "intrinsic", true)]
    [InlineData("intrinsic", "inner", true)]
    [InlineData("intrinsic", "connection", false)]
    [InlineData("connection", "intrinsic", false)]
    public void AConstruction_HasADefaultExactlyWhenEachElementItsStorageZeroesHasOne(string first, string second, bool expected)
    {
        var (pair, _) = Pair();

        Assert.Equal(expected, Defaultability.HasDefault(Construct(pair, Closing(Argument(first), Argument(second)))));
    }

    [Theory]
    [InlineData("intrinsic", true)]
    [InlineData("connection", false)]
    public void AHolderSpreadingItsOwnPackIntoTheConstruction_OwesItsOwnElements(string second, bool expected)
    {
        var (pair, _) = Pair();
        var holder = shapes.Type("Holder");
        var both = Pack(holder, "TBoth");
        _ = Slot(holder, "Inner", Construct(pair, Closing(new SpreadType(both))));

        Assert.Equal([both.ElementAt(0), both.ElementAt(1)], Defaultability.ZeroNeeded(holder));
        Assert.Equal(expected, Defaultability.HasDefault(Construct(holder, Closing(shapes.Integer, Argument(second)))));
    }

    [Fact]
    public void AnElement_AnswersWithItsPacksConstraintsWheneverTheyAreDeclared()
    {
        var (_, pack) = Pair();
        var element = pack.ElementAt(0);

        Assert.False(Defaultability.HasDefault(element));

        pack.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));

        Assert.True(Defaultability.HasDefault(element));
    }

    private (ConcreteType Pair, TypeParameter Pack) Pair()
    {
        var pair = shapes.Type("Pair", isStruct: true);
        var pack = Pack(pair, "TArgs");
        _ = Slot(pair, "Second", pack.ElementAt(1));
        _ = Slot(pair, "First", pack.ElementAt(0));

        return (pair, pack);
    }

    private static TypeParameter Pack(ConcreteType owner, string name)
    {
        var pack = Parameter(owner, name);
        pack.PackKind = TypePackKind.Unbracketed;
        pack.PackLength = 2;

        return pack;
    }

    private static LiftedType Closing(params IMistType[] elements)
    {
        return new LiftedType(HandBuiltModel.Class("Type"), new TypePackValue(elements));
    }

    private IMistType Argument(string name)
    {
        return name switch
        {
            "intrinsic" => shapes.Integer,
            "inner" => shapes.Inner(),
            _ => shapes.Connection(),
        };
    }
}