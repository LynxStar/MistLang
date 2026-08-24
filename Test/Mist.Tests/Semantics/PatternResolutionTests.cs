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
        int n = 7;
        string s = "mist";
        char c = 'a';
        bool flag = true;
        double d = 1.5;
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

    // ── Comparing against a constant ─────────────────────────────────────

    [Fact]
    public void AConstantPattern_IsAnEqualityComparison()
    {
        var comparison = Assert.IsType<ComparisonPattern>(PatternOf("var ok = n is 42;"));

        Assert.Equal(ComparisonKind.Equal, comparison.Kind);
        Assert.Equal(42L, Assert.IsType<LiteralExpression>(comparison.Bound).Value);
    }

    [Fact]
    public void EachRelationalPattern_CarriesItsOwnOperator()
    {
        Assert.Equal(ComparisonKind.LessThan, KindOf("n is < 10"));
        Assert.Equal(ComparisonKind.GreaterThan, KindOf("n is > 10"));
        Assert.Equal(ComparisonKind.LessThanOrEqual, KindOf("n is <= 10"));
        Assert.Equal(ComparisonKind.GreaterThanOrEqual, KindOf("n is >= 10"));
    }

    [Theory]
    [InlineData("s is \"mist\"")]
    [InlineData("c is 'a'")]
    [InlineData("flag is true")]
    [InlineData("d is 1.5")]
    public void EveryPrimitiveConstantForm_Binds(string test)
    {
        _ = Assert.IsType<ComparisonPattern>(PatternOf($"var ok = {test};"));
    }

    /// <summary>
    /// A value test says nothing about which type holds the value, so it must not narrow. The one case
    /// where it could -- picking a constituent out of a union -- is order 98's.
    /// </summary>
    [Fact]
    public void AComparisonPattern_NarrowsNothing()
    {
        Assert.Equal("Standard.Int32", PatternOf("var ok = n is > 5;").NarrowedType.FullyQualifiedName);
        Assert.Null(PatternOf("var ok = n is > 5;").Binding);
    }

    // ── Combinators ──────────────────────────────────────────────────────

    /// <summary>
    /// Three operands, because two cannot catch the trap: the generated Right slot holds the LAST
    /// repeated operand and also appears in the array, so a fold reading Left and Right drops the middle
    /// one and still passes every two-operand test.
    /// </summary>
    [Fact]
    public void AThreeOperandDisjunction_KeepsItsMiddleOperand()
    {
        var disjunction = Assert.IsType<DisjunctionPattern>(PatternOf("var ok = n is 1 or 3 or 5;"));

        Assert.Equal(3, disjunction.Operands.Count);
    }

    [Fact]
    public void AThreeOperandConjunction_KeepsItsMiddleOperand()
    {
        var conjunction = Assert.IsType<ConjunctionPattern>(
            PatternOf("var ok = n is > 0 and < 100 and not 50;"));

        Assert.Equal(3, conjunction.Operands.Count);
    }

    /// <summary>
    /// Matching a conjunction requires every operand, so each sees what its predecessors proved. C#
    /// narrows the same way and emission hands it the pattern verbatim, so a wider answer here would put
    /// the model at odds with the C# it produced.
    /// </summary>
    [Fact]
    public void AConjunction_NarrowsAsItGoes()
    {
        var conjunction = Assert.IsType<ConjunctionPattern>(PatternOf("var ok = a is Dog and var d;"));

        Assert.Equal("Probe.Dog", conjunction.NarrowedType.FullyQualifiedName);
        Assert.Equal("Probe.Dog", Assert.Single(conjunction.Operands.Skip(1)).NarrowedType.FullyQualifiedName);
    }

    /// <summary>
    /// A disjunction declines to narrow to the union of its operands even though a match proves it.
    /// Interning that union would add a carrier struct to the emitted output as a side effect of writing
    /// a pattern, because the emitter discovers carriers by reading the interner.
    /// </summary>
    [Fact]
    public void ADisjunction_NarrowsToTheScrutineeAndInternsNoUnion()
    {
        var model = BuildModel("var ok = u is int or string;", extraTypes: "", extraPreamble: "", out _);

        Assert.Empty(model.TypeInterner.UnionTypes);

        // Not vacuous: a union SPELLED in source does reach the interner, so the emptiness above is the
        // disjunction declining rather than this fixture never interning anything.
        var spelled = BuildModel("int | string w = 1;", extraTypes: "", extraPreamble: "", out _);
        Assert.NotEmpty(spelled.TypeInterner.UnionTypes);
    }

    [Fact]
    public void ANegation_NarrowsNothingAndKeepsNothing()
    {
        var negated = Assert.IsType<NegatedPattern>(PatternOf("var ok = u is not int;"));

        _ = Assert.IsType<UnknownType>(negated.NarrowedType);
        Assert.Null(negated.Binding);
    }

    /// <summary>
    /// Grouping is not modelled: the parentheses are dropped here and re-derived from precedence at
    /// emission, as they are for expressions.
    /// </summary>
    [Fact]
    public void AGroupedPattern_BindsToWhatItWraps()
    {
        _ = Assert.IsType<TypeTestPattern>(PatternOf("var ok = u is (int);"));
        _ = Assert.IsType<DisjunctionPattern>(PatternOf("var ok = n is (1 or 2);"));
    }

    // ── Where a binding may be introduced ────────────────────────────────

    [Fact]
    public void AConjunctionOperand_MayBind()
    {
        var conjunction = Assert.IsType<ConjunctionPattern>(PatternOf("var ok = u is int i and > 40;"));

        Assert.Empty(Errors(BuildRun("var ok = u is int i and > 40;"), "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
        Assert.Equal("i", Assert.Single(conjunction.Operands.Take(1)).Binding?.Name);
    }

    [Fact]
    public void ADisjunctionOperand_MayNotBind()
    {
        var diagnostics = BuildRun("var ok = u is int i or string;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
    }

    [Fact]
    public void ANegatedOperand_MayNotBind()
    {
        var diagnostics = BuildRun("var ok = u is not int i;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
    }

    /// <summary>
    /// The ban reaches through grouping and through a nesting conjunction, since what forbids the binding
    /// is the disjunction above it rather than its own position.
    /// </summary>
    [Fact]
    public void TheBanOnBinding_ReachesNestedPatterns()
    {
        _ = Assert.Single(Errors(
            BuildRun("var ok = u is (int i or string);"), "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
        _ = Assert.Single(Errors(
            BuildRun("var ok = u is (int i or string) and not Dog;"), "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
    }

    /// <summary>
    /// A rejected binding does not stop the pattern being built, so nothing declares the name and the
    /// second error is a consequence of the first rather than a separate fault.
    /// </summary>
    [Fact]
    public void ARejectedBinding_IsNotDeclared()
    {
        var diagnostics = BuildRun("if (u is int i or string) { var probe = i; }");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    // ── Forms deferred to later orders ───────────────────────────────────

    [Fact]
    public void AListPattern_ParksUnresolvedWithoutADiagnostic()
    {
        _ = InitializerOf<UnresolvedExpression>("var ok = u is [1];");

        Assert.Empty(Errors(BuildRun("var ok = u is [1];"), "MIST_PM_UNRESOLVED_IDENTIFIER"));
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

    private ComparisonKind KindOf(string test)
    {
        return Assert.IsType<ComparisonPattern>(PatternOf($"var ok = {test};")).Kind;
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