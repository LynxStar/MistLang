using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class EnumVariantPatternEmissionTests
{
    private readonly HandBuiltModel built = new();
    private readonly EnumType direction;

    public EnumVariantPatternEmissionTests()
    {
        direction = new EnumType("Direction", "Probe", null, null, null) { UnderlyingType = built.Type("Int32"), Assembly = new MistAssembly("Probe", MistAssemblyKind.Self) };
        direction.MembersList.Add(new EnumMember("North", true, HandBuiltModel.Literal(built.Type("Int32"), 0L)));
        direction.MembersList.Add(new EnumMember("South", true, HandBuiltModel.Literal(built.Type("Int32"), 1L)));
    }

    [Fact]
    public void AVariantTestedUnderAnEquality_StaysOneParenthesizedComparison()
    {
        var flag = Reference("flag", built.Type("Boolean"));
        var tested = Is(Reference("d", direction), Variant("North"));

        Assert.Equal("(d == Probe.Direction.North)", Emit(tested));
        Assert.Equal(
            "flag == (d == Probe.Direction.North)",
            Emit(new BinaryExpression(HandBuiltModel.Node(), flag, BinaryOperatorKind.Equal, tested, built.Type("Boolean"))));
    }

    [Fact]
    public void AVariantArm_BindsTheScrutineeAndComparesInAGuardAheadOfTheArmsOwn()
    {
        var boolean = built.Type("Boolean");
        var guard = new BinaryExpression(HandBuiltModel.Node(),
            Reference("a", boolean), BinaryOperatorKind.LogicalOr, Reference("b", boolean), boolean);
        var switched = new SwitchExpression(HandBuiltModel.Node(), Reference("d", direction),
        [
            new SwitchArm(HandBuiltModel.Node(), Variant("North"), guard, Number(1)),
            new SwitchArm(HandBuiltModel.Node(), Variant("South"), null, Number(2)),
            new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), direction, null), null, Number(0)),
        ], built.Type("Int32"));

        Assert.Equal(
            "d switch { var __e1 when __e1 == Probe.Direction.North && (a || b) => 1, "
            + "var __e2 when __e2 == Probe.Direction.South => 2, var _ => 0 }",
            Emit(switched));
    }

    [Fact]
    public void AVariantInAListPosition_IsHoistedBesideTheListPattern()
    {
        var sequence = SizedSequenceTypes.ArrayOf(direction, null);
        var list = new ListPattern(HandBuiltModel.Node(),
        [
            new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), direction, null), false, direction),
            new ListPatternElement(Variant("South"), false, direction),
        ], sequence);

        Assert.Equal(
            "(steps is [var _, var __e1] && __e1 == Probe.Direction.South)",
            Emit(Is(Reference("steps", sequence), list)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AVariantUnderACombinator_IsRefusedNamingTheVariant(bool disjunction)
    {
        Pattern pattern = disjunction
            ? new DisjunctionPattern(HandBuiltModel.Node(), direction, [Variant("North"), Variant("South")])
            : new NegatedPattern(HandBuiltModel.Node(), direction, Variant("North"));
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var writer = new CSharpWriter();

        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(Is(Reference("d", direction), pattern), writer, diagnostics);

        var refusals = diagnostics.ToImmutableArray();
        Assert.Equal(disjunction ? 2 : 1, refusals.Length);
        Assert.All(refusals, refusal => Assert.Equal(BackendDiagnostics.UnsupportedEnumPattern.Number, refusal.Number));
        Assert.Equal(
            disjunction ? ["Direction.North", "Direction.South"] : ["Direction.North"],
            refusals.Select(refusal => Assert.IsType<SymbolValue>(Assert.Single(refusal.Arguments)).Spelling));
        Assert.DoesNotContain("Probe.Direction.North", writer.ToString(), StringComparison.Ordinal);
    }

    private ComparisonPattern Variant(string name)
    {
        var member = direction.MembersList.Single(variant => variant.Name == name);
        var access = new MemberAccessExpression(HandBuiltModel.Node(),
            new TypeReferenceExpression(HandBuiltModel.Node(), direction), name, direction, member: new EnumMemberResult(member, direction));

        return new ComparisonPattern(HandBuiltModel.Node(), direction, ComparisonKind.Equal, access);
    }

    private IsPatternExpression Is(Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, pattern, built.Type("Boolean"));
    }

    private LiteralExpression Number(int value)
    {
        return HandBuiltModel.Literal(built.Type("Int32"), value);
    }

    private static LocalReferenceExpression Reference(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(expression, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }
}