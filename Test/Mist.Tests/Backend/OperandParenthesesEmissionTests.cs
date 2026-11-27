using System.Collections.Immutable;

namespace Mist.Tests.Backend;

// Mist's binder erases source parentheses, so emitted C# must restore them when its operand precedence requires them.
[Trait("Tier", "4")]
public sealed class OperandParenthesesEmissionTests
{
    private static readonly Lazy<ImmutableDictionary<string, string>> Bodies = new(() => ParenthesesMethodBodies.Emit(
        CoalescingOperands.Concat(RightOperands).Concat(LooserOperands).Concat(TightlyBoundOperands).Select(row => Assert.IsType<string>(row[0])),
        Locals));

    private const string Locals = """
        int a = 10; int b = 3; int c = 2; int? maybe = 250; int? other = 4; bool? flag = false; string? text = "xyz";
        int[]? xs = void; int[] ys = [7];
        """;

    public static TheoryData<string, string> CoalescingOperands => new()
    {
        { "int left = (maybe ?? b) + b;", "int left = (maybe ?? b) + b;" },
        { "int right = b + (maybe ?? b);", "int right = b + (maybe ?? b);" },
        { "bool compared = (maybe ?? b) > 5;", "bool compared = (maybe ?? b) > 5;" },
        { "int negated = -(maybe ?? b);", "int negated = -(maybe ?? b);" },
        { "bool inverted = !(flag ?? false);", "bool inverted = !(flag ?? false);" },
        { "bool either = (flag ?? false) || true;", "bool either = (flag ?? false) || true;" },
        { "string upper = (text ?? \"abc\").ToUpper();", "string upper = (text ?? \"abc\").ToUpperInvariant();" },
        { "int first = (xs ?? ys)[0];", "int first = (xs ?? ys)[0];" },
        { "int identity = ((maybe ?? b) as int) + b;", "int identity = (maybe ?? b) + b;" },
    };

    [Theory]
    [MemberData(nameof(CoalescingOperands))]
    public void ACoalescingOperand_KeepsItsParentheses(string statement, string expected)
    {
        Assert.Contains(expected, Bodies.Value[statement], StringComparison.Ordinal);
    }

    public static TheoryData<string, string> RightOperands => new()
    {
        { "int subtracted = a - (b - c);", "int subtracted = a - (b - c);" },
        { "int divided = a / (b / c);", "int divided = a / (b / c);" },
        { "string joined = \"n=\" + (b + c);", "string joined = \"n=\" + (b + c).ToString(System.Globalization.CultureInfo.InvariantCulture);" },
    };

    [Theory]
    [MemberData(nameof(RightOperands))]
    public void ARightOperandOfTheSamePrecedence_KeepsItsParentheses(string statement, string expected)
    {
        Assert.Contains(expected, Bodies.Value[statement], StringComparison.Ordinal);
    }

    public static TheoryData<string, string> LooserOperands => new()
    {
        { "var named = (b + c) switch { 5 => \"five\", _ => \"other\" };", "string named = (b + c) switch {" },
        { "int switched = -(c switch { 2 => 5, _ => 6 });", "int switched = -(c switch {" },
        { "int assigned = (a = 5) + 1;", "int assigned = (a = 5) + 1;" },
        { "int fallback = maybe ?? (a = 5);", "int fallback = maybe ?? (a = 5);" },
        { "int reassigned = (maybe = other) ?? 9;", "int reassigned = (maybe = other) ?? 9;" },
        { "var widened = (a + b) as long;", "((long)(a + b))" },
    };

    [Theory]
    [MemberData(nameof(LooserOperands))]
    public void ALooserOperand_KeepsItsParentheses(string statement, string expected)
    {
        Assert.Contains(expected, Bodies.Value[statement], StringComparison.Ordinal);
    }

    public static TheoryData<string, string> TightlyBoundOperands => new()
    {
        { "int chained = a - b - c;", "int chained = a - b - c;" },
        { "int mixed = a + b * c;", "int mixed = a + b * c;" },
        { "int scaled = (c switch { 2 => 5, _ => 6 }) * 2;", "int scaled = c switch { 2 => 5, var _ => 6 } * 2;" },
        { "int picked = maybe ?? b;", "int picked = maybe ?? b;" },
    };

    [Theory]
    [MemberData(nameof(TightlyBoundOperands))]
    public void AnOperandThatAlreadyBindsTightly_IsLeftBare(string statement, string expected)
    {
        Assert.Contains(expected, Bodies.Value[statement], StringComparison.Ordinal);
    }
}