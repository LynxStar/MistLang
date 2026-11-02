using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class InferredLoanTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void AScopedInferredBorrowPreservesTheSourceAndOwnsNoCleanup()
    {
        var original = f.Local("original", ownership: OwnershipKind.Owned);
        var alias = Inferred("alias");
        var body = HandBuiltModel.Block(Declare(original, f.New(1)),
            HandBuiltModel.Block(Declare(alias, Ref(original)), Run(f.Call(Lend(OwnershipKind.Borrowed), Ref(alias)))), Run(Ref(original)));

        Assert.Equal(ThisTier.Borrowed, LocalOwnership.For(body).Of(alias));
        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
        Assert.Equal(LifetimeRelease.None, LocalLifetimes.Plan(body).ReleaseOf(alias));
    }

    [Theory]
    [InlineData(OwnershipKind.Unrestricted)]
    [InlineData(OwnershipKind.Frozen)]
    public void TransitiveAndSelectedAliasesCannotEscalateSourceRights(OwnershipKind sourceTier)
    {
        var parameter = Owned("input", f.Token, sourceTier);
        var input = new LocalVariable("input", f.Token) { Ownership = sourceTier, Parameter = parameter };
        var first = Inferred("first");
        var second = Inferred("second");
        var mutate = new Method("Mutate", f.Void, [], default) { DeclaringType = f.Token, DeclaredThisTier = ThisTier.Borrowed };
        var receiver = new MemberAccessExpression(HandBuiltModel.Node(), Ref(second), mutate.Name, f.Void);
        var call = new InvocationExpression(HandBuiltModel.Node(), receiver, [], f.Void, mutate, []);
        var body = HandBuiltModel.Block(Declare(first, Ref(input)), Declare(second, f.Selected("switch", Ref(first), Ref(input))), Run(call));

        Assert.Contains(f.Model.Analyze(new TierAnalyzer(), body), diagnostic => diagnostic.Number.Value == "MIST0231");
    }

    [Fact]
    public void AConsumingUseRetainsAMoveAndRejectsLaterSourceUse()
    {
        var original = f.Local("original", ownership: OwnershipKind.Owned);
        var alias = Inferred("alias");
        var body = HandBuiltModel.Block(Declare(original, f.New(1)),
            HandBuiltModel.Block(Declare(alias, Ref(original)), Run(f.Call(f.Take, Ref(alias)))), Run(Ref(original)));

        Assert.Equal(ThisTier.Owned, LocalOwnership.For(body).Of(alias));
        Assert.Contains(f.Model.Analyze(new OwnershipAnalyzer(), body), diagnostic => diagnostic.Number.Value == "MIST0227");
    }

    [Theory]
    [InlineData(OwnershipKind.Borrowed)]
    [InlineData(OwnershipKind.Frozen)]
    public void FreshInferredOwnersCanLend(OwnershipKind ownership)
    {
        var local = Inferred("local");
        var body = HandBuiltModel.Block(Declare(local, f.New(1)), Run(f.Call(Lend(ownership), Ref(local))));
        Assert.Equal(ThisTier.Owned, LocalOwnership.For(body).Of(local));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }

    [Fact]
    public void ExplicitOwnedAnnotationKeepsItsMove()
    {
        var original = f.Local("original", ownership: OwnershipKind.Owned);
        var alias = f.Local("alias", ownership: OwnershipKind.Owned);
        var body = HandBuiltModel.Block(Declare(original, f.New(1)), HandBuiltModel.Block(Declare(alias, Ref(original))), Run(Ref(original)));
        Assert.Contains(f.Model.Analyze(new OwnershipAnalyzer(), body), diagnostic => diagnostic.Number.Value == "MIST0227");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForwardedLoansEndAtTheirLexicalScopes(bool loop)
    {
        var original = f.Local("original", ownership: OwnershipKind.Owned);
        var first = Inferred("first");
        var second = Inferred("second");
        var inner = HandBuiltModel.Block(Declare(second, Ref(first)), Run(f.Call(Lend(OwnershipKind.Borrowed), Ref(second))));
        var lease = HandBuiltModel.Block(Declare(first, Ref(original)), inner, Run(f.Call(Lend(OwnershipKind.Borrowed), Ref(first))));
        var body = HandBuiltModel.Block(Declare(original, f.New(1)), loop ? f.While(lease) : lease, Run(Ref(original)));
        Assert.Equal(ThisTier.Borrowed, LocalOwnership.For(body).Of(first));
        Assert.Equal(ThisTier.Borrowed, LocalOwnership.For(body).Of(second));
        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }

    [Fact]
    public void OverlappingMutableDemandsHaveNoValidLoanAssignment()
    {
        var original = f.Local("original", ownership: OwnershipKind.Owned);
        var alias = Inferred("alias");
        var body = HandBuiltModel.Block(Declare(original, f.New(1)), Declare(alias, Ref(original)),
            Run(f.Call(Lend(OwnershipKind.Borrowed), Ref(alias))), Run(Ref(original)));
        Assert.Contains(f.Model.Analyze(new TierAnalyzer(), body), diagnostic => diagnostic.Number.Value == "MIST0230");
    }

    [Fact]
    public void AssignmentFromASharedInputCannotLaunderReceiverRights()
    {
        var input = new LocalVariable("input", f.Token) { Parameter = Owned("input", f.Token, OwnershipKind.Unrestricted) };
        var alias = Inferred("alias");
        var body = HandBuiltModel.Block(Declare(alias, f.New(1)), Run(Assign(alias, Ref(input))),
            Run(f.Call(Lend(OwnershipKind.Borrowed), Ref(alias))));
        Assert.Equal(ThisTier.None, LocalOwnership.For(body).Of(alias));
        Assert.Contains(f.Model.Analyze(new TierAnalyzer(), body), diagnostic => diagnostic.Number.Value == "MIST0230");
    }

    [Fact]
    public void LendingThroughAliasesConstrainsAFreshOrdinarySourceToOwn()
    {
        var account = HandBuiltModel.Class("Account");
        var original = new LocalVariable("original", account) { InfersOwnership = true };
        var alias = new LocalVariable("alias", account) { InfersOwnership = true };
        var inspect = new Method("Inspect", f.Void, [Owned("value", account, OwnershipKind.Borrowed)], new MemberModifiers { IsStatic = true });
        var body = HandBuiltModel.Block(Declare(original, Fresh(account)),
            HandBuiltModel.Block(Declare(alias, Ref(original)), Run(f.Call(inspect, Ref(alias)))), Run(Ref(original)));
        Assert.Equal(ThisTier.Owned, LocalOwnership.For(body).Of(original));
        Assert.Equal(ThisTier.Borrowed, LocalOwnership.For(body).Of(alias));
        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }

    [Theory]
    [InlineData(OwnershipKind.Borrowed)]
    [InlineData(OwnershipKind.Frozen)]
    public void ExplicitLoansCannotAcquireRightsFromASharedInput(OwnershipKind ownership)
    {
        var account = HandBuiltModel.Class("Account");
        var input = new LocalVariable("input", account) { Parameter = Owned("input", account, OwnershipKind.Unrestricted) };
        var alias = new LocalVariable("alias", account) { Ownership = ownership };
        var body = HandBuiltModel.Block(Declare(alias, Ref(input)));
        Assert.Equal("MIST0230", Assert.Single(f.Model.Analyze(new TierAnalyzer(), body)).Number.Value);
    }

    [Fact]
    public void AReadonlyViewAllowsTheSourceToMutateWithoutAcquiringMutableRights()
    {
        var account = HandBuiltModel.Class("Account");
        var original = new LocalVariable("original", account) { Ownership = OwnershipKind.Owned };
        var alias = new LocalVariable("alias", account) { InfersOwnership = true };
        var mutate = new Method("Mutate", f.Void, [], default) { DeclaredThisTier = ThisTier.Borrowed };
        var read = new Method("Read", f.Void, [], default) { DeclaredThisTier = ThisTier.ReadonlyRef };
        InvocationExpression call(Method method, LocalVariable receiver)
        {
            return new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), Ref(receiver), method.Name, f.Void), [], f.Void, method, []);
        }

        var body = HandBuiltModel.Block(Declare(original, Fresh(account)), Declare(alias, Ref(original)), Run(call(mutate, original)), Run(call(read, alias)));
        Assert.Equal(ThisTier.ReadonlyRef, LocalOwnership.For(body).Of(alias));
        Assert.Empty(f.Model.Analyze(new OwnershipAnalyzer(), body));
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }

    [Fact]
    public void AnInferredOwnerPreservesOwnedFieldExtractionAsAMove()
    {
        var account = HandBuiltModel.Class("Account");
        var resource = HandBuiltModel.Class("Resource");
        var field = new Field("Value", resource, OwnershipKind.Owned, default) { DeclaringType = account };
        account.FieldsList.Add(field);
        var original = new LocalVariable("original", account) { InfersOwnership = true };
        var alias = new LocalVariable("alias", resource) { InfersOwnership = true };
        Expression access()
        {
            return new MemberAccessExpression(HandBuiltModel.Node(), Ref(original), field.Name, field.Type, member: new FieldResult(field, account, []));
        }

        var body = HandBuiltModel.Block(Declare(original, Fresh(account)), Declare(alias, access()), Run(access()));
        Assert.Equal(ThisTier.Owned, LocalOwnership.For(body).Of(original));
        Assert.Equal(ThisTier.Owned, LocalOwnership.For(body).Of(alias));
        Assert.Equal("MIST0227", Assert.Single(f.Model.Analyze(new OwnershipAnalyzer(), body)).Number.Value);
        Assert.Empty(f.Model.Analyze(new TierAnalyzer(), body));
    }

    private LocalVariable Inferred(string name)
    {
        return new LocalVariable(name, f.Token) { InfersOwnership = true };
    }

    private Method Lend(OwnershipKind ownership)
    {
        return new Method("Inspect", f.Void, [Owned("value", f.Token, ownership)], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
    }
}