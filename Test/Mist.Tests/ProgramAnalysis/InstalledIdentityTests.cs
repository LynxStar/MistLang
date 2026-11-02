using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class InstalledIdentityTests
{
    private readonly LifetimeFragments f = new();

    public static TheoryData<string, SubstructuralDiscipline> Shapes => new()
    {
        { "class", SubstructuralDiscipline.Linear },
        { "struct", SubstructuralDiscipline.Linear },
        { "class", SubstructuralDiscipline.Affine },
        { "struct", SubstructuralDiscipline.Affine },
    };

    private IMistType Type(string shape, SubstructuralDiscipline discipline)
    {
        return f.DisciplinedValue(shape, discipline);
    }

    private IReadOnlyList<Diagnostic> Analyze(Block body)
    {
        return f.Model.Analyze(new OwnershipAnalyzer(), body);
    }

    private IfStatement Alias(LocalVariable source, LocalVariable alias, params Statement[] statements)
    {
        return new IfStatement(f.Is(Ref(source), alias), HandBuiltModel.Block(statements), null);
    }

    private Statement Consume(LocalVariable local, string action)
    {
        var sink = new Method("Consume", f.Void, [new Parameter("v", local.Type, new ParameterModifier(false, false, action == "plain" ? OwnershipKind.Unrestricted : OwnershipKind.Owned))], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        return action switch
        {
            "return" => Return(Ref(local)),
            "move" or "plain" => Run(f.Call(sink, Ref(local))),
            _ => Run(Drop(local)),
        };
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ABranchThatMayRetainTheInstalledValueCannotDropIt(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(
            f.If(HandBuiltModel.Block(Run(Assign(r, Fresh(r.Type))))), Run(Drop(r)))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ABranchThatMayRetainTheInstalledValueIsDeadAfterPop(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(
            f.If(HandBuiltModel.Block(Run(Assign(r, Fresh(r.Type))))))), Run(f.Count(r)));

        Assert.Equal("MIST0227", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void FreshValuesOnEveryBranchRemainIndependent(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(
            f.If(HandBuiltModel.Block(Run(Assign(r, Fresh(r.Type)))), HandBuiltModel.Block(Run(Assign(r, Fresh(r.Type))))), Run(Drop(r)))));

        Assert.Empty(Analyze(body));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ALoopThatMayNotRunRetainsTheRestriction(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(
            f.While(HandBuiltModel.Block(Run(Assign(r, Fresh(r.Type))))), Run(Drop(r)))));

        Assert.Contains(Analyze(body), diagnostic => diagnostic.Number.Value == "MIST0479");
    }

    [Fact]
    public void RepeatedInstallationsKeepEachFreshReassignmentIndependent()
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), f.While(HandBuiltModel.Block(
            With(Ref(r), HandBuiltModel.Block(Run(Assign(r, f.New(2))),
                f.If(HandBuiltModel.Block(Consume(r, "move"), new BreakStatement())))))));

        Assert.Empty(Analyze(body));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void TransitivePatternAliasesCannotConsumeTheInstalledValue(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var a = f.Local("a", r.Type);
        var b = f.Local("b", r.Type);
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(Alias(r, a, Alias(a, b, Run(Drop(b)))))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [InlineData("drop")]
    [InlineData("move")]
    [InlineData("return")]
    [InlineData("plain")]
    public void AnAliasBoundBeforeInstallationRemainsProtectedAfterHolderReassignment(string action)
    {
        var r = f.Local("r");
        var a = f.Local("a");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), Alias(r, a,
            With(Ref(r), HandBuiltModel.Block(Run(Assign(r, f.New(2))), Consume(a, action)))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void APatternAliasBoundAfterFreshReassignmentIsIndependent(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var a = f.Local("a", r.Type);
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(
            Run(Assign(r, Fresh(r.Type))), Alias(r, a, Run(Drop(a))))));

        Assert.Empty(Analyze(body));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AnAliasBoundBeforeInstallationIsDeadAfterPop(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var a = f.Local("a", r.Type);
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), Alias(r, a,
            With(Ref(r), HandBuiltModel.Block(Run(Assign(r, Fresh(r.Type))))), Run(f.Count(a))));

        Assert.Equal("MIST0227", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, false)]
    [InlineData(OwnershipKind.Borrowed, false)]
    [InlineData(OwnershipKind.Unrestricted, true)]
    public void LoansAndTheirPatternAliasesCannotDropTheInstalledValue(OwnershipKind ownership, bool byRef)
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", r.Type) { Ownership = ownership, IsRef = byRef, IsReadonly = byRef };
        var a = f.Local("a");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), Alias(lease, a, Run(Drop(a))))));

        Assert.Contains(Analyze(body), diagnostic => diagnostic.Number.Value == "MIST0479");
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, false)]
    [InlineData(OwnershipKind.Borrowed, false)]
    [InlineData(OwnershipKind.Unrestricted, true)]
    public void ALoanCanBeReadWithoutConsumingTheInstalledValue(OwnershipKind ownership, bool byRef)
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", r.Type) { Ownership = ownership, IsRef = byRef, IsReadonly = byRef };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), Run(Ref(lease)))));

        Assert.Empty(Analyze(body));
    }

    [Fact]
    public void AMutableRefLoanIsNonConsumingButCannotDropTheInstalledValue()
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", r.Type) { IsRef = true };
        var valid = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(Declare(lease, Ref(r)), Run(Ref(lease)))));
        var invalid = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(Declare(lease, Ref(r)), Run(Drop(lease)))));

        Assert.Empty(Analyze(valid));
        Assert.Equal("MIST0479", Assert.Single(Analyze(invalid)).Number.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefLoanCanReadReinitializedStorage(bool throughLoan)
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", r.Type) { IsRef = true };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), Run(Assign(throughLoan ? lease : r, f.New(2))), Run(Ref(lease)))));

        Assert.Empty(Analyze(body));
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen)]
    [InlineData(OwnershipKind.Borrowed)]
    public void ALoanRetainsTheOldInstanceWithoutRestrictingAFreshHolder(OwnershipKind ownership)
    {
        var r = f.Local("r", Type("class", SubstructuralDiscipline.Linear));
        var lease = f.Local("lease", r.Type, ownership);
        var independent = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), Run(Assign(r, Fresh(r.Type))), Run(f.Count(r)), Run(Drop(r)))));
        var retained = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), Run(Assign(r, Fresh(r.Type))), Run(Drop(lease)))));

        Assert.Empty(Analyze(independent));
        Assert.Contains(Analyze(retained), diagnostic => diagnostic.Number.Value == "MIST0479");
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, false)]
    [InlineData(OwnershipKind.Borrowed, false)]
    [InlineData(OwnershipKind.Unrestricted, true)]
    public void InstallingALoanProtectsItWithoutMakingItDeadAtPop(OwnershipKind ownership, bool byRef)
    {
        var lease = new LocalVariable("lease", f.Token) { Ownership = ownership, IsRef = byRef, IsReadonly = byRef };
        var invalid = HandBuiltModel.Block(With(Ref(lease), HandBuiltModel.Block(Run(Drop(lease)))));
        var valid = HandBuiltModel.Block(With(Ref(lease), HandBuiltModel.Block(Run(Ref(lease)))), Run(Ref(lease)));

        Assert.Equal("MIST0479", Assert.Single(Analyze(invalid)).Number.Value);
        Assert.Empty(Analyze(valid));
    }

    [Fact]
    public void AFrozenLoanCreatedBeforeInstallationRetainsTheInstalledIdentity()
    {
        var r = f.Local("r");
        var lease = f.Local("lease", ownership: OwnershipKind.Frozen);
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), Declare(lease, Ref(r)),
            With(Ref(r), HandBuiltModel.Block(Run(Assign(r, f.New(2))), Run(Drop(lease)))));

        Assert.Contains(Analyze(body), diagnostic => diagnostic.Number.Value == "MIST0479");
    }

    [Fact]
    public void ASwitchPatternAliasUsesTheValueAtItsBindingStep()
    {
        var r = f.Local("r");
        var a = f.Local("a");
        var pattern = new TypeTestPattern(HandBuiltModel.Node(), r.Type, a);
        var choice = new SwitchExpression(HandBuiltModel.Node(), Ref(r), [new SwitchArm(HandBuiltModel.Node(), pattern, null, Drop(a))], f.Void);
        var protectedBody = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(Run(choice))));
        var freshBody = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(Run(Assign(r, f.New(2))), Run(choice))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(protectedBody)).Number.Value);
        Assert.Empty(Analyze(freshBody));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void ASecondOwningInstallationOfTheSameValueIsRefused(string shape, SubstructuralDiscipline discipline)
    {
        var r = f.Local("r", Type(shape, discipline));
        var body = HandBuiltModel.Block(Declare(r, Fresh(r.Type)), With(Ref(r), HandBuiltModel.Block(With(Ref(r), HandBuiltModel.Block()))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Fact]
    public void ASecondOwningInstallationOfANamedContextBindingIsRefused()
    {
        var named = new LocalVariable("named", f.Token) { IsContextBound = true };
        var body = HandBuiltModel.Block(With(f.New(1), HandBuiltModel.Block(With(Ref(named), HandBuiltModel.Block())), named));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Fact]
    public void AContextBindingOfAnExplicitLoanRemainsNonOwning()
    {
        var lease = f.Local("lease", ownership: OwnershipKind.Frozen);
        var named = new LocalVariable("named", f.Token) { IsContextBound = true };
        var body = HandBuiltModel.Block(With(Ref(lease), HandBuiltModel.Block(With(Ref(named), HandBuiltModel.Block())), named));

        Assert.Empty(Analyze(body));
    }

    [Fact]
    public void AFreshHolderMayHaveAnIndependentNestedOwningScope()
    {
        var r = f.Local("r");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Run(Assign(r, f.New(2))), With(Ref(r), HandBuiltModel.Block()))));

        Assert.Empty(Analyze(body));
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, false)]
    [InlineData(OwnershipKind.Borrowed, false)]
    [InlineData(OwnershipKind.Unrestricted, true)]
    public void ExplicitLoansPermitNestedInstallationAndNonConsumingCalls(OwnershipKind ownership, bool byRef)
    {
        var r = f.Local("r");
        var lease = new LocalVariable("lease", r.Type) { Ownership = ownership, IsRef = byRef, IsReadonly = byRef };
        var inspect = new Method("Inspect", f.Void, [new Parameter("lease", r.Type, new ParameterModifier(byRef, byRef, ownership))],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = f.Sink };
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), With(Ref(r), HandBuiltModel.Block(
            Declare(lease, Ref(r)), With(Ref(lease), HandBuiltModel.Block(Run(f.Call(inspect, Ref(lease))))))));

        Assert.Empty(Analyze(body));
    }

    [Fact]
    public void LeavingAnInnerScopeDoesNotClearAnOuterRestriction()
    {
        var outer = f.Local("outer");
        var inner = f.Local("inner");
        var a = f.Local("a");
        var body = HandBuiltModel.Block(Declare(outer, f.New(1)), Declare(inner, f.New(2)),
            With(Ref(outer), HandBuiltModel.Block(Alias(outer, a, With(Ref(inner), HandBuiltModel.Block()), Run(Drop(a))))));

        Assert.Equal("MIST0479", Assert.Single(Analyze(body)).Number.Value);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AContextBindingCannotDropTheAmbientValue(string shape, SubstructuralDiscipline discipline)
    {
        var r = new LocalVariable("context", Type(shape, discipline)) { IsContextBound = true };
        var body = HandBuiltModel.Block(Run(Drop(r)));
        var method = f.Function("Use", body);

        Assert.Contains(f.Model.Analyze(new OwnershipAnalyzer(), body, member: method), diagnostic => diagnostic.Number.Value == "MIST0479");
    }

    [Theory]
    [InlineData("drop")]
    [InlineData("move")]
    [InlineData("return")]
    [InlineData("plain")]
    public void AContextBindingAndItsAliasesCannotConsumeTheAmbientValue(string action)
    {
        var r = new LocalVariable("context", f.Token) { IsContextBound = true };
        var a = f.Local("a");
        var b = f.Local("b");
        var body = HandBuiltModel.Block(Alias(r, a, Alias(a, b, Consume(b, action))));
        var method = f.Function("Use", body);

        Assert.Contains(f.Model.Analyze(new OwnershipAnalyzer(), body, member: method), diagnostic => diagnostic.Number.Value == "MIST0479");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void AContextBindingMayBeReadAndLent(string shape, SubstructuralDiscipline discipline)
    {
        var r = new LocalVariable("context", Type(shape, discipline)) { IsContextBound = true };
        var peek = new Method("Peek", f.Void, [Owned("v", r.Type, OwnershipKind.Frozen)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var body = HandBuiltModel.Block(Run(f.Count(r)), Run(f.Call(peek, Ref(r))));

        Assert.Empty(Analyze(body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }

    [Theory]
    [InlineData(SubstructuralDiscipline.Relevant)]
    [InlineData(SubstructuralDiscipline.None)]
    public void RelevantAndUnrestrictedContextValuesRemainConsumable(SubstructuralDiscipline discipline)
    {
        var r = new LocalVariable("context", Type("class", discipline)) { IsContextBound = true };
        Assert.Empty(Analyze(HandBuiltModel.Block(Run(Drop(r)))));
    }
}