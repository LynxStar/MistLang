using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ConstantArithmeticEmissionTests
{
    private readonly HandBuiltModel model = new();

    private IMistType Int => model.Type("Int32");

    [Fact]
    public void AnOverflowingConstantSum_WrapsUnchecked()
    {
        Assert.Equal("unchecked(2147483647 + 1)", Emit(Binary(Literal(2147483647), BinaryOperatorKind.Add, Literal(1))));
    }

    [Fact]
    public void AnOverflowingConstantProductOfLongs_WrapsUnchecked()
    {
        var wide = model.Type("Int64");
        var product = new BinaryExpression(
            HandBuiltModel.Node(), HandBuiltModel.Literal(wide, 9223372036854775807L), BinaryOperatorKind.Add, HandBuiltModel.Literal(wide, 1L), wide);

        Assert.Equal("unchecked(9223372036854775807L + 1L)", Emit(product));
    }

    [Fact]
    public void ANarrowConstantSumOutOfItsRange_WrapsTheSpelledBackCastToo()
    {
        var narrow = model.Type("Byte");
        var sum = new BinaryExpression(
            HandBuiltModel.Node(), HandBuiltModel.Literal(narrow, (byte)200), BinaryOperatorKind.Add, HandBuiltModel.Literal(narrow, (byte)100), narrow);

        var emitted = Emit(sum);

        Assert.StartsWith("unchecked(((byte)(", emitted, StringComparison.Ordinal);
        Assert.EndsWith(")))", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverflowNestedInsideALargerConstant_IsWrappedOnce()
    {
        var nested = Binary(Binary(Literal(65536), BinaryOperatorKind.Multiply, Literal(65536)), BinaryOperatorKind.Subtract, Literal(1));

        Assert.Equal("unchecked(65536 * 65536 - 1)", Emit(nested));
    }

    [Fact]
    public void ADivisionByConstantZero_DividesByAValueCSharpCannotFold()
    {
        Assert.Equal("1 / ((int)(object)(0))", Emit(Binary(Literal(1), BinaryOperatorKind.Divide, Literal(0))));
        Assert.Equal("7 % ((int)(object)(0))", Emit(Binary(Literal(7), BinaryOperatorKind.Modulo, Literal(0))));
    }

    [Fact]
    public void ConstantsThatFitAndArithmeticOverVariables_AreLeftAsWritten()
    {
        var big = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("big", Int));

        Assert.Equal("2147483646 + 1", Emit(Binary(Literal(2147483646), BinaryOperatorKind.Add, Literal(1))));
        Assert.Equal("big + 1", Emit(Binary(big, BinaryOperatorKind.Add, Literal(1))));
        Assert.Equal("big / 0", Emit(Binary(big, BinaryOperatorKind.Divide, Literal(0))));
        Assert.Equal("6 / 3", Emit(Binary(Literal(6), BinaryOperatorKind.Divide, Literal(3))));
    }

    private LiteralExpression Literal(int value)
    {
        return HandBuiltModel.Literal(Int, value);
    }

    private BinaryExpression Binary(Expression left, BinaryOperatorKind kind, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, kind, right, Int);
    }

    private static string Emit(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag());
    }
}