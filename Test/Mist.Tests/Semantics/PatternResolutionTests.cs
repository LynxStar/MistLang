// Pattern binding and the SCOPE of what a pattern introduces, asserted on the MODEL. No sample can
// carry the load-bearing half of order 90: a sample that compiles proves a binding IS reachable
// somewhere, and the claim being made is where it stops being reachable. Those cases are compile
// failures, so they exist only here. Order 90 and 94 of
// backlog/emitter-v2-capability-progression.yaml rest on this file.

using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class PatternResolutionTests : ProgramModelProbe
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
            public string Bark()
            {
                return "woof";
            }
        }

        public class Box<T>
        {
            public T item;
        }
        """;

    public PatternResolutionTests()
        : base("MistPattern_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        unknown u = 42;
        Animal a = new Dog();
        """;

    // ── What matching proves ─────────────────────────────────────────────

    [Fact]
    public void ATypePatternBinding_HasTheTestedType()
    {
        var narrowed = BranchLocal("if (u is int i) { var probe = i; }");

        Assert.Equal("Standard.Int32", narrowed.Type.FullyQualifiedName);
    }

    [Fact]
    public void ATypePatternBinding_NarrowsAwayFromTheScrutineesType()
    {
        var narrowed = BranchLocal("if (a is Dog d) { var probe = d; }");

        Assert.Equal("Probe.Dog", narrowed.Type.FullyQualifiedName);
    }

    [Fact]
    public void AConstructedGenericTypePattern_KeepsItsTypeArgument()
    {
        var narrowed = BranchLocal("if (u is Box<int> bx) { var probe = bx; }");

        var constructed = Assert.IsType<ConstructedType>(narrowed.Type);
        Assert.Equal("Standard.Int32", Assert.Single(constructed.TypeArguments).FullyQualifiedName);
    }

    [Fact]
    public void AVarPattern_NarrowsToTheScrutineesStaticType()
    {
        var caught = BranchLocal("if (u is var v) { var probe = v; }");

        _ = Assert.IsType<UnknownType>(caught.Type);
    }

    [Fact]
    public void AnIsExpression_TypesAsBoolean()
    {
        var local = SingleLocal("var ok = u is int;");

        Assert.Equal("Standard.Boolean", local.Type.FullyQualifiedName);
    }

    // ── Which forms keep a binding ───────────────────────────────────────

    [Fact]
    public void ANamedTypePattern_KeepsItsBinding()
    {
        var pattern = PatternOf("var ok = u is int i;");

        Assert.Equal("Standard.Int32", pattern.NarrowedType.FullyQualifiedName);
        Assert.NotNull(pattern.Binding);
        Assert.Equal("i", pattern.Binding.Name);
    }

    [Fact]
    public void ADiscardNamedTypePattern_StillTestsTheTypeAndKeepsNothing()
    {
        var pattern = PatternOf("var ok = u is int _;");

        _ = Assert.IsType<TypeTestPattern>(pattern);
        Assert.Equal("Standard.Int32", pattern.NarrowedType.FullyQualifiedName);
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void ABareTypePattern_KeepsNothing()
    {
        var pattern = PatternOf("var ok = a is Dog;");

        _ = Assert.IsType<TypeTestPattern>(pattern);
        Assert.Equal("Probe.Dog", pattern.NarrowedType.FullyQualifiedName);
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void AllThreeCatchAllForms_ClassifyAsCatchAll()
    {
        _ = Assert.IsType<CatchAllPattern>(PatternOf("var ok = u is var v;"));
        _ = Assert.IsType<CatchAllPattern>(PatternOf("var ok = u is var _;"));
        _ = Assert.IsType<CatchAllPattern>(PatternOf("var ok = u is _;"));
    }

    [Fact]
    public void ADiscardedCatchAll_KeepsNothingWhileAVarPatternDoes()
    {
        Assert.Null(PatternOf("var ok = u is var _;").Binding);
        Assert.Null(PatternOf("var ok = u is _;").Binding);
        Assert.NotNull(PatternOf("var ok = u is var v;").Binding);
    }

    // ── How far the binding reaches ──────────────────────────────────────

    /// <summary>
    /// The case the classic pipeline cannot express. It emits the test as the condition and hoists the
    /// binding into the branch, so the binding does not exist yet where this conjunction reads it.
    /// </summary>
    [Fact]
    public void ATypePatternBinding_IsReadableLaterInItsOwnCondition()
    {
        var diagnostics = BuildRun("if (u is int i && i > 0) { }");

        Assert.Empty(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void ATypePatternBinding_IsReadableInTheGuardedBranch()
    {
        var diagnostics = BuildRun("if (u is int i) { var probe = i; }");

        Assert.Empty(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void ATypePatternBinding_IsNotReadableInTheElseBranch()
    {
        var diagnostics = BuildRun("if (u is int i) { } else { var probe = i; }");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void ATypePatternBinding_IsNotReadableAfterTheIf()
    {
        var diagnostics = BuildRun("var probe = i;", extraPreamble: "if (u is int i) { }");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void SiblingConditions_MayReuseABindingName()
    {
        var diagnostics = BuildRun("if (u is int i) { } if (u is string i) { }");

        Assert.Empty(Errors(diagnostics, "MIST_PM_DUPLICATE_LOCAL"));
        Assert.Empty(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void AWhileConditionsBinding_IsReadableInTheLoopBody()
    {
        var diagnostics = BuildRun("while (u is int i) { var probe = i; u = \"done\"; }");

        Assert.Empty(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    // ── Forms deferred to later orders ───────────────────────────────────

    [Fact]
    public void CombinedPatterns_ParkUnresolvedWithoutADiagnostic()
    {
        _ = InitializerOf<UnresolvedExpression>("var ok = u is int i or string s;");
        _ = InitializerOf<UnresolvedExpression>("var ok = u is int i and Dog d;");
        _ = InitializerOf<UnresolvedExpression>("var ok = u is not int;");
        _ = InitializerOf<UnresolvedExpression>("var ok = u is (int);");

        Assert.Empty(Errors(BuildRun("var ok = u is int i or string s;"), "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void AnUnresolvableTestedType_ErrorsOnceAndDoesNotPark()
    {
        var diagnostics = BuildRun("var ok = u is Nonexistent n;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_TYPE"));
        _ = InitializerOf<ErrorExpression>("var ok = u is Nonexistent n;");
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private Pattern PatternOf(string statement)
    {
        return InitializerOf<IsPatternExpression>(statement).Pattern;
    }

    private LocalVariable BranchLocal(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out _);
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var run = probe.MethodsList.Single(method => method.Name == "Run");

        Assert.NotNull(run.Body);
        var branch = Assert.IsType<IfStatement>(run.Body.Statements.OfType<IfStatement>().Last());

        return Assert.Single(branch.ThenBranch.Statements.OfType<LocalDeclarationStatement>()).Local;
    }
}