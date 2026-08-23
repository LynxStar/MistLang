// Property patterns: what a subpattern name resolves to, and how far the binders it introduces reach.
// Asserted on the MODEL, because output cannot tell these apart. A program that prints the right answer
// says only that SOME member answered; whether the subpattern was typed by that member -- which is what
// makes the nested pattern's own narrowing correct -- is visible only here. The negatives are compile
// failures and so exist nowhere else. Order 93 of backlog/emitter-v2-capability-progression.yaml rests
// on this file.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class PropertyPatternResolutionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public class Animal
        {
            public string Name { get; set; }

            public int Age { get; set; }

            public string Tag()
            {
                return "animal";
            }
        }

        public class Dog : Animal
        {
            public string Breed { get; set; }
        }

        public class Box<T>
        {
            public T item;
        }
        """;

    public PropertyPatternResolutionTests()
        : base("MistPropertyPattern_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        unknown u = 42;
        Animal a = new Dog();
        Dog dog = new Dog();
        string s = "mist";
        string? maybe = "mist";
        Box<int> bx = new Box<int>();
        """;

    // ── What a subpattern resolves against ───────────────────────────────

    [Fact]
    public void ASubpatternName_ResolvesAsAMemberOfTheScrutineesType()
    {
        var subpattern = OnlySubpatternOf("var ok = a is { Name: var who };");

        Assert.Equal("Name", subpattern.MemberName);
        Assert.Equal("Standard.String", subpattern.Pattern.NarrowedType.FullyQualifiedName);
    }

    [Fact]
    public void ASubpatternOnAPrimitiveScrutinee_ResolvesAgainstItsDeclaredMembers()
    {
        var subpattern = OnlySubpatternOf("var ok = s is { Length: > 3 };");

        Assert.Equal("Standard.Int32", subpattern.Pattern.NarrowedType.FullyQualifiedName);
        BindsCleanly("var ok = s is { Length: > 3 };");
    }

    [Fact]
    public void ASubpatternOnAConstructedGeneric_SubstitutesTheTypeArgument()
    {
        var subpattern = OnlySubpatternOf("var ok = bx is { item: > 3 };");

        Assert.Equal("Standard.Int32", subpattern.Pattern.NarrowedType.FullyQualifiedName);
    }

    [Fact]
    public void ASubpatternMayNameAnInheritedMember()
    {
        var subpattern = OnlySubpatternOf("var ok = dog is { Name: var who };");

        Assert.Equal("Standard.String", subpattern.Pattern.NarrowedType.FullyQualifiedName);
        BindsCleanly("var ok = dog is { Name: var who };");
    }

    [Fact]
    public void ASubpatternOnAnOptionalScrutinee_ResolvesAgainstWhatItHolds()
    {
        var pattern = PatternOf("var ok = maybe is { Length: > 3 };");

        Assert.Equal("Standard.String", pattern.NarrowedType.FullyQualifiedName);
        BindsCleanly("var ok = maybe is { Length: > 3 };");
    }

    [Fact]
    public void ANestedPropertyPattern_ScrutineesTheMemberTheOuterOneNamed()
    {
        var inner = Assert.IsType<PropertyPattern>(
            OnlySubpatternOf("var ok = a is { Name: { Length: > 2 } };").Pattern);

        Assert.Equal("Standard.String", inner.NarrowedType.FullyQualifiedName);
        Assert.Equal("Standard.Int32", Assert.Single(inner.Subpatterns).Pattern.NarrowedType.FullyQualifiedName);
    }

    // ── What the pattern itself proves ───────────────────────────────────

    [Fact]
    public void APropertyPattern_NarrowsToTheScrutineesOwnTypeAndKeepsNothing()
    {
        var pattern = PatternOf("var ok = a is { Age: > 3 };");

        Assert.Equal("Probe.Animal", pattern.NarrowedType.FullyQualifiedName);
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void AnEmptyPropertyPattern_BindsAndSubpatternsNothing()
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf("var ok = a is { };"));

        Assert.Empty(pattern.Subpatterns);
        BindsCleanly("var ok = a is { };");
    }

    // ── Several binders out of one pattern ───────────────────────────────

    [Fact]
    public void SeveralSubpatterns_EachKeepTheirOwnBinder()
    {
        var pattern = Assert.IsType<PropertyPattern>(
            PatternOf("var ok = a is { Age: int years, Name: var who };"));

        Assert.Equal(["years", "who"], pattern.Subpatterns.Select(s => s.Pattern.Binding?.Name));
        Assert.Null(pattern.Binding);
    }

    [Fact]
    public void EverySubpatternBinder_IsReadableInTheGuardedBranch()
    {
        BindsCleanly("if (a is { Age: int years, Name: var who }) { var probe = $\"{who}{years}\"; }");
    }

    [Fact]
    public void ASubpatternBinder_IsReadableLaterInItsOwnCondition()
    {
        BindsCleanly("if (a is { Name: var who } && who.Length > 0) { }");
    }

    [Fact]
    public void ASubpatternBinder_IsNotReadableInTheElseBranch()
    {
        var diagnostics = BuildRun("if (a is { Name: var who }) { } else { var probe = who; }");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void ASubpatternBinder_IsNotReadableAfterTheIf()
    {
        var diagnostics = BuildRun("var probe = who;", extraPreamble: "if (a is { Name: var who }) { }");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    [Fact]
    public void ASwitchArmsSubpatternBinder_ReachesItsGuardAndItsResult()
    {
        BindsCleanly("var ok = a switch { { Name: var who } when who.Length > 2 => who, _ => \"none\" };");
    }

    [Fact]
    public void ASwitchArmsSubpatternBinder_DoesNotReachASiblingArm()
    {
        var diagnostics = BuildRun(
            "var ok = a switch { { Name: var who } => who, _ => who };");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER"));
    }

    // ── Where a binder may be introduced ─────────────────────────────────

    [Fact]
    public void ASubpatternBinderUnderOr_IsRefused()
    {
        var diagnostics = BuildRun("var ok = a is { Name: var who } or { Age: > 3 };");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
    }

    [Fact]
    public void ASubpatternBinderUnderNot_IsRefused()
    {
        var diagnostics = BuildRun("var ok = a is not { Name: var who };");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED"));
    }

    [Fact]
    public void ASubpatternBinderInAConjunction_IsPermitted()
    {
        BindsCleanly("var ok = a is { Age: > 3 } and { Name: var who };");
    }

    // ── Names that answer nothing usable ─────────────────────────────────

    [Fact]
    public void ASubpatternNamingNoMember_ErrorsOnceAndDoesNotPark()
    {
        var diagnostics = BuildRun("var ok = a is { Nope: > 3 };");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_MEMBER"));
        _ = InitializerOf<ErrorExpression>("var ok = a is { Nope: > 3 };");
    }

    [Fact]
    public void ASubpatternNamingAMethod_IsRefusedAsNotStorage()
    {
        var diagnostics = BuildRun("var ok = a is { Tag: > 3 };");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_PATTERN_SUBPATTERN_NOT_STORAGE"));
        Assert.Empty(Errors(diagnostics, "MIST_PM_UNRESOLVED_MEMBER"));
    }

    /// <summary>
    /// An unknown scrutinee has no members to speak of, so a subpattern against one is not a mistake to
    /// report -- it is a lookup that could not be made. The whole <c>is</c> parks instead, and emission
    /// says the form is unsupported, which keeps this off the two channels that would be wrong: an error
    /// nobody made, or an ErrorExpression with nothing reported. The last assertion is what makes the
    /// first two non-vacuous -- the SAME scrutinee binds when there is nothing to look up.
    /// </summary>
    [Fact]
    public void AScrutineeWithNoMembersToSpeakOf_ParksWithoutADiagnostic()
    {
        _ = InitializerOf<UnresolvedExpression>("var ok = u is { Length: > 3 };");
        BindsCleanly("var ok = u is { Length: > 3 };");

        _ = Assert.IsType<PropertyPattern>(PatternOf("var ok = u is { };"));
    }

    [Fact]
    public void AnEmptyPropertyPatternOnAnUnknownScrutinee_NarrowsNothing()
    {
        _ = Assert.IsType<UnknownType>(PatternOf("var ok = u is { };").NarrowedType);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private Pattern PatternOf(string statement)
    {
        return InitializerOf<IsPatternExpression>(statement).Pattern;
    }

    private PropertySubpattern OnlySubpatternOf(string statement)
    {
        return Assert.Single(Assert.IsType<PropertyPattern>(PatternOf(statement)).Subpatterns);
    }

    private void BindsCleanly(string statement)
    {
        Assert.Empty(BuildRun(statement).Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}