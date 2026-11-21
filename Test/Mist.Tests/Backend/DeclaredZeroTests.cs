using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
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

    [Theory]
    [InlineData(0L)]
    [InlineData(false)]
    [InlineData(0.0)]
    [InlineData('\0')]
    public void LiteralZeroInitializer_Holds(object zero)
    {
        var initialized = shapes.Type("Initialized", isStruct: true);
        _ = Slot(initialized, "Y", integer, HandBuiltModel.Literal(integer, zero));

        Assert.True(DeclaredZero.Holds(initialized));
    }

    [Theory]
    [InlineData(-0.0)]
    [InlineData(true)]
    [InlineData('a')]
    public void LiteralThatSetsABit_DoesNotHold(object constant)
    {
        var initialized = shapes.Type("Initialized", isStruct: true);
        _ = Slot(initialized, "Y", integer, HandBuiltModel.Literal(integer, constant));

        Assert.False(DeclaredZero.Holds(initialized));
    }

    [Fact]
    public void DefaultBlockEntryOfZero_Holds()
    {
        var blocked = Plain("Blocked");
        Block(blocked, new DefaultValueEntry("Y", HandBuiltModel.Literal(integer, 0L)));

        Assert.True(DeclaredZero.Holds(blocked));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("-0")]
    [InlineData("1 - 1")]
    [InlineData("(int)0")]
    [InlineData("(int)(2 * 3 - 6)")]
    public void ConstantZeroInitializer_Holds(string shape)
    {
        var initialized = shapes.Type("Initialized", isStruct: true);
        _ = Slot(initialized, "Y", integer, Constant(shape));

        Assert.True(DeclaredZero.Holds(initialized));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("3 + 4")]
    [InlineData("(int)7")]
    public void ConstantThatFoldsToNonZero_DoesNotHold(string shape)
    {
        var initialized = shapes.Type("Initialized", isStruct: true);
        _ = Slot(initialized, "Y", integer, Constant(shape));

        Assert.False(DeclaredZero.Holds(initialized));
    }

    [Fact]
    public void DefaultBlockEntryFoldingToZero_Holds()
    {
        var blocked = Plain("Blocked");
        Block(blocked, new DefaultValueEntry("Y", Constant("1 - 1")));

        Assert.True(DeclaredZero.Holds(blocked));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultOfAStructField_HoldsExactlyWhenTheFieldsTypeDoes(bool zeroField)
    {
        var inner = Plain("Inner");

        if(!zeroField)
        {
            inner.FieldsList[0].Initializer = HandBuiltModel.Literal(integer, 7L);
        }

        var outer = shapes.Type("Outer", isStruct: true);
        _ = Slot(outer, "Inner", inner, new DefaultExpression(HandBuiltModel.Node(), inner));

        Assert.Equal(zeroField, DeclaredZero.Holds(outer));
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

    private Expression Constant(string shape)
    {
        return shape switch
        {
            "default" => new DefaultExpression(HandBuiltModel.Node(), integer),
            "-0" => new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, Number(0), integer),
            "-1" => new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, Number(1), integer),
            "1 - 1" => Binary(Number(1), BinaryOperatorKind.Subtract, Number(1)),
            "3 + 4" => Binary(Number(3), BinaryOperatorKind.Add, Number(4)),
            "(int)0" => Cast(Number(0)),
            "(int)7" => Cast(Number(7)),
            "(int)(2 * 3 - 6)" => Cast(Binary(Binary(Number(2), BinaryOperatorKind.Multiply, Number(3)), BinaryOperatorKind.Subtract, Number(6))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private LiteralExpression Number(long value)
    {
        return HandBuiltModel.Literal(integer, value);
    }

    private BinaryExpression Binary(Expression left, BinaryOperatorKind kind, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, kind, right, integer);
    }

    private CastExpression Cast(Expression operand)
    {
        return new CastExpression(HandBuiltModel.Node(), operand, integer, [], integer);
    }

    private ConcreteType Plain(string name)
    {
        var type = shapes.Type(name, isStruct: true);
        _ = Slot(type, "Y", integer);
        return type;
    }
}