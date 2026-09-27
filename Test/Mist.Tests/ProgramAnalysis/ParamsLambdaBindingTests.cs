// Tier 3 (Component): the binder types a lambda argument to a params parameter against the element type, which
// is the half of the params rows that no hand-built fragment can carry; the judging of a lambda so typed is
// PuritySoundnessRoundTwoTests' Tier 2 rows.

using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "3")]
public sealed class ParamsLambdaBindingTests : ProgramModelProbe
{
    public ParamsLambdaBindingTests()
        : base("MistParamsLambdaBinding_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Clock
        {
            public static int Ticks;
        }

        type Reader = pure func<int>;

        public class Ops
        {
            public static Reader Pick(params Reader[] rs) { foreach(var r in rs) { return r; } return () => 0; }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("Reader r = Ops.Pick(() => { Clock.Ticks = 1; return 1; }, () => 1);")]
    [InlineData("Reader r = Ops.Pick(() => 1, () => 2);")]
    public void ALambdaPassedThroughAParamsParameter_IsBoundAgainstTheElementType(string statement)
    {
        var call = Assert.IsType<InvocationExpression>(Unwrapped(DeclarationIn(statement, "").Initializer!));

        Assert.Equal(2, call.Arguments.Count);
        Assert.All(call.Arguments, argument => Assert.True(Unwrapped(argument) is LambdaExpression { Type: IFunctionType { IsPure: true } }));
    }

    private static Expression Unwrapped(Expression expression)
    {
        return expression is ImplicitConversionExpression { Operator: null } conversion ? Unwrapped(conversion.Operand) : expression;
    }
}