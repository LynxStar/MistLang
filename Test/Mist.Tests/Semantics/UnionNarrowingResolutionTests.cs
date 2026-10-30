// Narrowing a union to one constituent, asserted on the MODEL. The TypeUnions sample cannot carry
// the load: every sample type test takes the true branch, so a discriminator that always answered
// true would pass every line, and no sample reads a narrowed binding in its introducing condition. Kept apart from PatternResolutionTests
// because a union in the preamble would reach the interner, and one of that class's tests asserts
// the interner holds none.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class UnionNarrowingResolutionTests : ProgramModelProbe
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

    public UnionNarrowingResolutionTests()
        : base("MistUnionNarrow_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        int | string un = 42;
        Animal | int mixed = new Dog();
        int? opt = 7;
        unknown u = 42;
        """;

    // ── Which constituent a test goes through ────────────────────────────

    [Fact]
    public void ATypeTestOnAUnion_RecordsTheConstituentItNarrowsThrough()
    {
        var pattern = Assert.IsType<TypeTestPattern>(PatternOf("var ok = un is int i;"));

        Assert.NotNull(pattern.Constituent);
        Assert.Equal("Standard.Int32", pattern.Constituent.FullyQualifiedName);
        Assert.Equal("Standard.Int32", pattern.NarrowedType.FullyQualifiedName);
    }

    /// <summary>
    /// The constituent recorded is the INTERNED one off the union, not a separately resolved type that
    /// merely spells the same. The accessor name is derived from it, so a different instance would name a
    /// member the carrier does not declare.
    /// </summary>
    [Fact]
    public void TheRecordedConstituent_IsTheUnionsOwnInstance()
    {
        // One model, because interning is per-model and comparing instances across two would fail for a
        // reason that has nothing to do with the claim.
        var model = BuildModel("var ok = un is int i;", extraTypes: "", extraPreamble: "", out _);
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var run = probe.MethodsList.Single(method => method.Name == "Run");

        Assert.NotNull(run.Body);
        var declaration = run.Body.Statements.OfType<LocalDeclarationStatement>().Last();
        var pattern = Assert.IsType<TypeTestPattern>(
            Assert.IsType<IsPatternExpression>(declaration.Initializer).Pattern);

        var union = model.TypeInterner.UnionTypes.Single(candidate => candidate.Name.Contains("String"));

        Assert.Contains(union.Constituents, constituent => ReferenceEquals(constituent, pattern.Constituent));
    }

    /// <summary>
    /// A type no constituent holds is narrowed through nothing, so the test has no lowering rather than
    /// merely a false answer -- which is why construction reports it instead of an analyzer.
    /// </summary>
    [Fact]
    public void ATypeNoConstituentHolds_IsReportedAndBindsAnError()
    {
        var diagnostics = BuildRun("var ok = un is double d;");

        _ = Assert.Single(Errors(diagnostics, "MIST0345"));
        _ = InitializerOf<ErrorExpression>("var ok = un is double d;");
    }

    [Fact]
    public void ASubtypeOfAConstituent_NarrowsThroughThatConstituent()
    {
        var pattern = Assert.IsType<TypeTestPattern>(PatternOf("var ok = mixed is Dog d;"));

        Assert.NotNull(pattern.Constituent);
        Assert.Equal("Probe.Animal", pattern.Constituent.FullyQualifiedName);
        Assert.Equal("Probe.Dog", pattern.NarrowedType.FullyQualifiedName);
    }

    // ── Scrutinees that need no carrier ──────────────────────────────────

    [Fact]
    public void ATypeTestOnANonUnion_RecordsNoConstituent()
    {
        Assert.Null(Assert.IsType<TypeTestPattern>(PatternOf("var ok = u is int i;")).Constituent);
    }

    [Fact]
    public void ATypeTestOnAnOptional_RecordsNoConstituent()
    {
        Assert.Null(Assert.IsType<TypeTestPattern>(PatternOf("var ok = opt is int p;")).Constituent);
    }

    // ── What the sample cannot show ──────────────────────────────────────

    [Fact]
    public void ANarrowedBinding_IsReadableLaterInItsOwnCondition()
    {
        var diagnostics = BuildRun("if (un is int i && i > 40) { }");

        Assert.Empty(Errors(diagnostics, "MIST0296"));
        Assert.Empty(Errors(diagnostics, "MIST0345"));
    }

    [Fact]
    public void ANarrowedBinding_HasTheConstituentTypeInTheBranch()
    {
        var model = BuildModel("if (un is int i) { var probe = i; }", extraTypes: "", extraPreamble: "", out _);
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var run = probe.MethodsList.Single(method => method.Name == "Run");

        Assert.NotNull(run.Body);
        var branch = run.Body.Statements.OfType<IfStatement>().Last();
        var narrowed = Assert.Single(branch.ThenBranch.Statements.OfType<LocalDeclarationStatement>());

        Assert.Equal("Standard.Int32", narrowed.Local.Type.FullyQualifiedName);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private Pattern PatternOf(string statement)
    {
        return InitializerOf<IsPatternExpression>(statement).Pattern;
    }
}