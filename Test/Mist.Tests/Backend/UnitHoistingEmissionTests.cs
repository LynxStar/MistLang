using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UnitHoistingEmissionTests
{
    [Theory]
    [InlineData(UnitHoistingScenario.CallArgument, "Probe.Log.F();\nstring called = Probe.Log.G(Standard.Void.Instance, Probe.Log.H());")]
    [InlineData(UnitHoistingScenario.InterpolatedHole, "Probe.Log.F();\nstring hole = System.FormattableString.Invariant($\"hole={Standard.Void.Instance} {Probe.Log.H()}\");")]
    [InlineData(UnitHoistingScenario.AssignedUnit, "Probe.Log.F();\nunit = Standard.Void.Instance;")]
    [InlineData(UnitHoistingScenario.LocalRead, "Probe.Log.F();\nstring read = Probe.Log.Read(seen, Standard.Void.Instance);")]
    public void ACallYieldingNothingAsAValue_RunsAheadOfItsStatementWithTheUnitInItsPlace(UnitHoistingScenario scenario, string expected)
    {
        Assert.Contains(expected, EmitRun(scenario, out _));
    }

    [Theory]
    [InlineData(UnitHoistingScenario.EarlierCall, "int __e1 = Probe.Log.K();\nProbe.Log.F();\nstring ordered = Probe.Log.G3(__e1, Standard.Void.Instance, Probe.Log.H());")]
    [InlineData(UnitHoistingScenario.EarlierField, "string __e1 = Probe.Log.Trail;\nProbe.Log.F();\nstring read = Probe.Log.Read(__e1, Standard.Void.Instance);")]
    public void AnOperandEvaluatedBeforeTheCall_IsStoredAheadOfIt(UnitHoistingScenario scenario, string expected)
    {
        Assert.Contains(expected, EmitRun(scenario, out _));
    }

    [Theory]
    [InlineData(UnitHoistingScenario.FieldReceiver, "Probe.Log __e1 = Probe.Log.Make();\nProbe.Log.F();\n__e1.Pt.Name = Probe.Log.Show(Standard.Void.Instance);")]
    [InlineData(UnitHoistingScenario.ReferenceArgument, "int __e1 = Probe.Log.K();\nProbe.Log.F();\nProbe.Log.Bump(ref slots[__e1], Standard.Void.Instance);")]
    [InlineData(UnitHoistingScenario.ValueReceiver, "Probe.Spot __e1 = Probe.Log.MakeSpot();\nProbe.Log.F();\nstring text = __e1.Describe(Standard.Void.Instance);")]
    public void AReceiverOrReferenceArgument_IsStoredAheadOfTheCallAsCSharpWouldReadIt(UnitHoistingScenario scenario, string expected)
    {
        Assert.Contains(expected, EmitRun(scenario, out _));
    }

    [Fact]
    public void ALoopCondition_RunsItsCallOnEveryIteration()
    {
        Assert.Contains(
            "while (true)\n{\nProbe.Log.F();\nif (!(Probe.Log.Below(Standard.Void.Instance, 2)))\n{\nbreak;\n}",
            EmitRun(UnitHoistingScenario.WhileCondition, out _));
    }

    [Fact]
    public void AnElseIfCondition_RunsItsCallInsideTheElse()
    {
        Assert.Contains(
            "else\n{\nProbe.Log.F();\nif (Probe.Log.Check(Standard.Void.Instance))",
            EmitRun(UnitHoistingScenario.ElseIfCondition, out _));
    }

    [Fact]
    public void ACallYieldingAUnitValue_IsNeitherHoistedNorRefused()
    {
        var emitted = EmitRun(UnitHoistingScenario.GenericUnitResult, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Contains("bool checkedIt = flag && Probe.Log.Check(Probe.Log.Echo<Standard.Void>(Standard.Void.Instance));", emitted);
    }

    [Theory]
    [InlineData(
        UnitHoistingScenario.LogicalAnd,
        "bool __e1 = false;\nif (flag)\n{\nProbe.Log.F();\nif (Probe.Log.Check(Standard.Void.Instance))\n{\n__e1 = true;\n}\n}\nbool held = __e1;")]
    [InlineData(
        UnitHoistingScenario.LogicalOr,
        "bool __e1 = true;\nif (!(flag))\n{\nProbe.Log.F();\nif (!(Probe.Log.Check(Standard.Void.Instance)))\n{\n__e1 = false;\n}\n}\nbool held = __e1;")]
    [InlineData(
        UnitHoistingScenario.CoalescedValue,
        "string __e1;\nif (none is { } __e2)\n{\n__e1 = __e2;\n}\nelse\n{\nProbe.Log.F();\n__e1 = Probe.Log.Show(Standard.Void.Instance);\n}\nstring text = __e1;")]
    [InlineData(
        UnitHoistingScenario.CoalescingAssignment,
        "if (none is null)\n{\nProbe.Log.F();\nnone = Probe.Log.Show(Standard.Void.Instance);\n}")]
    [InlineData(
        UnitHoistingScenario.ConditionalReceiver,
        "string? __e1 = default;\nif (maybe is not null)\n{\nProbe.Log.F();\n__e1 = maybe?.Take(Standard.Void.Instance);\n}\nstring? taken = __e1;")]
    [InlineData(
        UnitHoistingScenario.SwitchArm,
        "string __e1;\nswitch (one)\n{\ncase 1:\n{\nProbe.Log.F();\n__e1 = Probe.Log.Show(Standard.Void.Instance);\nbreak;\n}\ncase var _:\n{\n__e1 = \"none\";\nbreak;\n}\ndefault:\nthrow new System.Runtime.CompilerServices.SwitchExpressionException();\n}\nstring text = __e1;")]
    public void ACallUnderAnOperandThatMayNotRun_RunsInsideTheBranchThatRunsTheOperand(UnitHoistingScenario scenario, string expected)
    {
        var emitted = EmitRun(scenario, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Contains(expected, emitted);
    }

    [Fact]
    public void AConditionalAccessReceiver_IsStoredOnceAheadOfItsTest()
    {
        Assert.Contains(
            "Probe.Log? __e1 = Probe.Log.Maybe();\nstring? __e2 = default;\nif (__e1 is not null)\n{\nProbe.Log.F();\n__e2 = __e1?.Take(Standard.Void.Instance);\n}",
            EmitRun(UnitHoistingScenario.ConditionalFactory, out _));
    }

    [Fact]
    public void ACoalescingAssignment_StoresWhatItsTargetIsMadeOf()
    {
        Assert.Contains(
            "Probe.Log __e1 = Probe.Log.Make();\nif (__e1.Opt is null)\n{\nProbe.Log.F();\n__e1.Opt = Probe.Log.Show(Standard.Void.Instance);\n}",
            EmitRun(UnitHoistingScenario.CoalescingReceiver, out _));
    }

    [Fact]
    public void AConditionBindingAPatternVariable_NestsItsGuardsSoTheBodyStillReadsIt()
    {
        Assert.Contains(
            "bool __e1 = false;\nif (o is Probe.Log l)\n{\nProbe.Log.F();\nif (l.Has(Standard.Void.Instance))\n{\n__e1 = true;\nl.Poke(Standard.Void.Instance);\n}\n}\nif (!__e1)\n{\nProbe.Log.F();\n}",
            EmitRun(UnitHoistingScenario.PatternIf, out _));
    }

    [Fact]
    public void ALoopConditionBindingAPatternVariable_BreaksOneGuardAtATime()
    {
        Assert.Contains(
            "while (true)\n{\nif (!(Probe.Log.Maybe() is Probe.Log l))\n{\nbreak;\n}\n\nProbe.Log.F();\nif (!(l.Has(Standard.Void.Instance)))\n{\nbreak;\n}\n\nl.Poke(Standard.Void.Instance);\n}",
            EmitRun(UnitHoistingScenario.PatternWhile, out _));
    }

    [Theory]
    [InlineData(
        UnitHoistingScenario.ForCondition,
        "{\nint i = 0;\nwhile (true)\n{\nProbe.Log.F();\nif (!(Probe.Log.Below(Standard.Void.Instance, i)))\n{\nbreak;\n}\n\n{\n}\ni = i + 1;\n}\n}")]
    [InlineData(
        UnitHoistingScenario.ForIterator,
        "{\nint i = 0;\nwhile (true)\n{\nif (!(i < 2))\n{\nbreak;\n}\n\n{\n}\nProbe.Log.F();\ni = Probe.Log.Next(Standard.Void.Instance, i);\n}\n}")]
    public void AForLoopWhoseClauseNeedsStatements_RunsEachClauseWhereTheForWould(UnitHoistingScenario scenario, string expected)
    {
        Assert.Contains(expected, EmitRun(scenario, out _));
    }

    [Theory]
    [InlineData(
        UnitHoistingScenario.ExpressionLambda,
        "System.Func<string> fn = () =>\n{\nProbe.Log.F();\nreturn Probe.Log.Show(Standard.Void.Instance);\n}")]
    [InlineData(
        UnitHoistingScenario.ConditionalLambda,
        "System.Action act = () =>\n{\nif (maybe is not null)\n{\nProbe.Log.F();\nmaybe?.Poke(Standard.Void.Instance);\n}\n}")]
    public void AnExpressionLambda_TakesABlockToRunItsStatementsIn(UnitHoistingScenario scenario, string expected)
    {
        Assert.Contains(expected, EmitRun(scenario, out _));
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
    [InlineData(UnitHoistingScenario.SwitchGuard, "guard")]
    [InlineData(UnitHoistingScenario.NegatedPattern, "pattern variable")]
    public void ACallWithNowhereToRunAsAStatement_IsRefused(UnitHoistingScenario scenario, string reason)
    {
        var emitted = EmitRun(scenario, out var diagnostics);
        var refusal = Assert.Single(diagnostics);

        Assert.Equal("MIST0024", refusal.Number.Value);
        Assert.Contains(reason, refusal.Message);
        Assert.DoesNotContain("Probe.Log.F();", emitted);
    }

    private static string EmitRun(UnitHoistingScenario scenario, out List<Diagnostic> emitDiagnostics)
    {
        var fixture = new UnitHoistingFixture();
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var writer = new CSharpWriter();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitBlock(fixture.Block(scenario), writer, diagnostics);
        emitDiagnostics = [.. diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error)];
        return Normalize(writer.ToString());
    }

    private static string EmitType(string name)
    {
        Assert.Equal("Hosts", name);
        var fixture = new UnitHoistingFixture();
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var writer = new CSharpWriter();
        BodyEmitter.ResetTemporaries();
        TypeEmitter.EmitTypeBody(fixture.Hosts(), ConstraintClosureMap.Build(fixture.Model, diagnostics), fixture.Model.VariadicUses, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return Normalize(writer.ToString());
    }

    private static string Normalize(string source)
    {
        return string.Join("\n", source.ReplaceLineEndings("\n").Split('\n').Select(line => line.Trim()));
    }
}