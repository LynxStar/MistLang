using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
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

    private readonly StructType integer = Intrinsic("Int32");

    private readonly StructType boolean = Intrinsic("Boolean");

    private readonly StructType real = Intrinsic("Double");

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
    [InlineData("!true")]
    [InlineData("1 == 2")]
    [InlineData("false && x")]
    [InlineData("0.0 * 1.0")]
    [InlineData("0.0 - 0.0")]
    [InlineData("(double)(1 - 1)")]
    [InlineData("2147483647 + 2147483647 + 2")]
    public void ConstantComputingZero_Holds(string shape)
    {
        var (storage, value) = Computed(shape);
        var initialized = shapes.Type("Initialized", isStruct: true);
        _ = Slot(initialized, "Y", storage, value);

        Assert.True(DeclaredZero.Holds(initialized));
    }

    [Theory]
    [InlineData("!false")]
    [InlineData("1 == 1")]
    [InlineData("true || x")]
    [InlineData("-(0.0)")]
    [InlineData("0.0 * -1.0")]
    [InlineData("1 / 0")]
    [InlineData("x")]
    public void ConstantSettingABitOrUnknown_DoesNotHold(string shape)
    {
        var (storage, value) = Computed(shape);
        var initialized = shapes.Type("Initialized", isStruct: true);
        _ = Slot(initialized, "Y", storage, value);

        Assert.False(DeclaredZero.Holds(initialized));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(1L, false)]
    public void TupleLiteral_HoldsExactlyWhenEveryElementIsZero(long second, bool zero)
    {
        var pair = new TupleType([integer, real]);
        var tuple = new TupleLiteralExpression(HandBuiltModel.Node(), [Number(0), Convert(Number(second), real)], [null, null], pair);
        var initialized = shapes.Type("Initialized", isStruct: true);
        _ = Slot(initialized, "T", pair, tuple);

        Assert.Equal(zero, DeclaredZero.Holds(initialized));
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

    private (IMistType Storage, Expression Value) Computed(string shape)
    {
        return shape switch
        {
            "!true" => (boolean, Not(HandBuiltModel.Literal(boolean, true))),
            "!false" => (boolean, Not(HandBuiltModel.Literal(boolean, false))),
            "1 == 2" => (boolean, new BinaryExpression(HandBuiltModel.Node(), Number(1), BinaryOperatorKind.Equal, Number(2), boolean)),
            "1 == 1" => (boolean, new BinaryExpression(HandBuiltModel.Node(), Number(1), BinaryOperatorKind.Equal, Number(1), boolean)),
            "false && x" => (boolean, new BinaryExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(boolean, false), BinaryOperatorKind.LogicalAnd, Unknown(boolean), boolean)),
            "true || x" => (boolean, new BinaryExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(boolean, true), BinaryOperatorKind.LogicalOr, Unknown(boolean), boolean)),
            "0.0 * 1.0" => (real, Real(0.0, BinaryOperatorKind.Multiply, 1.0)),
            "0.0 - 0.0" => (real, Real(0.0, BinaryOperatorKind.Subtract, 0.0)),
            "0.0 * -1.0" => (real, Real(0.0, BinaryOperatorKind.Multiply, -1.0)),
            "-(0.0)" => (real, new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, HandBuiltModel.Literal(real, 0.0), real)),
            "(double)(1 - 1)" => (real, new CastExpression(HandBuiltModel.Node(), Binary(Number(1), BinaryOperatorKind.Subtract, Number(1)), real, [], real)),
            "2147483647 + 2147483647 + 2" => (integer, Binary(Binary(Number(int.MaxValue), BinaryOperatorKind.Add, Number(int.MaxValue)), BinaryOperatorKind.Add, Number(2))),
            "1 / 0" => (integer, Binary(Number(1), BinaryOperatorKind.Divide, Number(0))),
            "x" => (integer, Unknown(integer)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private UnaryExpression Not(Expression operand)
    {
        return new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.LogicalNot, operand, boolean);
    }

    private BinaryExpression Real(double left, BinaryOperatorKind kind, double right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(real, left), kind, HandBuiltModel.Literal(real, right), real);
    }

    private static CastExpression Convert(Expression operand, IMistType target)
    {
        return new CastExpression(HandBuiltModel.Node(), operand, target, [], target);
    }

    private static LocalReferenceExpression Unknown(IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("x", type));
    }

    private static StructType Intrinsic(string name)
    {
        return new StructType(name, "Standard", new Scope(null), null, null)
        {
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true },
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