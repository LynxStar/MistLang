using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class UnknownPropertyPatternEmissionTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void EachMemberIsReadDynamicallyThroughOneNullTestedBindingOfTheScrutinee()
    {
        var x = new LocalVariable("x", UnknownType.Instance);
        var pattern = Members(null,
            Member("X", new CatchAllPattern(HandBuiltModel.Node(), UnknownType.Instance, x)),
            Member("Y", new ComparisonPattern(HandBuiltModel.Node(), UnknownType.Instance, ComparisonKind.GreaterThan, Number(5))));

        Assert.Equal(
            "(box is not null and var __e1 && ((dynamic)__e1).X is var x && ((dynamic)__e1).Y is > 5)",
            Emit(Is(Reference("box"), pattern)));
    }

    [Fact]
    public void AWholeBinding_IsTheBindingTheMembersAreReadThrough()
    {
        var whole = new LocalVariable("whole", UnknownType.Instance);
        var pattern = Members(whole, Member("X", new ComparisonPattern(HandBuiltModel.Node(), UnknownType.Instance, ComparisonKind.Equal, Number(1))));

        Assert.Equal(
            "(box is not null and var whole && ((dynamic)whole).X is 1)",
            Emit(Is(Reference("box"), pattern)));
    }

    [Fact]
    public void AMembersOwnHoistedTest_FollowsTheReadThatBindsIt()
    {
        var pattern = Members(null, Member("Name", new RegexPattern(HandBuiltModel.Node(), UnknownType.Instance, "a.*")));
        var switched = new SwitchExpression(HandBuiltModel.Node(), Reference("box"),
        [
            new SwitchArm(HandBuiltModel.Node(), pattern, null, Number(1)),
            new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), UnknownType.Instance, null), null, Number(0)),
        ], built.Type("Int32"));

        Assert.Equal(
            "box switch { not null and var __e1 when ((dynamic)__e1).Name is var __e2 && "
            + RegexLowering.IsFullMatch("__e2", RegexLowering.Quote("a.*")) + " => 1, var _ => 0 }",
            Emit(switched));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnderNotOrOr_ItIsRefusedRatherThanReadThroughNoBinding(bool disjunction)
    {
        var members = Members(null, Member("X", new ComparisonPattern(HandBuiltModel.Node(), UnknownType.Instance, ComparisonKind.Equal, Number(1))));
        Pattern pattern = disjunction
            ? new DisjunctionPattern(HandBuiltModel.Node(), UnknownType.Instance, [members, new TypeTestPattern(HandBuiltModel.Node(), built.Type("String"), null)])
            : new NegatedPattern(HandBuiltModel.Node(), UnknownType.Instance, members);
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var writer = new CSharpWriter();

        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitExpression(Is(Reference("box"), pattern), writer, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.UnsupportedUnknownPropertyPattern.Number, refusal.Number);
        Assert.DoesNotContain("dynamic", writer.ToString(), StringComparison.Ordinal);
    }

    private static PropertyPattern Members(LocalVariable? whole, params PropertySubpattern[] subpatterns)
    {
        return new PropertyPattern(HandBuiltModel.Node(), UnknownType.Instance, subpatterns, whole);
    }

    private static PropertySubpattern Member(string name, Pattern pattern)
    {
        return new PropertySubpattern(HandBuiltModel.Node(), name, pattern, UnknownType.Instance, new NotFoundResult());
    }

    private IsPatternExpression Is(Expression scrutinee, Pattern pattern)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, pattern, built.Type("Boolean"));
    }

    private LiteralExpression Number(int value)
    {
        return HandBuiltModel.Literal(built.Type("Int32"), value);
    }

    private static LocalReferenceExpression Reference(string name)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, UnknownType.Instance));
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