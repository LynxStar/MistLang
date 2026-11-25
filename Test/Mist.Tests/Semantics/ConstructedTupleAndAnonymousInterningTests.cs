using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Constructions and tuples are keyed on the identity of each type they are built from, in order, so a
/// second instance that merely shares a name is a different argument; <c>NamesakeInteger</c> is that
/// instance. Every distinctness case is checked against an identical request that does reuse.
/// </summary>
[Trait("Tier", "1")]
public sealed class ConstructedTupleAndAnonymousInterningTests
{
    private static readonly ClassType Integer = new("Int32", "Standard", new Scope(null), null, null);
    private static readonly ClassType NamesakeInteger = new("Int32", "Other", new Scope(null), null, null);
    private static readonly ClassType Text = new("String", "Standard", new Scope(null), null, null);
    private static readonly ClassType Pair = new("Pair<>", "Probe", new Scope(null), null, null);

    [Fact]
    public void AConstructionWithItsArgumentsSwapped_IsAnotherTypeAndBothAreListedOnce()
    {
        var interner = new TypeInterner();
        var forward = interner.GetOrCreateConstructedType(Pair, [Integer, Text]);

        var swapped = interner.GetOrCreateConstructedType(Pair, [Text, Integer]);

        Assert.Same(forward, interner.GetOrCreateConstructedType(Pair, [Integer, Text]));
        Assert.NotSame(forward, swapped);
        Assert.Equal([Text, Integer], swapped.TypeArguments);
        Assert.Equal(2, interner.ConstructedTypes.Count);
        Assert.Contains(forward, interner.ConstructedTypes);
        Assert.Contains(swapped, interner.ConstructedTypes);
    }

    [Fact]
    public void AConstructionOverANamesakeArgument_IsAnotherType()
    {
        var interner = new TypeInterner();
        var original = interner.GetOrCreateConstructedType(Pair, [Integer, Text]);

        var namesake = interner.GetOrCreateConstructedType(Pair, [NamesakeInteger, Text]);

        Assert.NotSame(original, namesake);
        Assert.Same(NamesakeInteger, namesake.TypeArguments[0]);
    }

    [Fact]
    public void ANestedConstructionUnderAnInstantiation_IsDistinctFromTheSameConstructionWithNoEnclosing()
    {
        var interner = new TypeInterner();
        var outer = new ClassType("Outer<>", "Probe", new Scope(null), null, null);
        var inner = new ClassType("Inner<>", "Probe", new Scope(null), outer, null);
        var outerOfText = interner.GetOrCreateConstructedType(outer, [Text]);
        var bare = interner.GetOrCreateConstructedType(inner, [Integer]);

        var enclosed = interner.GetOrCreateConstructedType(inner, [Integer], outerOfText);

        Assert.Same(bare, interner.GetOrCreateConstructedType(inner, [Integer]));
        Assert.NotSame(bare, enclosed);
        Assert.Null(bare.Enclosing);
        Assert.Same(outerOfText, enclosed.Enclosing);
    }

    [Fact]
    public void ATupleOfTheSameElementInstances_IsReused()
    {
        var interner = new TypeInterner();
        var first = interner.GetOrCreateTupleType([Integer, Text]);

        var second = interner.GetOrCreateTupleType([Integer, Text]);

        Assert.Same(first, second);
    }

    [Theory]
    [InlineData("swapped")]
    [InlineData("shorter")]
    [InlineData("longer")]
    [InlineData("namesake")]
    public void ATupleDifferingByOrderLengthOrElementIdentity_IsAnotherType(string variant)
    {
        var interner = new TypeInterner();
        var original = interner.GetOrCreateTupleType([Integer, Text]);
        IMistType[] elements = variant switch
        {
            "swapped" => [Text, Integer],
            "shorter" => [Integer],
            "longer" => [Integer, Text, Integer],
            "namesake" => [NamesakeInteger, Text],
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };

        var other = interner.GetOrCreateTupleType(elements);

        Assert.NotSame(original, other);
        Assert.Equal(elements, other.ElementTypes);
    }

    [Fact]
    public void AnAnonymousTypeWhoseMemberHasAnotherType_IsAnotherType()
    {
        var interner = new TypeInterner();
        var ofInteger = interner.GetOrCreateAnonymousType(["X"], [Integer]);

        var ofText = interner.GetOrCreateAnonymousType(["X"], [Text]);

        Assert.Same(ofInteger, interner.GetOrCreateAnonymousType(["X"], [Integer]));
        Assert.NotSame(ofInteger, ofText);
        Assert.Same(Text, Assert.Single(ofText.Fields).Type);
    }
}