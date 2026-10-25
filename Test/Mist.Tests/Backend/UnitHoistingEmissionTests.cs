// Tier 3 (Component): a call yielding nothing whose value is taken, emitted as a statement ahead of the one
// holding it with the unit instance in its place. Asserted on the emitted text, since a hoist that ran the call
// before an operand C# evaluates first still compiles and prints the same unit.

using System.Text.RegularExpressions;
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

            public string? Opt = void;

            public Spot Pt = new Spot();

            public static void F() { Trail = Trail + "f"; }

            public static int H() { return 1; }

            public static int K() { return 2; }

            public static string G(void unit, int value) { return "g"; }

            public static string G3(int first, void unit, int last) { return "g3"; }

            public static string Read(string seen, void unit) { return seen; }

            public static bool Check(void unit) { return true; }

            public static bool Below(void unit, int limit) { return false; }

            public static int Next(void unit, int i) { return i + 1; }

            public static string Show(void unit) { return "s"; }

            public static void Bump(ref int slot, void unit) { slot = slot + 1; }

            public static Log Make() { return new Log(); }

            public static Spot MakeSpot() { return new Spot(); }

            public static Log? Maybe() { return void; }

            public static T Echo<T>(T value) { return value; }

            public string Take(void unit) { return "took"; }

            public bool Has(void unit) { return true; }

            public void Poke(void unit) { }
        }

        public struct Spot
        {
            public string Name;

            public string Describe(void unit) { return Name; }
        }

        public class Base
        {
            public Base(string seen) { }
        }

        public class Hosts : Base
        {
            public static string Seeded = Log.Show(Log.F());

            public string Held { get; } = Log.Show(Log.F());

            public string Shown => Log.Show(Log.F());

            public Hosts(int n) : base(Log.G(Log.F(), n)) { }
        }
        """;

    [Theory]
    [InlineData("var called = Log.G(Log.F(), Log.H());", "Probe.Log.F();\nstring called = Probe.Log.G(Standard.Void.Instance, Probe.Log.H());")]
    [InlineData("var hole = $\"hole={Log.F()} {Log.H()}\";", "Probe.Log.F();\nstring hole = System.FormattableString.Invariant($\"hole={Standard.Void.Instance} {Probe.Log.H()}\");")]
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

    [Theory]
    [InlineData("Log.Make().Pt.Name = Log.Show(Log.F());", "Probe.Log __e1 = Probe.Log.Make();\nProbe.Log.F();\n__e1.Pt.Name = Probe.Log.Show(Standard.Void.Instance);")]
    [InlineData("int[] slots = [5]; Log.Bump(ref slots[Log.K()], Log.F());", "int __e1 = Probe.Log.K();\nProbe.Log.F();\nProbe.Log.Bump(ref slots[__e1], Standard.Void.Instance);")]
    [InlineData("var text = Log.MakeSpot().Describe(Log.F());", "Probe.Spot __e1 = Probe.Log.MakeSpot();\nProbe.Log.F();\nstring text = __e1.Describe(Standard.Void.Instance);")]
    public void AReceiverOrReferenceArgument_IsStoredAheadOfTheCallAsCSharpWouldReadIt(string statements, string expected)
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

    [Theory]
    [InlineData(
        "bool flag = true; bool held = flag && Log.Check(Log.F());",
        "bool __e1 = false;\nif (flag)\n{\nProbe.Log.F();\nif (Probe.Log.Check(Standard.Void.Instance))\n{\n__e1 = true;\n}\n}\nbool held = __e1;")]
    [InlineData(
        "bool flag = true; bool held = flag || Log.Check(Log.F());",
        "bool __e1 = true;\nif (!(flag))\n{\nProbe.Log.F();\nif (!(Probe.Log.Check(Standard.Void.Instance)))\n{\n__e1 = false;\n}\n}\nbool held = __e1;")]
    [InlineData(
        "string? none = void; var text = none ?? Log.Show(Log.F());",
        "string __e1;\nif (none is { } __e2)\n{\n__e1 = __e2;\n}\nelse\n{\nProbe.Log.F();\n__e1 = Probe.Log.Show(Standard.Void.Instance);\n}\nstring text = __e1;")]
    [InlineData(
        "string? none = void; none ??= Log.Show(Log.F());",
        "if (none is null)\n{\nProbe.Log.F();\nnone = Probe.Log.Show(Standard.Void.Instance);\n}")]
    [InlineData(
        "Log? maybe = void; var taken = maybe?.Take(Log.F());",
        "string? __e1 = default;\nif (maybe is not null)\n{\nProbe.Log.F();\n__e1 = maybe?.Take(Standard.Void.Instance);\n}\nstring? taken = __e1;")]
    [InlineData(
        "int one = 1; var text = one switch { 1 => Log.Show(Log.F()), _ => \"none\" };",
        "string __e1;\nswitch (one)\n{\ncase 1:\n{\nProbe.Log.F();\n__e1 = Probe.Log.Show(Standard.Void.Instance);\nbreak;\n}\ncase var _:\n{\n__e1 = \"none\";\nbreak;\n}\ndefault:\nthrow new System.Runtime.CompilerServices.SwitchExpressionException();\n}\nstring text = __e1;")]
    public void ACallUnderAnOperandThatMayNotRun_RunsInsideTheBranchThatRunsTheOperand(string statements, string expected)
    {
        var emitted = EmitRun(statements, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Contains(expected, emitted);
    }

    [Fact]
    public void AConditionalAccessReceiver_IsStoredOnceAheadOfItsTest()
    {
        Assert.Contains(
            "Probe.Log? __e1 = Probe.Log.Maybe();\nstring? __e2 = default;\nif (__e1 is not null)\n{\nProbe.Log.F();\n__e2 = __e1?.Take(Standard.Void.Instance);\n}",
            EmitRun("var taken = Log.Maybe()?.Take(Log.F());", out _));
    }

    [Fact]
    public void ACoalescingAssignment_StoresWhatItsTargetIsMadeOf()
    {
        Assert.Contains(
            "Probe.Log __e1 = Probe.Log.Make();\nif (__e1.Opt is null)\n{\nProbe.Log.F();\n__e1.Opt = Probe.Log.Show(Standard.Void.Instance);\n}",
            EmitRun("Log.Make().Opt ??= Log.Show(Log.F());", out _));
    }

    [Fact]
    public void AConditionBindingAPatternVariable_NestsItsGuardsSoTheBodyStillReadsIt()
    {
        Assert.Contains(
            "bool __e1 = false;\nif (o is Probe.Log l)\n{\nProbe.Log.F();\nif (l.Has(Standard.Void.Instance))\n{\n__e1 = true;\nl.Poke(Standard.Void.Instance);\n}\n}\nif (!__e1)\n{\nProbe.Log.F();\n}",
            EmitRun("object o = \"x\"; if(o is Log l && l.Has(Log.F())) { l.Poke(void); } else { Log.F(); }", out _));
    }

    [Fact]
    public void ALoopConditionBindingAPatternVariable_BreaksOneGuardAtATime()
    {
        Assert.Contains(
            "while (true)\n{\nif (!(Probe.Log.Maybe() is Probe.Log l))\n{\nbreak;\n}\n\nProbe.Log.F();\nif (!(l.Has(Standard.Void.Instance)))\n{\nbreak;\n}\n\nl.Poke(Standard.Void.Instance);\n}",
            EmitRun("while(Log.Maybe() is Log l && l.Has(Log.F())) { l.Poke(void); }", out _));
    }

    [Theory]
    [InlineData(
        "for(int i = 0; Log.Below(Log.F(), i); i = i + 1) { }",
        "{\nint i = 0;\nwhile (true)\n{\nProbe.Log.F();\nif (!(Probe.Log.Below(Standard.Void.Instance, i)))\n{\nbreak;\n}\n\n{\n}\ni = i + 1;\n}\n}")]
    [InlineData(
        "for(int i = 0; i < 2; i = Log.Next(Log.F(), i)) { }",
        "{\nint i = 0;\nwhile (true)\n{\nif (!(i < 2))\n{\nbreak;\n}\n\n{\n}\nProbe.Log.F();\ni = Probe.Log.Next(Standard.Void.Instance, i);\n}\n}")]
    public void AForLoopWhoseClauseNeedsStatements_RunsEachClauseWhereTheForWould(string statements, string expected)
    {
        Assert.Contains(expected, EmitRun(statements, out _));
    }

    [Theory]
    [InlineData(
        "Func<string> fn = () => Log.Show(Log.F());",
        "System.Func<string> fn = () =>\n{\nProbe.Log.F();\nreturn Probe.Log.Show(Standard.Void.Instance);\n}")]
    [InlineData(
        "Log? maybe = void; Action act = () => maybe?.Poke(Log.F());",
        "System.Action act = () =>\n{\nif (maybe is not null)\n{\nProbe.Log.F();\nmaybe?.Poke(Standard.Void.Instance);\n}\n}")]
    public void AnExpressionLambda_TakesABlockToRunItsStatementsIn(string statements, string expected)
    {
        Assert.Contains(expected, EmitRun(statements, out _));
    }

    [Fact]
    public void AnExpressionBodiedGetter_TakesABlockToRunItsStatementsIn()
    {
        Assert.Contains(
            "public string Shown\n{\nget\n{\nProbe.Log.F();\nreturn Probe.Log.Show(Standard.Void.Instance);\n}\n}",
            EmitType("Hosts"));
    }

    [Fact]
    public void AnInitializer_RunsItsStatementsInAStaticMethodCalledInItsPlace()
    {
        var emitted = EmitType("Hosts");

        Assert.Contains("public static string Seeded = __InitializeSeeded();", emitted);
        Assert.Contains("private static string __InitializeSeeded()\n{\nProbe.Log.F();\nreturn Probe.Log.Show(Standard.Void.Instance);\n}", emitted);
        Assert.Contains("public string Held { get; } = __InitializeHeld();", emitted);
        Assert.Contains("private static string __InitializeHeld()\n{\nProbe.Log.F();\nreturn Probe.Log.Show(Standard.Void.Instance);\n}", emitted);
    }

    [Fact]
    public void AChainedArgument_RunsItsStatementsInAStaticMethodTakingTheParametersItReads()
    {
        var emitted = EmitType("Hosts");
        var call = Regex.Match(emitted, @"public Hosts\(int n\) : base\((__ChainArgument\d+)\(n\)\)");

        Assert.True(call.Success, emitted);
        Assert.Contains(
            $"private static string {call.Groups[1].Value}(int n)\n{{\nProbe.Log.F();\nreturn Probe.Log.G(Standard.Void.Instance, n);\n}}",
            emitted);
    }

    [Theory]
    [InlineData("int one = 1; var armed = one switch { 1 when Log.Check(Log.F()) => \"a\", _ => \"b\" };", "guard")]
    [InlineData("object o = \"x\"; if(!(!(o is Log l && l.Has(Log.F())))) { l.Poke(void); }", "pattern variable")]
    public void ACallWithNowhereToRunAsAStatement_IsRefused(string statements, string reason)
    {
        var emitted = EmitRun(statements, out var diagnostics);
        var refusal = Assert.Single(diagnostics);

        Assert.Equal("MIST0024", refusal.Number.Value);
        Assert.Contains(reason, refusal.Message);
        Assert.DoesNotContain("Probe.Log.F();", emitted);
    }

    private static string EmitRun(string statements, out List<Diagnostic> emitDiagnostics)
    {
        return Emit(statements, "Probe", out emitDiagnostics);
    }

    private static string EmitType(string name)
    {
        var emitted = Emit("", name, out var diagnostics);

        Assert.Empty(diagnostics);
        return emitted;
    }

    private static string Emit(string statements, string typeName, out List<Diagnostic> emitDiagnostics)
    {
        var model = StandardModelProbe.Build(statements, out var buildDiagnostics, Types);
        Assert.Empty(buildDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var type = Assert.IsAssignableFrom<MemberType>(model.TypesByFQN[$"Probe.{typeName}"]);
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        BodyEmitter.ResetTemporaries();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics);

        emitDiagnostics = [.. diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error)];

        return string.Join("\n", writer.ToString().ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()));
    }
}