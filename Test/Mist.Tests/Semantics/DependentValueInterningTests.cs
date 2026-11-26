using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class DependentValueInterningTests
{
    private readonly TypeInterner interner = new();
    private readonly ClassType packDomain = HandBuiltModel.Class("TypeArray");
    private readonly ClassType integer = HandBuiltModel.Class("Int32");
    private readonly ClassType text = HandBuiltModel.Class("String");

    [Fact]
    public void SeparatePacksWithTheSameOrderedElementsLiftToOneValue()
    {
        var first = interner.LiftPack(packDomain, new TypePackValue([integer, text]));
        var second = interner.LiftPack(packDomain, new TypePackValue([integer, text]));

        Assert.Same(first, second);
    }

    [Fact]
    public void ReorderedPacksLiftToDistinctValues()
    {
        var forward = interner.LiftPack(packDomain, new TypePackValue([integer, text]));
        var reversed = interner.LiftPack(packDomain, new TypePackValue([text, integer]));

        Assert.NotSame(forward, reversed);
    }

    [Fact]
    public void ALongerPackWithTheSamePrefixLiftsToADistinctValue()
    {
        var prefix = interner.LiftPack(packDomain, new TypePackValue([integer, text]));
        var extended = interner.LiftPack(packDomain, new TypePackValue([integer, text, text]));

        Assert.NotSame(prefix, extended);
    }

    [Fact]
    public void PacksWithNamesakeElementsLiftToDistinctValues()
    {
        var namesake = HandBuiltModel.Class("Int32");
        var originalPack = new TypePackValue([text, integer]);
        var namesakePack = new TypePackValue([text, namesake]);

        Assert.False(originalPack.Equals(namesakePack));
        Assert.NotSame(interner.LiftPack(packDomain, originalPack), interner.LiftPack(packDomain, namesakePack));
    }

    [Fact]
    public void PackEqualityDistinguishesElementOrder()
    {
        var forward = new TypePackValue([integer, text]);
        var reversed = new TypePackValue([text, integer]);

        Assert.False(forward.Equals(reversed));
    }

    [Fact]
    public void PackEqualityDistinguishesAPrefixFromItsExtension()
    {
        var prefix = new TypePackValue([integer, text]);
        var extended = new TypePackValue([integer, text, text]);

        Assert.False(prefix.Equals(extended));
    }

    [Fact]
    public void UnknownValuesAreReusedOnlyForTheSameDomainInstance()
    {
        var firstDomain = HandBuiltModel.Class("Length");
        var secondDomain = HandBuiltModel.Class("Length");
        var first = interner.GetOrCreateUnknownValue(firstDomain);
        var second = interner.GetOrCreateUnknownValue(secondDomain);

        Assert.NotSame(first, second);
        Assert.Same(first, interner.GetOrCreateUnknownValue(firstDomain));
        Assert.Same(second, interner.GetOrCreateUnknownValue(secondDomain));
    }
}