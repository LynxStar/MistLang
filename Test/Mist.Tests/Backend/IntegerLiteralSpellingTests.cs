using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class IntegerLiteralSpellingTests
{
    [Theory]
    [InlineData(18446744073709551615UL, "UInt64", "18446744073709551615UL")]
    [InlineData(9223372036854775808UL, "UInt64", "9223372036854775808UL")]
    [InlineData(long.MinValue, "Int64", "-9223372036854775808L")]
    [InlineData(int.MinValue, "Int32", "-2147483648")]
    [InlineData(4294967295U, "UInt32", "4294967295U")]
    [InlineData((byte)255, "Byte", "255")]
    [InlineData((sbyte)-128, "SByte", "-128")]
    [InlineData((ushort)65535, "UInt16", "65535")]
    [InlineData((short)-32768, "Int16", "-32768")]
    public void AnExactIntegralBox_IsSpelledAsItsModelTypesLiteral(object value, string type, string expected)
    {
        var literal = new LiteralExpression(null!, value, new ClassType(type, "Standard", new Scope(null), null, null!));

        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }
}