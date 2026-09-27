// Tier 3 (Component): the parentheses an operand keeps in C#, over the real Standard sources. The model holds
// the tree and parentheses written in source reach it as nothing, so every operand that binds more loosely
// than its position in C# has to be parenthesized again on the way out. Asserted on the emitted text because
// most of these compiled and printed a different number: `(maybe ?? b) + b` printed 250 where Mist, and
// classic, print 260, and `a - (b - c)` printed 5 for 9. No Roslyn compile and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class OperandParenthesesEmissionTests
{
    private const string Locals = """
        int a = 10; int b = 3; int c = 2; int? maybe = 250; int? other = 4; bool? flag = false; string? text = "xyz";
        int[]? xs = void; int[] ys = [7];
        """;

    [Theory]
    [InlineData("int left = (maybe ?? b) + b;", "int left = (maybe ?? b) + b;")]
    [InlineData("int right = b + (maybe ?? b);", "int right = b + (maybe ?? b);")]
    [InlineData("bool compared = (maybe ?? b) > 5;", "bool compared = (maybe ?? b) > 5;")]
    [InlineData("int negated = -(maybe ?? b);", "int negated = -(maybe ?? b);")]
    [InlineData("bool inverted = !(flag ?? false);", "bool inverted = !(flag ?? false);")]
    [InlineData("bool either = (flag ?? false) || true;", "bool either = (flag ?? false) || true;")]
    [InlineData("string upper = (text ?? \"abc\").ToUpper();", "string upper = (text ?? \"abc\").ToUpperInvariant();")]
    [InlineData("int first = (xs ?? ys)[0];", "int first = (xs ?? ys)[0];")]
    [InlineData("int identity = ((maybe ?? b) as int) + b;", "int identity = (maybe ?? b) + b;")]
    public void ACoalescingOperand_KeepsItsParentheses(string statement, string expected)
    {
        Assert.Contains(expected, Emit(Locals + statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int subtracted = a - (b - c);", "int subtracted = a - (b - c);")]
    [InlineData("int divided = a / (b / c);", "int divided = a / (b / c);")]
    [InlineData("string joined = \"n=\" + (b + c);", "string joined = \"n=\" + (b + c).ToString(System.Globalization.CultureInfo.InvariantCulture);")]
    public void ARightOperandOfTheSamePrecedence_KeepsItsParentheses(string statement, string expected)
    {
        Assert.Contains(expected, Emit(Locals + statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("var named = (b + c) switch { 5 => \"five\", _ => \"other\" };", "string named = (b + c) switch {")]
    [InlineData("int switched = -(c switch { 2 => 5, _ => 6 });", "int switched = -(c switch {")]
    [InlineData("int assigned = (a = 5) + 1;", "int assigned = (a = 5) + 1;")]
    [InlineData("int fallback = maybe ?? (a = 5);", "int fallback = maybe ?? (a = 5);")]
    [InlineData("int reassigned = (maybe = other) ?? 9;", "int reassigned = (maybe = other) ?? 9;")]
    [InlineData("var widened = (a + b) as long;", "((long)(a + b))")]
    public void ALooserOperand_KeepsItsParentheses(string statement, string expected)
    {
        Assert.Contains(expected, Emit(Locals + statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int chained = a - b - c;", "int chained = a - b - c;")]
    [InlineData("int mixed = a + b * c;", "int mixed = a + b * c;")]
    [InlineData("int scaled = (c switch { 2 => 5, _ => 6 }) * 2;", "int scaled = c switch { 2 => 5, var _ => 6 } * 2;")]
    [InlineData("int picked = maybe ?? b;", "int picked = maybe ?? b;")]
    public void AnOperandThatAlreadyBindsTightly_IsLeftBare(string statement, string expected)
    {
        Assert.Contains(expected, Emit(Locals + statement), StringComparison.Ordinal);
    }

    private static string Emit(string statements)
    {
        var model = StandardModelProbe.Build(statements, out var built);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(run.Body);

        using(IntrinsicSupply.Enter(model, Type.GetType))
        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitMethodBody(run, run.Body, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }
}