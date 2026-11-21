using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class DeclaredZeroTests
{
    private readonly GenericDefaultShapes shapes = new();

    private readonly StructType integer = new("Int32", "Standard", new Scope(null), null, null)
    {
        Modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true },
    };

    [Fact]
    public void StructWithNoInitializerOrBlock_Holds()
    {
        Assert.True(DeclaredZero.Holds(Plain("Zero")));
    }

    [Fact]
    public void FieldInitializer_DoesNotHold()
    {
        var seven = shapes.Type("Seven", isStruct: true);
        _ = Slot(seven, "Y", integer, HandBuiltModel.Literal(integer, 7L));

        Assert.False(DeclaredZero.Holds(seven));
    }

    [Fact]
    public void DefaultBlockEntry_DoesNotHold()
    {
        var blocked = Plain("Blocked");
        Block(blocked, new DefaultValueEntry("Y", HandBuiltModel.Literal(integer, 5L)));

        Assert.False(DeclaredZero.Holds(blocked));
    }

    [Fact]
    public void FieldOfANonZeroStruct_DoesNotHold()
    {
        var seven = shapes.Type("Seven", isStruct: true);
        _ = Slot(seven, "Y", integer, HandBuiltModel.Literal(integer, 7L));
        var outer = shapes.Type("Outer", isStruct: true);
        _ = Slot(outer, "Inner", seven);

        Assert.False(DeclaredZero.Holds(outer));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Construction_HoldsExactlyWhenItsArgumentDoes(bool zeroArgument)
    {
        var argument = Plain("Argument");

        if(!zeroArgument)
        {
            argument.FieldsList[0].Initializer = HandBuiltModel.Literal(integer, 7L);
        }

        Assert.Equal(zeroArgument, DeclaredZero.Holds(Construct(shapes.Box(isStruct: true), argument)));
    }

    [Fact]
    public void OpenTypeParameter_DoesNotHold()
    {
        Assert.False(DeclaredZero.Holds(Parameter(shapes.Type("Owner", isStruct: true))));
    }

    [Fact]
    public void IntrinsicStruct_Holds()
    {
        Assert.True(DeclaredZero.Holds(integer));
    }

    private ConcreteType Plain(string name)
    {
        var type = shapes.Type(name, isStruct: true);
        _ = Slot(type, "Y", integer);
        return type;
    }
}