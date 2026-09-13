// Tier 3 (Component): how an implicit conversion is spelled in C#, over the real Standard sources. Asserted on
// the emitted text because each shape here compiled to a Roslyn error when the operand was written bare -- C#
// refused the signed choice as ambiguous, would not apply a declared operator beside a second user-defined
// one, and has no conversion from Regex to string at all. No sample can hold them: the baseline is shared
// with classic, which cannot declare the first or run the rest. No Roslyn compile and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ImplicitConversionEmissionTests
{
    private const string Types = """
        public struct Point
        {
            public int X;

            public Point(int x) { this.X = x; }
        }

        public type Position = new Point;

        public class Beacon
        {
            public Beacon() { }

            public static implicit operator Position(Beacon b) { return (Position)new Point(2); }
        }

        public class Gauge
        {
            public Gauge() { }

            public static implicit operator Gauge(uint reading) { return new Gauge(); }

            public static implicit operator Gauge(short reading) { return new Gauge(); }
        }

        public class Tally
        {
            public Tally() { }

            public static implicit operator int(Tally t) { return 1; }
        }

        public class Sink
        {
            public static int Length(string text) { return text.Length; }

            public static string Back(regex pattern) { return pattern; }
        }
        """;

    [Fact]
    public void AnOperandReachingTheParameterByAWidening_IsCastToTheParameterFirst()
    {
        Assert.Contains("Probe.Gauge g = ((Probe.Gauge)((short)(small)));", Emit("byte small = 2; Gauge g = small;"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Point p = new Beacon();", "Probe.Point p = ((Probe.Position)(new Probe.Beacon()));")]
    [InlineData("int | string u = new Tally();", "((int)(new Probe.Tally()))")]
    public void AResultReachingTheSlotLosslessly_IsCastToTheResultSoCSharpAppliesOneOperatorAtATime(string statement, string expected)
    {
        Assert.Contains(expected, Emit(statement), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("string declared = pattern;", "string declared = pattern.ToString();")]
    [InlineData("string assigned = \"\"; assigned = pattern;", "assigned = pattern.ToString();")]
    [InlineData("var length = Sink.Length(pattern);", "Probe.Sink.Length(pattern.ToString())")]
    public void ARegexWhereAStringIsExpected_ReadsItsPatternAtEverySite(string statement, string expected)
    {
        Assert.Contains(expected, Emit("var pattern = `^[a-z]+$`; " + statement), StringComparison.Ordinal);
    }

    [Fact]
    public void ARegexReturnedWhereAStringIsExpected_ReadsItsPattern()
    {
        Assert.Contains("return pattern.ToString();", Emit("", "Sink", "Back"), StringComparison.Ordinal);
    }

    private static string Emit(string statements, string typeName = "Probe", string methodName = "Run")
    {
        var model = StandardModelProbe.Build(statements, out var built, Types);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var method = Assert.IsType<ClassType>(model.TypesByFQN[$"Probe.{typeName}"]).MethodsList.Single(m => m.Name == methodName);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(method.Body);

        using(IntrinsicSupply.Enter(model, Type.GetType))
        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitMethodBody(method, method.Body, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }
}