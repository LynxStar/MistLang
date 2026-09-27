using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class NegationEmissionTests
{
    private readonly HandBuiltModel model = new();

    [Fact]
    public void ANegatedNegation_IsParenthesized()
    {
        var type = model.Type("Int32");
        var negation = Negate(Negate(HandBuiltModel.Literal(type, 5L), type), type);

        Assert.Equal("-(-5)", Render(negation));
    }

    [Theory]
    [InlineData("Int32", -5L, "-(-5)")]
    [InlineData("Int64", -5000000000L, "-(-5000000000L)")]
    [InlineData("SByte", -5L, "((sbyte)(-(-5)))")]
    public void ANegatedNegativeLiteral_IsParenthesized(string typeName, long value, string expected)
    {
        var type = model.Type(typeName);

        Assert.Equal(expected, Render(Negate(HandBuiltModel.Literal(type, value), type)));
    }

    [Fact]
    public void ANegatedNonNegativeLiteral_IsLeftBare()
    {
        var type = model.Type("Int32");

        Assert.Equal("-5", Render(Negate(HandBuiltModel.Literal(type, 5L), type)));
    }

    private static UnaryExpression Negate(Expression operand, IMistType type)
    {
        return new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, operand, type);
    }

    private static string Render(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag());
    }
}