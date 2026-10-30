// Switch-expression binding: the result type every arm has to agree on, and the SCOPE of what an
// arm's pattern introduces. The scope half is the part no sample can carry, for the reason given in
// PatternResolutionTests -- a sample that compiles proves a binding is reachable somewhere, and the
// claim is about where it stops.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class SwitchExpressionResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public class Animal
        {
            public string Tag()
            {
                return "animal";
            }
        }

        public class Dog : Animal
        {
        }
        """;

    public SwitchExpressionResolutionTests()
        : base("MistSwitch_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        int n = 7;
        unknown u = 42;
        Animal a = new Dog();
        """;

    // ── The type every arm agrees on ─────────────────────────────────────

    [Fact]
    public void ASwitchExpression_TakesTheTypeItsArmsAgreeOn()
    {
        var local = SingleLocal("""var label = n switch { 1 => "one", _ => "many" };""");

        Assert.Equal("Standard.String", local.Type.FullyQualifiedName);
    }

    [Fact]
    public void ArmsThatDisagree_HaveNoCommonType()
    {
        var diagnostics = BuildRun("""var label = n switch { 1 => "one", _ => 2 };""");

        _ = Assert.Single(Errors(diagnostics, "MIST0347"));
    }

    [Fact]
    public void ATargetType_RelievesTheArmsOfAgreeing()
    {
        var diagnostics = BuildRun("""string label = n switch { 1 => "one", _ => 2 };""");

        Assert.Empty(Errors(diagnostics, "MIST0347"));
    }

    [Fact]
    public void ASwitchWithNoArms_IsReported()
    {
        var diagnostics = BuildRun("var label = n switch { };");

        _ = Assert.Single(Errors(diagnostics, "MIST0346"));
    }

    [Fact]
    public void AnArmWhoseResultFailed_DoesNotAlsoReportDisagreement()
    {
        var diagnostics = BuildRun("var label = n switch { int i => 0, _ => i };");

        _ = Assert.Single(Errors(diagnostics, "MIST0296"));
        Assert.Empty(Errors(diagnostics, "MIST0347"));
    }

    // ── Arms ─────────────────────────────────────────────────────────────

    [Fact]
    public void ArmsAreKeptInSourceOrder()
    {
        var expression = SwitchIn("""var label = n switch { < 0 => "neg", 0 => "zero", _ => "pos" };""");

        Assert.Equal(3, expression.Arms.Count);
        Assert.Equal(ComparisonKind.LessThan, Assert.IsType<ComparisonPattern>(expression.Arms[0].Pattern).Kind);
        Assert.Equal(ComparisonKind.Equal, Assert.IsType<ComparisonPattern>(expression.Arms[1].Pattern).Kind);
        _ = Assert.IsType<CatchAllPattern>(expression.Arms[2].Pattern);
    }

    [Fact]
    public void AGuardIsRecordedOnlyWhereOneIsWritten()
    {
        var expression = SwitchIn("var label = u switch { int i when i > 5 => 1, _ => 0 };");

        Assert.NotNull(expression.Arms[0].Guard);
        Assert.Null(expression.Arms[1].Guard);
    }

    [Fact]
    public void CombinatorsWorkInArmPosition()
    {
        var expression = SwitchIn("""var label = n switch { 1 or 3 or 5 => "odd", > 0 and < 100 => "small", _ => "other" };""");

        Assert.Equal(3, Assert.IsType<DisjunctionPattern>(expression.Arms[0].Pattern).Operands.Count);
        Assert.Equal(2, Assert.IsType<ConjunctionPattern>(expression.Arms[1].Pattern).Operands.Count);
    }

    [Fact]
    public void AnArmWhosePatternCannotBind_ParksTheWholeSwitch()
    {
        _ = InitializerOf<UnresolvedExpression>("var label = u switch { (0, 0) => 1, _ => 0 };");
    }

    // ── How far an arm's binding reaches ─────────────────────────────────

    [Fact]
    public void AnArmsBinding_IsReadableInItsOwnResult()
    {
        var diagnostics = BuildRun("var label = u switch { int i => i, _ => 0 };");

        Assert.Empty(Errors(diagnostics, "MIST0296"));
    }

    [Fact]
    public void AnArmsBinding_IsReadableInItsOwnGuard()
    {
        var diagnostics = BuildRun("var label = u switch { int i when i > 5 => 1, _ => 0 };");

        Assert.Empty(Errors(diagnostics, "MIST0296"));
    }

    [Fact]
    public void AnArmsBinding_IsNotReadableInASiblingArm()
    {
        var diagnostics = BuildRun("var label = u switch { int i => 0, _ => i };");

        _ = Assert.Single(Errors(diagnostics, "MIST0296"));
    }

    [Fact]
    public void AnArmsBinding_IsNotReadableInASiblingGuard()
    {
        var diagnostics = BuildRun("var label = u switch { int i => 0, string s when i > 5 => 1, _ => 2 };");

        _ = Assert.Single(Errors(diagnostics, "MIST0296"));
    }

    [Fact]
    public void AnArmsBinding_IsNotReadableAfterTheSwitch()
    {
        var diagnostics = BuildRun(
            "var probe = i;", extraPreamble: "var label = u switch { int i => 0, _ => 1 };");

        _ = Assert.Single(Errors(diagnostics, "MIST0296"));
    }

    [Fact]
    public void SiblingArms_MayReuseABindingName()
    {
        var diagnostics = BuildRun("var label = u switch { int i => i, string i => 0, _ => 1 };");

        Assert.Empty(Errors(diagnostics, "MIST0367"));
        Assert.Empty(Errors(diagnostics, "MIST0296"));
    }

    [Fact]
    public void ABindingUnderAnArmsDisjunction_IsStillRefused()
    {
        var diagnostics = BuildRun("var label = u switch { int i or string => 0, _ => 1 };");

        _ = Assert.Single(Errors(diagnostics, "MIST0348"));
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private SwitchExpression SwitchIn(string statement)
    {
        return InitializerOf<SwitchExpression>(statement);
    }
}