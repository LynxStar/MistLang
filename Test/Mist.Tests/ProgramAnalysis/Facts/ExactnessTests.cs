using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class ExactnessTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapacityProductRequiresProofThatItCannotWrap(bool bounded)
    {
        var model = new FactModel();
        var h = model.Expressions;
        var capacity = model.Value("capacity");
        var expression = h.Binary(HandBuiltRefinements.Local(h.Int), BinaryOperatorKind.Multiply, h.Literal(2), h.Int);
        var bag = MakeBag(Mist.ProgramAnalysis.Facts.Fact.In(capacity, new IntegralSet([IntegralTypes.Range(capacity.Type)!.Value])));
        if(bounded)
        {
            bag = bag.Add(Le(0, (capacity, -1))).Add(Le(-1_000_000, (capacity, 1)));
        }

        Assert.Equal(bounded, Exactness.Holds(expression, new Term(capacity).Scale(2), bag, Limit));
    }

    [Theory]
    [InlineData("add", true)]
    [InlineData("subtract", true)]
    [InlineData("multiply", true)]
    [InlineData("negate", true)]
    [InlineData("add", false)]
    [InlineData("subtract", false)]
    [InlineData("multiply", false)]
    [InlineData("negate", false)]
    public void EachIntegralOperationChecksBothEndsOfItsResult(string kind, bool fits)
    {
        var model = new FactModel();
        var h = model.Expressions;
        var value = model.Value("x");
        var local = HandBuiltRefinements.Local(h.Int);
        var operand = new Term(value);
        var (expression, term, input) = kind switch
        {
            "add" => (h.Binary(local, BinaryOperatorKind.Add, h.Literal(1), h.Int), operand.Add(new Term(1)), fits ? 10L : int.MaxValue),
            "subtract" => (h.Binary(local, BinaryOperatorKind.Subtract, h.Literal(1), h.Int), operand.Add(new Term(-1)), fits ? 10L : int.MinValue),
            "multiply" => (h.Binary(local, BinaryOperatorKind.Multiply, h.Literal(-2), h.Int), operand.Scale(-2), fits ? 10L : int.MaxValue),
            _ => ((Expression)new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, local, h.Int), operand.Scale(-1), fits ? 10L : int.MinValue),
        };

        Assert.Equal(fits, Exactness.Holds(expression, term, MakeBag(Eq(-input, (value, 1))), Limit));
    }

    [Theory]
    [InlineData("SByte", -128L, 127UL)]
    [InlineData("Byte", 0L, 255UL)]
    [InlineData("Int16", -32768L, 32767UL)]
    [InlineData("UInt16", 0L, 65535UL)]
    [InlineData("Int32", -2147483648L, 2147483647UL)]
    [InlineData("UInt32", 0L, 4294967295UL)]
    [InlineData("Int64", long.MinValue, 9223372036854775807UL)]
    [InlineData("UInt64", 0L, ulong.MaxValue)]
    [InlineData("Char", 0L, 65535UL)]
    public void IntegralRangesIncludeTheFullUnsignedDomain(string name, long low, ulong high)
    {
        var h = new HandBuiltRefinements();
        var type = h.Built.Type(name);
        var range = IntegralTypes.Range(type)!.Value;

        Assert.Equal(new BigInteger(low), range.Low);
        Assert.Equal(new BigInteger(high), range.High);
    }

    [Fact]
    public void EnumsUseTheirUnderlyingRange()
    {
        var h = new HandBuiltRefinements();
        var type = new EnumType("Code", "Probe", null, null, null) { UnderlyingType = h.Built.Type("Byte") };

        Assert.Equal(new IntegralInterval(0, 255), IntegralTypes.Range(type));
    }

    [Fact]
    public void UnsignedUpperBoundIsProvenWithoutSignedOverflow()
    {
        var h = new HandBuiltRefinements();
        var type = h.Built.Type("UInt64");
        var value = new Value(type, "x", HandBuiltModel.Node());
        var local = HandBuiltRefinements.Local(type);
        var expression = h.Binary(local, BinaryOperatorKind.Add, h.Literal(1UL, type), type);
        var max = new BigInteger(ulong.MaxValue);
        var bag = MakeBag(new Mist.ProgramAnalysis.Facts.Fact(new Term(value).Add(new Term(1 - max)), Relation.Equal));

        Assert.True(Exactness.Holds(expression, new Term(value).Add(new Term(1)), bag, Limit));
        Assert.False(Exactness.Holds(expression, new Term(value).Add(new Term(2)), bag, Limit));
    }

    [Fact]
    public void UnsupportedAndUserDefinedOperationsAreNotExactArithmeticFacts()
    {
        var h = new HandBuiltRefinements();
        var one = h.Literal(1);
        Expression[] expressions =
        [
            h.Binary(one, BinaryOperatorKind.Divide, one, h.Int),
            h.Binary(one, BinaryOperatorKind.Modulo, one, h.Int),
            h.Binary(HandBuiltRefinements.Local(h.Int), BinaryOperatorKind.Multiply, HandBuiltRefinements.Local(h.Int), h.Int),
            new BinaryExpression(HandBuiltModel.Node(), one, BinaryOperatorKind.Add, one, h.Int)
            {
                Operator = new Operator(OperatorKind.Plus, h.Int, [], new MemberModifiers()),
            },
            new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, one, h.Int)
            {
                Operator = new Operator(OperatorKind.Minus, h.Int, [], new MemberModifiers()),
            },
            h.Binary(h.Literal(1.0), BinaryOperatorKind.Add, h.Literal(2.0), h.Double),
        ];

        Assert.All(expressions, expression => Assert.False(Exactness.Holds(expression, new Term(1), MakeBag(), Limit)));
    }

    [Fact]
    public void ExhaustedProofDoesNotPermitRecordingArithmetic()
    {
        var h = new HandBuiltRefinements();
        var expression = h.Binary(h.Literal(1), BinaryOperatorKind.Add, h.Literal(1), h.Int);

        Assert.False(Exactness.Holds(expression, new Term(2), MakeBag(), new Budget(TimeSpan.Zero, 0)));
    }
}