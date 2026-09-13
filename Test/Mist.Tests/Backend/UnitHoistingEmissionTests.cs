// Tier 3 (Component): a call yielding nothing whose value is taken, emitted as a statement ahead of the one
// holding it with the unit instance in its place. Asserted on the emitted text, since a hoist that ran the call
// before an operand C# evaluates first still compiles and prints the same unit.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class UnitHoistingEmissionTests
{
    private const string Types = """
        public class Log
        {
            public static string Trail = "";

            public static void F() { Trail = Trail + "f"; }

            public static int H() { return 1; }

            public static int K() { return 2; }

            public static string G(void unit, int value) { return "g"; }

            public static string G3(int first, void unit, int last) { return "g3"; }

            public static string Read(string seen, void unit) { return seen; }

            public static bool Check(void unit) { return true; }

            public static bool Below(void unit, int limit) { return false; }

            public static T Echo<T>(T value) { return value; }
        }
        """;

    [Theory]
    [InlineData("var called = Log.G(Log.F(), Log.H());", "Probe.Log.F();\nstring called = Probe.Log.G(Standard.Void.Instance, Probe.Log.H());")]
    [InlineData("var hole = $\"hole={Log.F()} {Log.H()}\";", "Probe.Log.F();\nstring hole = $\"hole={Standard.Void.Instance} {Probe.Log.H()}\";")]
    [InlineData("void unit = void; unit = Log.F();", "Probe.Log.F();\nunit = Standard.Void.Instance;")]
    [InlineData("var seen = \"x\"; var read = Log.Read(seen, Log.F());", "Probe.Log.F();\nstring read = Probe.Log.Read(seen, Standard.Void.Instance);")]
    public void ACallYieldingNothingAsAValue_RunsAheadOfItsStatementWithTheUnitInItsPlace(string statements, string expected)
    {
        Assert.Contains(expected, EmitRun(statements, out _));
    }

    [Theory]
    [InlineData("var ordered = Log.G3(Log.K(), Log.F(), Log.H());", "int __e1 = Probe.Log.K();\nProbe.Log.F();\nstring ordered = Probe.Log.G3(__e1, Standard.Void.Instance, Probe.Log.H());")]
    [InlineData("var read = Log.Read(Log.Trail, Log.F());", "string __e1 = Probe.Log.Trail;\nProbe.Log.F();\nstring read = Probe.Log.Read(__e1, Standard.Void.Instance);")]
    public void AnOperandEvaluatedBeforeTheCall_IsStoredAheadOfIt(string statements, string expected)
    {
        Assert.Contains(expected, EmitRun(statements, out _));
    }

    [Fact]
    public void ALoopCondition_RunsItsCallOnEveryIteration()
    {
        Assert.Contains(
            "while (true)\n{\nProbe.Log.F();\nif (!(Probe.Log.Below(Standard.Void.Instance, 2)))\n{\nbreak;\n}",
            EmitRun("while(Log.Below(Log.F(), 2)) { }", out _));
    }

    [Fact]
    public void AnElseIfCondition_RunsItsCallInsideTheElse()
    {
        Assert.Contains(
            "else\n{\nProbe.Log.F();\nif (Probe.Log.Check(Standard.Void.Instance))",
            EmitRun("int value = 3; if(value == 1) { } else if(Log.Check(Log.F())) { }", out _));
    }

    [Fact]
    public void ACallYieldingAUnitValue_IsNeitherHoistedNorRefused()
    {
        var emitted = EmitRun("bool flag = true; bool checkedIt = flag && Log.Check(Log.Echo<void>(void));", out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Contains("bool checkedIt = flag && Probe.Log.Check(Probe.Log.Echo<Standard.Void>(Standard.Void.Instance));", emitted);
    }

    [Fact]
    public void ACallThatMayNotRun_IsRefusedRatherThanHoisted()
    {
        var emitted = EmitRun("bool flag = true; bool checkedIt = flag && Log.Check(Log.F());", out var diagnostics);

        Assert.Equal("MIST_BE_UNHOISTED_UNIT_CALL", Assert.Single(diagnostics).Id.Value);
        Assert.DoesNotContain("Probe.Log.F();", emitted);
    }

    private static string EmitRun(string statements, out List<Diagnostic> emitDiagnostics)
    {
        var model = StandardModelProbe.Build(statements, out var buildDiagnostics, Types);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var probe = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN["Probe.Probe"]);
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        BodyEmitter.ResetTemporaries();
        TypeEmitter.EmitTypeBody(probe, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        emitDiagnostics = [.. diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error)];

        return string.Join("\n", writer.ToString().ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()));
    }
}