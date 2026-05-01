using Mist.Backends.CSharpTranspiler.Intrinsics;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="IntrinsicEmissionTable"/> (Task 40.1).
/// Validates that the emission registry returns correct templates for
/// binary operators, unary operators, instance methods, and returns
/// null for unknown types.
/// </summary>
public class IntrinsicEmissionTableTests
{
    private readonly IIntrinsicEmissionRegistry _registry = new IntrinsicEmissionTable();

    // ── Binary operators ─────────────────────────────────────────────────

    [Fact]
    public void TryGetBinaryOperator_Int32Plus_ReturnsCorrectTemplate()
    {
        var result = _registry.TryGetBinaryOperator("Standard.Int32", "+");
        Assert.NotNull(result);
        Assert.Equal("{0} + {1}", result.Template);
    }

    [Fact]
    public void TryGetBinaryOperator_BooleanLogicalAnd_ReturnsCorrectTemplate()
    {
        var result = _registry.TryGetBinaryOperator("Standard.Boolean", "&&");
        Assert.NotNull(result);
        Assert.Equal("{0} && {1}", result.Template);
    }

    // ── Unary operators ──────────────────────────────────────────────────

    [Fact]
    public void TryGetUnaryOperator_BooleanNot_ReturnsCorrectTemplate()
    {
        var result = _registry.TryGetUnaryOperator("Standard.Boolean", "!");
        Assert.NotNull(result);
        Assert.Equal("!{0}", result.Template);
    }

    [Fact]
    public void TryGetUnaryOperator_Int32Negate_ReturnsCorrectTemplate()
    {
        var result = _registry.TryGetUnaryOperator("Standard.Int32", "-");
        Assert.NotNull(result);
        Assert.Equal("-{0}", result.Template);
    }

    // ── Instance methods ─────────────────────────────────────────────────

    [Fact]
    public void TryGetInstanceMethod_StringSubstring_ReturnsCorrectTemplate()
    {
        var result = _registry.TryGetInstanceMethod("Standard.String", "Substring", ["Standard.Int32"]);
        Assert.NotNull(result);
        Assert.Equal("{0}.Substring({1})", result.Template);
    }

    [Fact]
    public void TryGetInstanceMethod_StringLength_ReturnsCorrectTemplate()
    {
        var result = _registry.TryGetInstanceMethod("Standard.String", "Length", []);
        Assert.NotNull(result);
        Assert.Equal("{0}.Length", result.Template);
    }

    [Fact]
    public void TryGetInstanceMethod_ArrayLength_ReturnsCorrectTemplate()
    {
        var result = _registry.TryGetInstanceMethod("Standard.Array", "Length", []);
        Assert.NotNull(result);
        Assert.Equal("{0}.Length", result.Template);
    }

    // ── Non-existent lookups ─────────────────────────────────────────────

    [Fact]
    public void TryGetBinaryOperator_NonExistentType_ReturnsNull()
    {
        var result = _registry.TryGetBinaryOperator("Standard.NonExistent", "+");
        Assert.Null(result);
    }
}
