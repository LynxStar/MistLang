// Tier 3 (Component): arithmetic on byte, sbyte, short and ushort, which Standard types as the operand's own
// type and C# as int, over the real Standard sources. Asserted on the emitted text because the Roslyn failure
// is the visible half only: where no slot of the declared type forces the question, as an interpolation or a
// comparison, C#'s int prints 300 for 200 + 100 where Mist's byte prints 44. No Roslyn compile and no process
// spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class NarrowArithmeticEmissionTests
{
    private const string Locals = "byte a = 200; byte b = 100; short s = 3; sbyte n = 5; ushort u = 6; int i = 7; ";

    private const string Types = """
        public class Money
        {
            public byte Amount;

            public Money(byte amount)
            {
                this.Amount = amount;
            }

            public static Money operator +(Money x, Money y)
            {
                return new Money(x.Amount + y.Amount);
            }

            public static byte operator -(Money x, Money y)
            {
                return x.Amount - y.Amount;
            }
        }
        """;

    [Theory]
    [InlineData("byte sum = a + b;", "byte sum = ((byte)(a + b));")]
    [InlineData("var inferred = a - b;", "byte inferred = ((byte)(a - b));")]
    [InlineData("short product = s * s;", "short product = ((short)(s * s));")]
    [InlineData("ushort remainder = u % u;", "ushort remainder = ((ushort)(u % u));")]
    [InlineData("sbyte negated = -n;", "sbyte negated = ((sbyte)(-n));")]
    [InlineData("byte nested = a - (b - a);", "byte nested = ((byte)(a - ((byte)(b - a))));")]
    [InlineData("bool compared = a + b > b;", "bool compared = ((byte)(a + b)) > b;")]
    [InlineData("Money m = new Money(a); byte fromOperator = m - (m + m);", "byte fromOperator = ((byte)(m - (m + m)));")]
    public void ArithmeticOverANarrowIntegral_IsSpelledBackIntoItsType(string statement, string expected)
    {
        Assert.Contains(expected, Emit(Locals + statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int widened = a + i;", "int widened = a + i;")]
    [InlineData("int whole = i * i;", "int whole = i * i;")]
    [InlineData("bool less = a < b;", "bool less = a < b;")]
    public void ArithmeticThatAnswersAnotherType_IsLeftAsWritten(string statement, string expected)
    {
        Assert.Contains(expected, Emit(Locals + statement), StringComparison.Ordinal);
    }

    private static string Emit(string statements)
    {
        var model = StandardModelProbe.Build(statements, out var built, Types);
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