using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Tier 1 unit tests for <see cref="BodyEmitter.EmitLiteral"/>.
/// Constructs <see cref="LiteralExpression"/> instances directly with the appropriate
/// CLR value and resolved type, then asserts on the emitted C# string.
/// No lexer, parser, ProgramModel builder, or Roslyn compilation involved.
/// </summary>
public sealed class LiteralEmissionTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Integer literals — suffix requirements
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(42L, "42")]
    [InlineData(2147483647L, "2147483647")]
    [InlineData(-1L, "-1")]
    public void Int32Literal_EmitsWithoutSuffix(long value, string expected)
    {
        var literal = MakeLiteral(value, "Int32");
        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }

    [Theory]
    [InlineData(0L, "0U")]
    [InlineData(100L, "100U")]
    [InlineData(3000000000L, "3000000000U")]
    public void UInt32Literal_EmitsWithUSuffix(long value, string expected)
    {
        var literal = MakeLiteral(value, "UInt32");
        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }

    [Theory]
    [InlineData(0L, "0L")]
    [InlineData(100L, "100L")]
    [InlineData(5000000000L, "5000000000L")]
    [InlineData(-5000000000L, "-5000000000L")]
    public void Int64Literal_EmitsWithLSuffix(long value, string expected)
    {
        var literal = MakeLiteral(value, "Int64");
        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }

    [Theory]
    [InlineData(0L, "0UL")]
    [InlineData(999L, "999UL")]
    [InlineData(5000000000L, "5000000000UL")]
    public void UInt64Literal_EmitsWithULSuffix(long value, string expected)
    {
        var literal = MakeLiteral(value, "UInt64");
        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Floating point literals — suffix requirements
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(2.5f, "2.5f")]
    [InlineData(0f, "0f")]
    [InlineData(-1.5f, "-1.5f")]
    public void FloatLiteral_EmitsWithFSuffix(float value, string expected)
    {
        var literal = MakeLiteral(value, "Single");
        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }

    [Theory]
    [InlineData(3.14, "3.14")]
    [InlineData(0.0, "0.0")]
    [InlineData(100.0, "100.0")]
    [InlineData(1E10, "10000000000.0")]
    public void DoubleLiteral_EmitsWithDecimalPointOrExponent(double value, string expected)
    {
        var literal = MakeLiteral(value, "Double");
        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }

    [Theory]
    [InlineData("99.99", "99.99m")]
    [InlineData("0", "0m")]
    [InlineData("123.456", "123.456m")]
    public void DecimalLiteral_EmitsWithMSuffix(string decimalStr, string expected)
    {
        var value = decimal.Parse(decimalStr);
        var literal = MakeLiteral(value, "Decimal");
        Assert.Equal(expected, BodyEmitter.EmitLiteral(literal));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Other literal types
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BoolTrue_EmitsTrue()
    {
        var literal = MakeLiteral(true, "Boolean");
        Assert.Equal("true", BodyEmitter.EmitLiteral(literal));
    }

    [Fact]
    public void BoolFalse_EmitsFalse()
    {
        var literal = MakeLiteral(false, "Boolean");
        Assert.Equal("false", BodyEmitter.EmitLiteral(literal));
    }

    [Fact]
    public void CharLiteral_EmitsWithSingleQuotes()
    {
        var literal = MakeLiteral('Z', "Char");
        Assert.Equal("'Z'", BodyEmitter.EmitLiteral(literal));
    }

    [Fact]
    public void StringLiteral_EmitsWithDoubleQuotes()
    {
        var literal = MakeLiteral("hello", "String");
        Assert.Equal("\"hello\"", BodyEmitter.EmitLiteral(literal));
    }

    [Fact]
    public void NullLiteral_EmitsNull()
    {
        var literal = MakeLiteral(null, "Object");
        Assert.Equal("null", BodyEmitter.EmitLiteral(literal));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge cases: values that would fail C# compilation without suffix
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UInt32_ExceedingIntMax_RequiresSuffix()
    {
        // 3000000000 > int.MaxValue (2147483647). Without U, C# rejects it.
        var literal = MakeLiteral(3000000000L, "UInt32");
        Assert.Equal("3000000000U", BodyEmitter.EmitLiteral(literal));
    }

    [Fact]
    public void Int64_ExceedingIntMax_RequiresSuffix()
    {
        // 5000000000 > int.MaxValue. Without L, C# interprets as int and rejects.
        var literal = MakeLiteral(5000000000L, "Int64");
        Assert.Equal("5000000000L", BodyEmitter.EmitLiteral(literal));
    }

    [Fact]
    public void UInt64_ExceedingIntMax_RequiresSuffix()
    {
        // Without UL, C# can't determine the type.
        var literal = MakeLiteral(5000000000L, "UInt64");
        Assert.Equal("5000000000UL", BodyEmitter.EmitLiteral(literal));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static LiteralExpression MakeLiteral(object? value, string typeName)
    {
        var type = new ClassType(typeName, "Standard", new Scope(null), null, null!);
        return new LiteralExpression(null!, value, type);
    }
}
