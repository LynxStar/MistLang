using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class StorageRecursionIdentityTests
{
    [Fact]
    public void DescendsThroughAResolvedAlias()
    {
        var box = GenericStruct("Box<>");
        var inner = new ConstructedType(box, [HandBuiltModel.Struct("Leaf")]);
        var alias = new TypeAlias("LeafBox", "Probe", null, null, new TypeAliasDeclarationNode())
        {
            AliasedType = inner,
        };
        var outer = new ConstructedType(box, [alias]);
        var recursion = new StorageRecursion();

        Assert.True(recursion.Enter(box, outer));
        Assert.True(recursion.Enter(alias, alias));
        Assert.True(recursion.Enter(box, inner));
    }

    [Fact]
    public void RefusesDescendantFromADifferentGenericDefinition()
    {
        var box = GenericStruct("Box<>");
        var other = GenericStruct("Other<>");
        var leaf = HandBuiltModel.Struct("Leaf");
        var outer = new ConstructedType(box, [new ConstructedType(other, [leaf])]);
        var candidate = new ConstructedType(box, [leaf]);
        var recursion = new StorageRecursion();

        Assert.True(recursion.Enter(box, outer));
        Assert.False(recursion.Enter(box, candidate));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DescendsThroughAPackAndPreservesElementOrder(bool sameOrder)
    {
        var box = GenericStruct("Box<>");
        var first = HandBuiltModel.Struct("First");
        var second = HandBuiltModel.Struct("Second");
        var array = new ClassType("Array<,>", "Standard", null, null, null);
        var type = new ClassType("Type", "Standard", null, null, null);
        var integer = new StructType("Int32", "Standard", null, null, null);
        var domain = new ConstructedType(array, [type, new LiftedType(integer, 2L)]);
        var pair = HandBuiltModel.Struct("Pair<>");
        pair.TypeParametersList.Add(new TypeParameter("TArgs", 0, pair.Scope, pair)
        {
            Domain = domain,
            IsDependent = true,
            PackKind = TypePackKind.Unbracketed,
            PackLength = 2,
        });

        var inner = closePair(new ConstructedType(box, [first]), second);
        var outer = closePair(inner, second);
        var rebuiltElement = new ConstructedType(box, [first]);
        var candidate = sameOrder ? closePair(rebuiltElement, second) : closePair(second, rebuiltElement);
        var recursion = new StorageRecursion();

        Assert.True(recursion.Enter(pair, outer));
        Assert.Equal(sameOrder, recursion.Enter(pair, candidate));

        ConstructedType closePair(IMistType left, IMistType right)
        {
            return new ConstructedType(pair, [new LiftedType(domain, new TypePackValue([left, right]))]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MatchesTupleArgumentsByElementTypeRegardlessOfLabels(bool sameElements)
    {
        var box = GenericStruct("Box<>");
        var first = HandBuiltModel.Struct("First");
        var second = HandBuiltModel.Struct("Second");
        var storedTuple = new NamedTupleType(new TupleType([first, second]), ["left", "right"]);
        var candidateTuple = new NamedTupleType(new TupleType([first, sameElements ? second : first]), ["start", "end"]);
        var outer = new ConstructedType(box, [new ConstructedType(box, [storedTuple])]);
        var candidate = new ConstructedType(box, [candidateTuple]);
        var recursion = new StorageRecursion();

        Assert.True(recursion.Enter(box, outer));
        Assert.Equal(sameElements, recursion.Enter(box, candidate));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DescendsThroughEnclosingArgumentsAndDistinguishesTheirIdentity(bool sameEnclosingArgument)
    {
        var outer = GenericStruct("Outer<>");
        var inner = new StructType("Inner", "Probe", outer.Scope, outer, null);
        var leaf = HandBuiltModel.Struct("Leaf");
        var descendant = closeInner(leaf);
        var ancestor = closeInner(descendant);
        var candidate = closeInner(sameEnclosingArgument ? leaf : HandBuiltModel.Struct("Other"));
        var recursion = new StorageRecursion();

        Assert.True(recursion.Enter(inner, ancestor));
        Assert.Equal(sameEnclosingArgument, recursion.Enter(inner, candidate));

        ConstructedType closeInner(IMistType argument)
        {
            return new ConstructedType(inner, [], new ConstructedType(outer, [argument]));
        }
    }

    private static StructType GenericStruct(string name)
    {
        var type = HandBuiltModel.Struct(name);
        type.TypeParametersList.Add(new TypeParameter("T", 0, type.Scope, type));
        return type;
    }
}