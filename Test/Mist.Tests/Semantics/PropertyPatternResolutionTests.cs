// Property patterns: what a subpattern name resolves to, and how far the binders it introduces reach.
// Asserted on the MODEL, because output cannot tell these apart. A program that prints the right answer
// says only that SOME member answered; whether the subpattern was typed by that member -- which is what
// makes the nested pattern's own narrowing correct -- is visible only here. The negatives are compile
// failures and so exist nowhere else. Order 93 of backlog/emitter-v2-capability-progression.yaml rests
// on this file.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
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

        public type Hound = Dog;

        public type MaybeDog = Dog?;

        public type IntBox = Box<int>;
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

    [Theory]
    [InlineData("Dog")]
    [InlineData("Probe.Dog")]
    [InlineData("Hound")]
    [InlineData("Probe.Hound")]
    public void ATypedPropertyPattern_ResolvesItsPrefixAndMembersAgainstTheTestedType(string prefix)
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf($"var ok = a is {prefix} {{ Breed: var breed }};"));

        Assert.NotNull(pattern.TypeTest);
        Assert.Equal("Probe.Dog", pattern.TypeTest.NarrowedType.FullyQualifiedName);
        Assert.Same(pattern.TypeTest.NarrowedType, pattern.NarrowedType);
        Assert.Null(pattern.Binding);

        var subpattern = Assert.Single(pattern.Subpatterns);
        var member = Assert.IsType<PropertyResult>(subpattern.Member);

        Assert.Same(pattern.NarrowedType, member.DeclaringType);
        Assert.Equal("Breed", member.Property.Name);
        Assert.Equal("Standard.String", subpattern.Pattern.NarrowedType.FullyQualifiedName);
        BindsCleanly($"var ok = a is {prefix} {{ Breed: var breed }};");
    }

    [Fact]
    public void ATypedPropertyPatternOnUnknown_UsesTheTestedMembersInsteadOfUnknown()
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf("var ok = u is Dog { Breed: var breed, Name: var name } whole;"));

        Assert.Equal("Probe.Dog", pattern.NarrowedType.FullyQualifiedName);
        Assert.NotNull(pattern.Binding);
        Assert.Same(pattern.NarrowedType, pattern.Binding.Type);
        Assert.Equal(["breed", "name"], pattern.Subpatterns.Select(subpattern => subpattern.Pattern.Binding?.Name));
        Assert.All(pattern.Subpatterns, subpattern => Assert.Equal("Standard.String", subpattern.MemberType.FullyQualifiedName));
        Assert.Equal("Probe.Animal", Assert.IsType<PropertyResult>(pattern.Subpatterns[1].Member).DeclaringType.FullyQualifiedName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOptionalAliasPrefix_NarrowsBeforeResolvingMembersAndBindingTheWholeValue(bool withMember)
    {
        var clause = withMember ? "{ Breed: var breed }" : "{ }";
        var pattern = Assert.IsType<PropertyPattern>(PatternOf($"var ok = u is MaybeDog {clause} whole;"));

        Assert.NotNull(pattern.TypeTest);
        Assert.Equal("Probe.Dog", pattern.TypeTest.NarrowedType.FullyQualifiedName);
        Assert.Same(pattern.TypeTest.NarrowedType, pattern.NarrowedType);
        Assert.NotNull(pattern.Binding);
        Assert.Same(pattern.NarrowedType, pattern.Binding.Type);

        if(withMember)
        {
            var subpattern = Assert.Single(pattern.Subpatterns);
            var member = Assert.IsType<PropertyResult>(subpattern.Member);
            Assert.Equal("Breed", member.Property.Name);
            Assert.Equal("Standard.String", subpattern.Pattern.Binding?.Type.FullyQualifiedName);
        }
        else
        {
            Assert.Empty(pattern.Subpatterns);
        }

        BindsCleanly($"if (u is MaybeDog {clause} whole && whole.Breed.Length > 0) {{ var kept = whole; }}");
    }

    [Fact]
    public void AClosedGenericAliasPrefix_SubstitutesTheTypeOfItsMember()
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf("var ok = u is IntBox { item: var number } whole;"));

        Assert.NotNull(pattern.TypeTest);
        var constructed = Assert.IsType<ConstructedType>(pattern.TypeTest.NarrowedType);
        Assert.Equal("Standard.Int32", Assert.Single(constructed.TypeArguments).FullyQualifiedName);
        Assert.Equal("Standard.Int32", Assert.Single(pattern.Subpatterns).Pattern.Binding?.Type.FullyQualifiedName);
        Assert.NotNull(pattern.Binding);
        Assert.Same(constructed, pattern.Binding.Type);
    }

    [Fact]
    public void ATypedPropertyPatternOnAUnion_KeepsTheTypeTestAndItsConstituent()
    {
        var pattern = Assert.IsType<PropertyPattern>(InitializerOf<IsPatternExpression>(
            "var ok = mixed is Dog { Breed: var breed } whole;",
            extraPreamble: "Animal | int mixed = new Dog();").Pattern);

        Assert.NotNull(pattern.TypeTest);
        Assert.NotNull(pattern.TypeTest.Constituent);
        Assert.Equal("Probe.Animal", pattern.TypeTest.Constituent.FullyQualifiedName);
        Assert.Equal("Probe.Dog", pattern.NarrowedType.FullyQualifiedName);
        Assert.Null(pattern.TypeTest.Binding);
        Assert.NotNull(pattern.Binding);
        Assert.Same(pattern.NarrowedType, pattern.Binding.Type);
    }

    [Fact]
    public void AnUntypedWholeValueBinding_KeepsTheNonVoidScrutineeType()
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf("var ok = maybe is { Length: var length } whole;"));

        Assert.Null(pattern.TypeTest);
        Assert.Equal("Standard.String", pattern.NarrowedType.FullyQualifiedName);
        Assert.NotNull(pattern.Binding);
        Assert.Equal("whole", pattern.Binding.Name);
        Assert.Same(pattern.NarrowedType, pattern.Binding.Type);
        Assert.Equal("Standard.Int32", Assert.Single(pattern.Subpatterns).Pattern.Binding?.Type.FullyQualifiedName);
        BindsCleanly("if (maybe is { Length: var length } whole && whole.Length == length) { var kept = whole; }");
    }

    [Fact]
    public void AnUntypedWholeValueBindingOnUnknown_StillKeepsUnknown()
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf("var ok = u is { } whole;"));

        Assert.Null(pattern.TypeTest);
        Assert.NotNull(pattern.Binding);
        _ = Assert.IsType<UnknownType>(pattern.Binding.Type);
    }

    [Fact]
    public void AnEmptyTypedPropertyPattern_KeepsItsTypeTestAndWholeValueBinding()
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf("var ok = a is Dog { } whole;"));

        Assert.Empty(pattern.Subpatterns);
        Assert.NotNull(pattern.TypeTest);
        Assert.NotNull(pattern.Binding);
        Assert.Equal("Probe.Dog", pattern.Binding.Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("a is Dog { Breed: var part } whole", "Breed", "Probe.Dog")]
    [InlineData("a is { Name: var part } whole", "Name", "Probe.Animal")]
    public void WholeAndSubpatternBindings_KeepTheirIdentityInTheConjunctAndBranch(string test, string memberName, string expectedWholeType)
    {
        var branch = Assert.Single(BodyIn($"if ({test} && whole.{memberName} == part) {{ var kept = whole; var piece = part; }}")
            .Statements.OfType<IfStatement>());
        var condition = Assert.IsType<BinaryExpression>(branch.Condition);
        var pattern = Assert.IsType<PropertyPattern>(Assert.IsType<IsPatternExpression>(condition.Left).Pattern);
        var conjunct = Assert.IsType<BinaryExpression>(condition.Right);
        var memberAccess = Assert.IsType<MemberAccessExpression>(conjunct.Left);
        var wholeInCondition = Assert.IsType<LocalReferenceExpression>(memberAccess.Receiver);
        var partInCondition = Assert.IsType<LocalReferenceExpression>(conjunct.Right);
        var declarations = branch.ThenBranch.Statements.OfType<LocalDeclarationStatement>().ToArray();

        Assert.True(branch.ConditionBinds);
        Assert.NotNull(pattern.Binding);
        Assert.Equal(expectedWholeType, pattern.Binding.Type.FullyQualifiedName);
        Assert.Same(pattern.Binding, wholeInCondition.Local);
        Assert.Same(pattern.Binding, Assert.IsType<LocalReferenceExpression>(declarations[0].Initializer).Local);

        var part = Assert.Single(pattern.Subpatterns).Pattern.Binding;
        Assert.NotNull(part);
        Assert.Equal("Standard.String", part.Type.FullyQualifiedName);
        Assert.Same(part, partInCondition.Local);
        Assert.Same(part, Assert.IsType<LocalReferenceExpression>(declarations[1].Initializer).Local);
        BindsCleanly($"if ({test} && whole.{memberName} == part) {{ var kept = whole; var piece = part; }}");
    }

    [Theory]
    [InlineData("if (a is Dog { Breed: var part } whole) { } else { var kept = whole; var piece = part; }")]
    [InlineData("if (a is Dog { Breed: var part } whole) { } var kept = whole; var piece = part;")]
    [InlineData("if (a is { Name: var part } whole) { } else { var kept = whole; var piece = part; }")]
    [InlineData("if (a is { Name: var part } whole) { } var kept = whole; var piece = part;")]
    public void WholeAndSubpatternBindings_DoNotEscapeTheGuardedBranch(string statement)
    {
        Assert.Equal(2, Errors(BuildRun(statement), "MIST_PM_UNRESOLVED_IDENTIFIER").Count());
    }

    [Fact]
    public void SiblingConditions_CanReuseWholeAndSubpatternBindingNames()
    {
        BindsCleanly("if (a is Dog { Breed: var part } whole) { } if (a is { Name: var part } whole) { }");
    }

    [Theory]
    [InlineData("Dog { Breed: var part } whole", "Breed", "Probe.Dog", "dog")]
    [InlineData("{ Name: var part } whole", "Name", "Probe.Animal", "a")]
    public void WholeAndSubpatternBindings_ReachTheirSwitchGuardAndResult(string test, string memberName, string expectedWholeType, string fallbackName)
    {
        var selection = InitializerOf<SwitchExpression>(
            $"var ok = a switch {{ {test} when whole.{memberName} == part => whole, _ => {fallbackName} }};");
        var arm = selection.Arms[0];
        var pattern = Assert.IsType<PropertyPattern>(arm.Pattern);
        var guard = Assert.IsType<BinaryExpression>(arm.Guard);
        var memberAccess = Assert.IsType<MemberAccessExpression>(guard.Left);

        Assert.NotNull(pattern.Binding);
        Assert.Equal(expectedWholeType, pattern.Binding.Type.FullyQualifiedName);
        Assert.Same(pattern.Binding, Assert.IsType<LocalReferenceExpression>(memberAccess.Receiver).Local);
        Assert.Same(pattern.Binding, Assert.IsType<LocalReferenceExpression>(arm.Result).Local);
        Assert.Same(Assert.Single(pattern.Subpatterns).Pattern.Binding, Assert.IsType<LocalReferenceExpression>(guard.Right).Local);
        BindsCleanly($"var ok = a switch {{ {test} when whole.{memberName} == part => whole, _ => {fallbackName} }};");
    }

    [Fact]
    public void AWholeValueSwitchBinding_DoesNotReachTheNextArmOrFollowingStatements()
    {
        var diagnostics = BuildRun(
            "var ok = a switch { Dog { Breed: var part } whole => whole, _ => whole }; var kept = whole; var piece = part;");

        Assert.Equal(3, Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER").Count());
    }

    [Theory]
    [InlineData("a is Dog { Breed: var part } whole or Animal")]
    [InlineData("a is not Dog { Breed: var part } whole")]
    [InlineData("a is { Name: var part } whole or { }")]
    [InlineData("a is not { Name: var part } whole")]
    public void WholeAndSubpatternBindingsUnderOrOrNot_AreBothRefused(string test)
    {
        var diagnostics = BuildRun($"if ({test}) {{ var kept = whole; var piece = part; }}");

        Assert.Equal(2, Errors(diagnostics, "MIST_PM_PATTERN_BINDING_NOT_ESTABLISHED").Count());
        Assert.Equal(2, Errors(diagnostics, "MIST_PM_UNRESOLVED_IDENTIFIER").Count());
    }

    [Theory]
    [InlineData("a is Dog { Breed: var _ } _")]
    [InlineData("a is { Name: var _ } _")]
    public void ADiscardTrailingName_KeepsNeitherWholeNorSubpatternBinding(string test)
    {
        var pattern = Assert.IsType<PropertyPattern>(PatternOf($"var ok = {test};"));

        Assert.Null(pattern.Binding);
        Assert.Null(Assert.Single(pattern.Subpatterns).Pattern.Binding);
        BindsCleanly($"var ok = {test};");
        BindsCleanly($"var ok = {test.Replace("a is ", "a is not ", StringComparison.Ordinal)};");
        BindsCleanly($"var ok = {test} or {{ }};");
    }

    [Fact]
    public void AnUnresolvablePropertyPatternPrefix_ReportsTheTypeInsteadOfLookingUpMembers()
    {
        var diagnostics = BuildRun("var ok = a is Nonexistent { Breed: var breed } whole;");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_TYPE"));
        Assert.Empty(Errors(diagnostics, "MIST_PM_UNRESOLVED_MEMBER"));
        _ = InitializerOf<ErrorExpression>("var ok = a is Nonexistent { Breed: var breed } whole;");
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
    /// AN UNKNOWN SCRUTINEE ANSWERS EVERY MEMBER NAME, with unknown. Nothing is known about its shape, so
    /// the member can be called neither present nor missing, and the pattern that asks for it is the only
    /// thing that could have said -- which is what matching an anonymous type held as unknown IS. The
    /// subpattern therefore binds and takes unknown as its own scrutinee, rather than the whole <c>is</c>
    /// parking as it did while no emission could read a member off an object.
    /// </summary>
    [Fact]
    public void AScrutineeWithNoMembersToSpeakOf_AnswersWithUnknown()
    {
        _ = InitializerOf<IsPatternExpression>("var ok = u is { Length: > 3 };");
        BindsCleanly("var ok = u is { Length: > 3 };");

        _ = Assert.IsType<UnknownType>(OnlySubpatternOf("var ok = u is { Length: var n };").Pattern.NarrowedType);
        _ = Assert.IsType<PropertyPattern>(PatternOf("var ok = u is { };"));
    }

    /// <summary>
    /// A scrutinee that already FAILED keeps parking, and for the reason unknown no longer does: there is
    /// a mistake here, it was reported where the type failed, and answering the member would launder that
    /// failure into a pattern that looks resolved.
    /// </summary>
    [Fact]
    public void AScrutineeThatAlreadyFailed_StillParks()
    {
        _ = InitializerOf<UnresolvedExpression>(
            "var ok = bad is { Length: > 3 };", extraPreamble: "Nonexistent bad = void;");
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