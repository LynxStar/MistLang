// Tier 3 (Component): what the binder records for a purity spelling -- the modifiers a member, a type, an
// accessor and a local function carry, the placements it refuses and the ones it admits -- read off the
// real model builder over Standard's shells, since what a member records combines its own spelling with its
// type's and, for an accessor, its declaration's, which is the builder's chain rather than one decision.

using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class PurityModifierTests : ProgramModelProbe
{
    public PurityModifierTests()
        : base("MistPurityModifier_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("public class Box { public pure int M() { return 1; } }")]
    [InlineData("public struct Box { public pure int M() { return 1; } }")]
    [InlineData("public class Box { public static pure int M() { return 1; } }")]
    [InlineData("public struct Box { public pure readonly int M() { return 1; } }")]
    public void ProgramModel_PureOnAMemberImpliesReadonly(string declaration)
    {
        var modifiers = MethodNamed(TypeIn(declaration), "M").Modifiers;

        Assert.True(modifiers.IsPure);
        Assert.True(modifiers.IsReadonly);
        Assert.False(modifiers.IsLocal);
    }

    [Theory]
    [InlineData("public class Box { public local readonly int M() { return 1; } }")]
    [InlineData("public class Box { public readonly local int M() { return 1; } }")]
    public void ProgramModel_LocalReadonlyIsLocalAndReadonlyButNotPure(string declaration)
    {
        var modifiers = MethodNamed(TypeIn(declaration), "M").Modifiers;

        Assert.True(modifiers.IsLocal);
        Assert.True(modifiers.IsReadonly);
        Assert.False(modifiers.IsPure);
    }

    [Fact]
    public void ProgramModel_LocalAloneIsNeitherReadonlyNorPure()
    {
        var modifiers = MethodNamed(TypeIn("public class Box { public local int M() { return 1; } }"), "M").Modifiers;

        Assert.True(modifiers.IsLocal);
        Assert.False(modifiers.IsReadonly);
        Assert.False(modifiers.IsPure);
    }

    [Fact]
    public void ProgramModel_APureTypeMakesEveryMemberPureAndReadonly()
    {
        var type = TypeIn("public pure class Box { public Box() { } public int M() { return 1; } public static int S() { return 1; } public int P { get { return 1; } } public int this[int i] { get { return i; } } }");

        MemberModifiers[] members =
        [
            type.ConstructorsList.Single().Modifiers,
            MethodNamed(type, "M").Modifiers,
            MethodNamed(type, "S").Modifiers,
            type.PropertiesList.Single().Modifiers,
            type.IndexersList.Single().Modifiers,
        ];

        Assert.True(type.Modifiers.IsPure);
        Assert.True(type.Modifiers.IsReadonly);
        Assert.All(members, modifiers => Assert.True(modifiers.IsPure));
        Assert.All(members, modifiers => Assert.True(modifiers.IsReadonly));
        Assert.All(members, modifiers => Assert.False(modifiers.IsLocal));
    }

    [Fact]
    public void ProgramModel_APureStructsMembersArePure()
    {
        var type = TypeIn("public pure struct Box { public int M() { return 1; } public int P { get { return 1; } } }");

        Assert.True(MethodNamed(type, "M").Modifiers.IsPure);
        Assert.True(MethodNamed(type, "M").Modifiers.IsReadonly);
        Assert.True(type.PropertiesList.Single().Modifiers.IsPure);
    }

    [Fact]
    public void ProgramModel_ALocalTypeMakesItsInstanceMembersLocalAndNothingElse()
    {
        var type = TypeIn("public local class Box { public Box() { } public int M() { return 1; } public static int S() { return 1; } public int P { get { return 1; } } public int this[int i] { get { return i; } } }");

        Assert.True(type.Modifiers.IsLocal);
        Assert.True(MethodNamed(type, "M").Modifiers.IsLocal);
        Assert.True(type.PropertiesList.Single().Modifiers.IsLocal);
        Assert.True(type.IndexersList.Single().Modifiers.IsLocal);
        Assert.False(type.ConstructorsList.Single().Modifiers.IsLocal);
        Assert.False(MethodNamed(type, "S").Modifiers.IsLocal);
        Assert.False(MethodNamed(type, "M").Modifiers.IsReadonly);
        Assert.False(MethodNamed(type, "M").Modifiers.IsPure);
    }

    [Fact]
    public void ProgramModel_APureMemberOfALocalTypeIsPureRatherThanLocal()
    {
        var modifiers = MethodNamed(TypeIn("public local class Box { public pure int Q() { return 1; } }"), "Q").Modifiers;

        Assert.True(modifiers.IsPure);
        Assert.True(modifiers.IsReadonly);
        Assert.False(modifiers.IsLocal);
    }

    [Fact]
    public void ProgramModel_ALocalReadonlyTypeMakesItsInstanceMembersLocalReadonly()
    {
        var type = TypeIn("public local readonly class Box { public int M() { return 1; } public static int S() { return 1; } }");

        Assert.True(type.Modifiers.IsLocal);
        Assert.True(type.Modifiers.IsReadonly);
        Assert.True(MethodNamed(type, "M").Modifiers.IsLocal);
        Assert.True(MethodNamed(type, "M").Modifiers.IsReadonly);
        Assert.False(MethodNamed(type, "M").Modifiers.IsPure);
        Assert.False(MethodNamed(type, "S").Modifiers.IsLocal);
    }

    [Theory]
    [InlineData("pure int F(int x) { return x; } var r = F(1);")]
    [InlineData("local int F(int x) { return x; } var r = F(1);")]
    [InlineData("local readonly int F(int x) { return x; } var r = F(1);")]
    public void ProgramModel_ALocalFunctionCarriesItsPromise(string statement)
    {
        Assert.Empty(BuildRun(statement));

        var modifiers = BodyIn(statement).Statements.OfType<LocalFunctionStatement>().Single().Function.Modifiers;

        Assert.Equal(statement.StartsWith("pure", StringComparison.Ordinal), modifiers.IsPure);
        Assert.Equal(statement.StartsWith("pure", StringComparison.Ordinal) || statement.Contains("readonly", StringComparison.Ordinal), modifiers.IsReadonly);
        Assert.Equal(statement.StartsWith("local", StringComparison.Ordinal), modifiers.IsLocal);
    }

    [Fact]
    public void ProgramModel_APureTypesFieldIsReadonlyAndPromisesNothing()
    {
        var declaration = "public pure class Box { public readonly int Item; public int Other; public pure Box() { Item = 1; Other = 2; } }";
        Assert.Empty(BuildRun("", declaration));

        var type = TypeIn(declaration);

        Assert.All(type.FieldsList, field => Assert.True(field.Modifiers.IsReadonly));
        Assert.All(type.FieldsList, field => Assert.False(field.Modifiers.IsPure));
    }

    [Theory]
    [InlineData("public class Box { public pure local int M() { return 1; } }")]
    [InlineData("public class Box { public local pure int M() { return 1; } }")]
    [InlineData("public pure local class Box { }")]
    [InlineData("public local pure struct Box { }")]
    public void ProgramModel_PureAndLocalOnOneDeclarationAreRefused(string declaration)
    {
        AssertSingleError(declaration, "MIST_PM_PURE_AND_LOCAL");
    }

    [Theory]
    [InlineData("public class Box { public local static int S() { return 1; } }")]
    [InlineData("public class Box { public static local int S() { return 1; } }")]
    [InlineData("public class Box { public local Box() { } }")]
    public void ProgramModel_LocalRequiresAnInstance(string declaration)
    {
        AssertSingleError(declaration, "MIST_PM_LOCAL_REQUIRES_INSTANCE");
    }

    [Theory]
    [InlineData("public class Box { public pure int Item; }")]
    [InlineData("public class Box { public local int Item; }")]
    [InlineData("public pure type Step = int;")]
    [InlineData("public struct Unit { public int Value; } public pure type Meters = new Unit;")]
    [InlineData("public pure enum Color : int { Red = 1 }")]
    [InlineData("public class Box { public static local Box operator +(Box a, Box b) { return a; } }")]
    [InlineData("public class Box { public static local implicit operator int(Box b) { return 1; } }")]
    public void ProgramModel_PurityModifiersAreRefusedWhereNoScopeMakesThePromise(string declaration)
    {
        AssertSingleError(declaration, "MIST_PM_PURITY_MODIFIER_PLACEMENT");
    }

    [Theory]
    [InlineData("public struct Box { public pure readonly int M() { return 1; } }")]
    [InlineData("public class Box { public readonly pure int M() { return 1; } }")]
    public void ProgramModel_ReadonlyBesidePureIsReportedAsImplied(string declaration)
    {
        var diagnostics = BuildRun("", declaration);

        var info = Assert.Single(diagnostics, diagnostic => diagnostic.Id.Value == "MIST_PM_READONLY_IMPLIED_BY_PURE");
        Assert.Equal(DiagnosticSeverity.Info, info.Severity);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("public class Box { public pure int M() { return 1; } }")]
    [InlineData("public class Box { public local int M() { return 1; } }")]
    [InlineData("public class Box { public local readonly int M() { return 1; } }")]
    [InlineData("public class Box { public readonly local int M() { return 1; } }")]
    [InlineData("public class Box { public pure Box() { } }")]
    [InlineData("public class Box { public pure int P { get { return 1; } } }")]
    [InlineData("public class Box { public local int P { get { return 1; } } }")]
    [InlineData("public class Box { public local readonly int this[int i] { get { return i; } } }")]
    [InlineData("public class Box { public pure int this[int i] { get { return i; } } }")]
    [InlineData("public class Box { public static pure Box operator +(Box a, Box b) { return a; } }")]
    [InlineData("public class Box { public static pure implicit operator int(Box b) { return 1; } }")]
    [InlineData("public pure class Box { }")]
    [InlineData("public local class Box { }")]
    [InlineData("public local readonly class Box { }")]
    [InlineData("public readonly local class Box { }")]
    [InlineData("public pure struct Box { }")]
    [InlineData("public local struct Box { }")]
    [InlineData("public pure abstract Box { pure int M(); }")]
    [InlineData("public local abstract Box { local readonly int M(); }")]
    public void ProgramModel_AdmittedSpellingsReportNothing(string declaration)
    {
        Assert.Empty(BuildRun("", declaration));
    }

    private void AssertSingleError(string declaration, string id)
    {
        var diagnostics = BuildRun("", declaration);

        var error = Assert.Single(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(id, error.Id.Value);
    }

    private ConcreteType TypeIn(string declaration)
    {
        var model = BuildModel("", declaration, "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["Probe.Box"]);
    }

    private static Method MethodNamed(ConcreteType type, string name)
    {
        return type.MethodsList.Single(method => method.Name == name);
    }
}