using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class MixedSelectionTests
{
    private readonly LifetimeFragments f = new();

    [Theory]
    [InlineData("switch", "binding")]
    [InlineData("switch", "plain")]
    [InlineData("switch", "owned")]
    [InlineData("switch", "return")]
    [InlineData("switch", "field")]
    [InlineData("switch", "drop")]
    [InlineData("switch", "initializer")]
    [InlineData("switch", "install")]
    [InlineData("switch", "array")]
    [InlineData("coalesce", "binding")]
    [InlineData("coalesce", "plain")]
    [InlineData("coalesce", "owned")]
    [InlineData("coalesce", "return")]
    [InlineData("coalesce", "field")]
    [InlineData("coalesce", "drop")]
    [InlineData("coalesce", "initializer")]
    [InlineData("coalesce", "install")]
    [InlineData("coalesce", "array")]
    public void EveryPossibleInstalledSourceIsCheckedAtTheConsumingOperation(string selection, string operation)
    {
        var r = f.Local("r");
        var selected = f.Selected(selection, Ref(r), f.New(2));
        var take = new Method("Take", f.Void, [Owned("value", f.Token, operation == "owned" ? OwnershipKind.Owned : OwnershipKind.Unrestricted)], new MemberModifiers { IsStatic = true });
        Statement consuming = operation switch
        {
            "binding" => Declare(f.Local("alias"), selected),
            "return" => Return(selected),
            "field" => Run(Store(new Field("Value", f.Token, OwnershipKind.Owned, default), selected)),
            "drop" => Run(DropOf(selected)),
            "install" => With(selected, HandBuiltModel.Block()),
            "initializer" => Run(Initialized(selected)),
            "array" => Run(new ArrayLiteralExpression(HandBuiltModel.Node(), f.Token, [selected], f.Model.Interner.GetOrCreateConstructedType(f.Model.Type("Array<,>"), [f.Token]))),
            _ => Run(f.Call(take, selected)),
        };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(consuming)));
        Assert.Contains(f.Model.Analyze(new OwnershipAnalyzer(), body), diagnostic => diagnostic.Number.Value == "MIST0479");
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("coalesce")]
    public void OwningSelectionTransfersCleanupAtEachArmAndKeepsBothLexicalFlags(string selection)
    {
        var r = f.Local("r");
        var s = f.Local("s");
        var left = Ref(r);
        var right = Ref(s);
        var value = f.Selected(selection, left, right);
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), Declare(s, f.New(2)), With(value, HandBuiltModel.Block()));
        var plan = LocalLifetimes.Plan(body);
        Assert.Equal(r, Assert.Single(plan.MarksAfter(left)).Owner);
        Assert.Equal(s, Assert.Single(plan.MarksAfter(right)).Owner);
        Assert.Empty(plan.MarksAfter(value));
        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(r));
        Assert.Equal(LifetimeRelease.Guarded, plan.ReleaseOf(s));
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("coalesce")]
    public void InferredMixedAccessOwnsNoCleanupAndFreshAlternativesHaveIndependentLifetime(string selection)
    {
        var r = f.Local("r");
        var alias = new LocalVariable("alias", f.Token) { InfersOwnership = true };
        var declaration = Declare(alias, f.Selected(selection, Ref(r), f.New(2)));
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(declaration, Run(f.Call(f.Peek, Ref(alias))))));
        Assert.True(LocalOwnership.For(body).IsLoan(alias));
        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
        Assert.Equal(LifetimeRelease.None, LocalLifetimes.Plan(body).ReleaseOf(alias));
        _ = Assert.Single(LentValues.LoanedBy(declaration, LocalOwnership.For(body)));
    }

    private NewExpression Initialized(Expression value)
    {
        var container = HandBuiltModel.Class("Container");
        container.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        var field = new Field("Value", f.Token, OwnershipKind.Owned, default) { DeclaringType = container };
        container.FieldsList.Add(field);
        var entry = new ObjectInitializerAssignment(HandBuiltModel.Node(), field.Name, value, field.Type, member: new FieldResult(field, container, []));
        return new NewExpression(HandBuiltModel.Node(), container, [], null, [entry], []);
    }

    [Fact]
    public void EmptySelectionHasNoResolvedPlace()
    {
        var value = new SwitchExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(f.Int32), [], f.Token);
        Assert.Same(value, OwnershipAnalyzer.Resolved(value));
    }
}