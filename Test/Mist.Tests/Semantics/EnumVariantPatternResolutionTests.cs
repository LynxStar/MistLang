// An enum variant in pattern position, asserted on the MODEL. Output cannot carry any of this: a
// program that prints True for `d is Direction.North` is equally consistent with a type test that
// happened to succeed, and the load-bearing claim is that the dotted name resolved as a VALUE at
// all. The negatives -- a dotted name that is NOT a variant, which must keep reporting an unresolved
// type -- are compile failures and exist nowhere else.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class EnumVariantPatternResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public enum Direction
        {
            North,
            South
        }

        public enum Level : string
        {
            Info = "info",
            Warning = "warning"
        }

        public class Animal
        {
        }

        public class Outer
        {
            public class Inner
            {
            }
        }
        """;

    public EnumVariantPatternResolutionTests()
        : base("MistEnumPattern_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        unknown u = 42;
        Direction d = Direction.North;
        Level lv = Level.Info;
        Animal a = new Animal();
        """;

    // ── What a variant pattern is ────────────────────────────────────────

    [Fact]
    public void AnEnumVariantPattern_IsAnEqualityComparison()
    {
        var comparison = Assert.IsType<ComparisonPattern>(PatternOf("var ok = d is Direction.North;"));

        Assert.Equal(ComparisonKind.Equal, comparison.Kind);
        var bound = Assert.IsType<MemberAccessExpression>(comparison.Bound);
        Assert.Equal("North", bound.MemberName);
        Assert.Equal("Probe.Direction", bound.Type.FullyQualifiedName);
    }

    /// <summary>
    /// A Mist variant is a VALUE, so <c>Direction.North</c> names no
    /// type -- and the reading has to be tried BEFORE type resolution, which reports what it cannot find
    /// rather than declining. A test that only checked the bound pattern would pass with the diagnostic
    /// still being emitted alongside it.
    /// </summary>
    [Fact]
    public void AnEnumVariantPattern_ResolvesAsAValueWithoutReportingAnUnresolvedType()
    {
        var diagnostics = BuildRun("var ok = d is Direction.North;");

        Assert.Empty(Errors(diagnostics, "MIST0298"));
        BindsCleanly("var ok = d is Direction.North;");
    }

    [Fact]
    public void AVariantPattern_NarrowsNothingAndKeepsNothing()
    {
        var pattern = PatternOf("var ok = d is Direction.South;");

        Assert.Equal("Probe.Direction", pattern.NarrowedType.FullyQualifiedName);
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void AnIsExpressionOverAVariant_StillTypesAsBoolean()
    {
        Assert.Equal("Standard.Boolean", SingleLocal("var ok = d is Direction.North;").Type.FullyQualifiedName);
    }

    [Fact]
    public void AVariantOverAnyUnderlyingType_Binds()
    {
        _ = Assert.IsType<ComparisonPattern>(PatternOf("var ok = lv is Level.Warning;"));
        BindsCleanly("var ok = lv is Level.Warning;");
    }

    [Fact]
    public void AVariantPattern_IsReadFromTheNameRatherThanTheScrutinee()
    {
        _ = Assert.IsType<ComparisonPattern>(PatternOf("var ok = u is Direction.North;"));
        BindsCleanly("var ok = u is Direction.North;");
    }

    // ── What the reading must NOT claim ──────────────────────────────────

    [Fact]
    public void AnUndottedTypeName_StaysATypeTest()
    {
        _ = Assert.IsType<TypeTestPattern>(PatternOf("var ok = a is Animal;"));
    }

    [Fact]
    public void ADottedTypeName_StaysATypeTest()
    {
        var pattern = Assert.IsType<TypeTestPattern>(PatternOf("var ok = u is Outer.Inner;"));

        Assert.Equal("Probe.Outer.Inner", pattern.NarrowedType.FullyQualifiedName);
        BindsCleanly("var ok = u is Outer.Inner;");
    }

    [Fact]
    public void ADottedNameQualifiedByANonEnum_FallsBackToReportingAnUnresolvedType()
    {
        _ = Assert.Single(Errors(BuildRun("var ok = u is Animal.Nope;"), "MIST0298"));
    }

    [Fact]
    public void AVariantNameTheEnumDoesNotDeclare_FallsBackToReportingAnUnresolvedType()
    {
        _ = Assert.Single(Errors(BuildRun("var ok = d is Direction.Up;"), "MIST0298"));
    }

    // ── The other position a pattern occupies ────────────────────────────

    [Fact]
    public void AVariantPattern_BindsInASwitchArm()
    {
        var switchExpression = Assert.IsType<SwitchExpression>(
            InitializerOf<SwitchExpression>("""var label = d switch { Direction.North => "up", _ => "other" };"""));

        _ = Assert.IsType<ComparisonPattern>(switchExpression.Arms[0].Pattern);
        BindsCleanly("""var label = d switch { Direction.North => "up", _ => "other" };""");
    }

    [Fact]
    public void AVariantUnderACombinator_BindsAndIsLeftForEmissionToRefuse()
    {
        var disjunction = Assert.IsType<DisjunctionPattern>(
            PatternOf("var ok = d is Direction.North or Direction.South;"));

        Assert.Equal(2, disjunction.Operands.Count);
        BindsCleanly("var ok = d is Direction.North or Direction.South;");
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private Pattern PatternOf(string statement)
    {
        return InitializerOf<IsPatternExpression>(statement).Pattern;
    }

    private void BindsCleanly(string statement)
    {
        Assert.Empty(BuildRun(statement).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }
}