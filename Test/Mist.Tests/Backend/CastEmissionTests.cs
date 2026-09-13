// Tier 3 (Component): how a cast's operand is spelled in C#, over the real Standard sources. Asserted on the
// emitted text because each shape here failed quietly: `(double)(a / b)` emitted bare divides after the cast
// and prints 3.5 where Mist, and classic, print 3; and a regex cast to a string had only a C# cast to spell
// it, which Roslyn refuses. No Roslyn compile and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class CastEmissionTests
{
    private const string Types = """
        public class Tag
        {
            public Tag() { }

            public static explicit operator Tag(string text) { return new Tag(); }
        }

        public class Meters
        {
            public Meters() { }

            public static implicit operator Meters(string text) { return new Meters(); }
        }

        public class Feet
        {
            public Feet() { }

            public static explicit operator Feet(Meters m) { return new Feet(); }
        }
        """;

    [Theory]
    [InlineData("var plain = (string)pattern;", "string plain = (pattern).ToString();")]
    [InlineData("var tag = (Tag)pattern;", "Probe.Tag tag = ((Probe.Tag)((pattern).ToString()));")]
    [InlineData("var feet = (Feet)pattern;", "Probe.Feet feet = ((Probe.Feet)(((Probe.Meters)((pattern).ToString()))));")]
    public void ACastReachingStringFromARegex_ReadsThePattern(string statement, string expected)
    {
        Assert.Contains(expected, Emit("var pattern = `ab+`; " + statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("double ratio = (double)(a / b);", "double ratio = ((double)(a / b));")]
    [InlineData("long product = (long)(a * b);", "long product = ((long)(a * b));")]
    [InlineData("long negated = (long)-a;", "long negated = ((long)(-a));")]
    public void ACastsOperand_KeepsTheParenthesesItWasWrittenWith(string statement, string expected)
    {
        Assert.Contains(expected, Emit("int a = 7; int b = 2; " + statement), StringComparison.Ordinal);
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