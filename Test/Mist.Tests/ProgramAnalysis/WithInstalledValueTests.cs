// Tier 2 (Functional): ownership flow over hand-built bodies proves that a with keeps the linear or
// affine value it installs live for the scope -- refusing a drop, a move to an owned destination, an
// owned return or any other consumption of it or an alias of it -- while admitting a non-consuming use,
// a lease and a reassignment that gives the holder an independent fresh value. Relevant and unrestricted
// values keep their ordinary rules.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class WithInstalledValueTests
{
    public static TheoryData<SubstructuralDiscipline> Disciplines =>
        new() { SubstructuralDiscipline.Linear, SubstructuralDiscipline.Affine };

    private readonly LifetimeFragments f = new();

    private LocalVariable Held(SubstructuralDiscipline discipline)
    {
        return f.Local("r", discipline == SubstructuralDiscipline.Affine ? f.Once : f.Token);
    }

    private NewExpression Fresh(LocalVariable held)
    {
        return ReferenceEquals(held.Type, f.Once) ? f.New(1, f.Once) : f.New(1);
    }

    private Method OwnedSink(IMistType type)
    {
        return new Method("Consume", f.Void, [Owned("v", type)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
    }

    private IReadOnlyList<Diagnostic> Analyze(Block body)
    {
        return f.Model.Analyze(new OwnershipAnalyzer(), body);
    }

    [Theory]
    [MemberData(nameof(Disciplines))]
    public void DroppingTheInstalledValueIsRefused(SubstructuralDiscipline discipline)
    {
        var r = Held(discipline);
        var body = HandBuiltModel.Block(Declare(r, Fresh(r)), With(Ref(r), HandBuiltModel.Block(Run(Drop(r)))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [MemberData(nameof(Disciplines))]
    public void MovingTheInstalledValueIntoAnOwnedParameterIsRefused(SubstructuralDiscipline discipline)
    {
        var r = Held(discipline);
        var body = HandBuiltModel.Block(Declare(r, Fresh(r)), With(Ref(r), HandBuiltModel.Block(Run(f.Call(OwnedSink(r.Type), Ref(r))))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [MemberData(nameof(Disciplines))]
    public void MovingTheInstalledValueIntoAFreshBindingIsRefused(SubstructuralDiscipline discipline)
    {
        var r = Held(discipline);
        var moved = f.Local("moved", r.Type);
        var body = HandBuiltModel.Block(Declare(r, Fresh(r)), With(Ref(r), HandBuiltModel.Block(Declare(moved, Ref(r)))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Fact]
    public void ReturningTheInstalledValueIsRefused()
    {
        var r = f.Local("r");
        var take = new Method("Take", f.Token, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = f.Sink,
            Body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(Return(Ref(r))))),
            DeclarationNode = HandBuiltModel.Node(),
        };

        Assert.Equal("MIST0479", Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), take.Body!)).Number.Value);
    }

    [Fact]
    public void ConsumingTheNamedBindingIsRefused()
    {
        var scoped = f.Local("scoped");
        var body = HandBuiltModel.Block(With(f.New(1), HandBuiltModel.Block(Run(f.Call(OwnedSink(f.Token), Ref(scoped)))), scoped));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Fact]
    public void ConsumingAPatternAliasOfTheInstalledValueIsRefused()
    {
        var r = f.Local("r");
        var viewed = f.Local("x");
        var guard = new IfStatement(f.Is(Ref(r), viewed), HandBuiltModel.Block(Run(Drop(viewed))), null);
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(guard)));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Fact]
    public void DroppingTheInstalledValueInANestedBlockIsRefused()
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(HandBuiltModel.Block(Run(Drop(r))))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [MemberData(nameof(Disciplines))]
    public void ReadingTheInstalledValueIsAdmitted(SubstructuralDiscipline discipline)
    {
        var type = f.DisciplinedValue("class", discipline);
        var r = f.Local("r", type);
        var body = HandBuiltModel.Block(Declare(r, LifetimeFragments.Fresh(type)), With(Ref(r), HandBuiltModel.Block(Run(f.Count(r)))));

        Assert.Empty(Analyze(body));
    }

    [Fact]
    public void LendingTheInstalledValueFrozenThroughItsOwnNameIsAdmitted()
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(Run(f.Call(f.Peek, Ref(r))))));

        Assert.Empty(Analyze(body));
    }

    [Theory]
    [MemberData(nameof(Disciplines))]
    public void PassingTheInstalledValueToAPlainParameterIsRefused(SubstructuralDiscipline discipline)
    {
        var r = Held(discipline);
        var inspect = new Method("Inspect", f.Void, [new Parameter("lease", r.Type)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var body = HandBuiltModel.Block(Declare(r, Fresh(r)), With(Ref(r), HandBuiltModel.Block(Run(f.Call(inspect, Ref(r))))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Fact]
    public void LendingTheNamedBindingFrozenIsAdmitted()
    {
        var scoped = f.Local("scoped");
        var body = HandBuiltModel.Block(With(f.New(1), HandBuiltModel.Block(Run(f.Call(f.Peek, Ref(scoped)))), scoped));

        Assert.Empty(Analyze(body));
    }

    [Fact]
    public void ReassigningTheHolderGivesAnIndependentValueThatMayBeConsumed()
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(
            Declare(r, f.New(1)),
            With(Ref(r), HandBuiltModel.Block(Run(Assign(r, f.New(2))), Run(Drop(r)))));

        Assert.Empty(Analyze(body));
    }

    [Fact]
    public void AReadAfterTheScopeIsAUseOfADroppedValue()
    {
        var type = f.DisciplinedValue("class", SubstructuralDiscipline.Linear);
        var r = f.Local("r", type);
        var body = HandBuiltModel.Block(Declare(r, LifetimeFragments.Fresh(type)), With(Ref(r), HandBuiltModel.Block()), Run(f.Count(r)));

        Assert.Equal("MIST0227", Assert.Single(Analyze(body)).Number.Value);
    }

    [Fact]
    public void ARelevantValueMayBeDroppedInsideItsWith()
    {
        var r = f.Local("r", f.Shared);
        var body = HandBuiltModel.Block(Declare(r, f.New(1, f.Shared)), With(Ref(r), HandBuiltModel.Block(Run(Drop(r)))));

        Assert.Empty(Analyze(body));
    }
}