// variadics, task 4.3: constructed variadic type identity (smoke check)
//
// Requirements: 5.1, 5.2, 5.3, 5.4
//
// A constructed variadic type is identified by the pair (family, the comptime Type[]
// value bound to its pack slot). Identity is order-sensitive and arity-sensitive via
// elementwise Type[] equality, reusing the interner's reference-identity relation for
// type arguments. These smoke checks exercise TypeInterner directly (no process spawn);
// the full FsCheck property test lives in task 4.4.

using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// Verifies that a constructed variadic type — a family applied to a comptime
/// <c>Type[]</c> value in its pack slot — has identity keyed by (family, that
/// <c>Type[]</c> value), with elementwise, order-sensitive, arity-sensitive equality.
/// </summary>
public sealed class ConstructedVariadicTypeIdentityTests
{
    private static ClassType NamedType(string name)
    {
        return new ClassType(name, "Standard", new Scope(null), null, null);
    }

    [Fact]
    public void SameFamily_EqualTypeArray_ReturnsSameReference()
    {
        var interner = new TypeInterner();
        var foo = NamedType("Foo<>");
        var intType = NamedType("Int32");
        var stringType = NamedType("String");

        // Foo<[int, string]> constructed twice from independent (but elementwise-equal)
        // Type[] values must resolve to the same interned constructed type.
        var first = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(new IMistType[] { intType, stringType }) });
        var second = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(new IMistType[] { intType, stringType }) });

        Assert.True(ReferenceEquals(first, second),
            "Foo<[int, string]> built twice from equal Type[] values should be the same type (R5.1, R5.2).");
    }

    [Fact]
    public void SameFamily_DifferentOrder_ReturnsDistinct()
    {
        var interner = new TypeInterner();
        var foo = NamedType("Foo<>");
        var intType = NamedType("Int32");
        var stringType = NamedType("String");

        var intThenString = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(new IMistType[] { intType, stringType }) });
        var stringThenInt = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(new IMistType[] { stringType, intType }) });

        Assert.False(ReferenceEquals(intThenString, stringThenInt),
            "Foo<[int, string]> and Foo<[string, int]> are distinct: order matters (R5.3).");
    }

    [Fact]
    public void SameFamily_DifferentArity_ReturnsDistinct()
    {
        var interner = new TypeInterner();
        var foo = NamedType("Foo<>");
        var intType = NamedType("Int32");
        var stringType = NamedType("String");
        var boolType = NamedType("Boolean");

        var two = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(new IMistType[] { intType, stringType }) });
        var three = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(new IMistType[] { intType, stringType, boolType }) });
        var empty = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(Array.Empty<IMistType>()) });

        // Foo<[int, string]>, Foo<[int, string, bool]>, and Foo<[]> are pairwise distinct (R5.4).
        Assert.False(ReferenceEquals(two, three),
            "Different pack arity (2 vs 3) must be distinct types.");
        Assert.False(ReferenceEquals(two, empty),
            "A non-empty pack and the empty pack must be distinct types.");
        Assert.False(ReferenceEquals(three, empty),
            "Different pack arity (3 vs 0) must be distinct types.");
    }

    [Fact]
    public void EmptyPack_EqualAcrossConstructions_ReturnsSameReference()
    {
        var interner = new TypeInterner();
        var foo = NamedType("Foo<>");

        var first = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(Array.Empty<IMistType>()) });
        var second = interner.GetOrCreateConstructedType(
            foo, new IMistType[] { interner.GetOrCreateLiftedTypeArray(Array.Empty<IMistType>()) });

        Assert.True(ReferenceEquals(first, second),
            "The empty-pack construction Foo<[]> must intern to a single type (R5.4).");
    }

    [Fact]
    public void DifferentFamily_SameTypeArray_ReturnsDistinct()
    {
        var interner = new TypeInterner();
        var foo = NamedType("Foo<>");
        var bar = NamedType("Bar<>");
        var intType = NamedType("Int32");
        var stringType = NamedType("String");

        var packValue = interner.GetOrCreateLiftedTypeArray(new IMistType[] { intType, stringType });
        var fooConstruction = interner.GetOrCreateConstructedType(foo, new IMistType[] { packValue });
        var barConstruction = interner.GetOrCreateConstructedType(bar, new IMistType[] { packValue });

        Assert.False(ReferenceEquals(fooConstruction, barConstruction),
            "Foo<[int, string]> and Bar<[int, string]> are distinct: identity includes the family (R5.2).");
    }
}