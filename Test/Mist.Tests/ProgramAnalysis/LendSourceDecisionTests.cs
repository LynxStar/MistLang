using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LendFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class LendSourceDecisionTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("static field", ThisTier.None)]
    [InlineData("plain field of this", ThisTier.None)]
    [InlineData("plain field of an owned local", ThisTier.None)]
    [InlineData("plain field of a borrowed parameter", ThisTier.None)]
    [InlineData("array element", ThisTier.None)]
    [InlineData("call not returning owned", ThisTier.None)]
    [InlineData("construction keeping this", ThisTier.None)]
    [InlineData("owned field of a static holder", ThisTier.None)]
    [InlineData("owned field of a construction keeping this", ThisTier.None)]
    [InlineData("selection reaching a static field", ThisTier.None)]
    [InlineData("owned local", ThisTier.Owned)]
    [InlineData("borrowed parameter", ThisTier.Borrowed)]
    [InlineData("frozen parameter", ThisTier.Frozen)]
    [InlineData("owned field of an owned local", ThisTier.Owned)]
    [InlineData("owned field of this", ThisTier.Owned)]
    [InlineData("fresh construction", ThisTier.Owned)]
    [InlineData("call returning owned", ThisTier.Owned)]
    [InlineData("static field of a type that cannot change", ThisTier.Owned)]
    public void OnlyAnExclusiveHolderLendsBorrowedOrFrozenAccess(string source, ThisTier lends)
    {
        Assert.Equal(lends, LocalOwnership.Empty.Lends(Source(source)));
    }

    [Theory]
    [InlineData("borrowed parameter")]
    [InlineData("frozen parameter")]
    public void AnOwnedFieldOfALentHolderKeepsWhatItLendsToday(string holder)
    {
        var parent = Parameter("p", f.Holder, Tier(holder == "borrowed parameter" ? "borrowed" : "frozen"));

        Assert.Equal(ThisTier.Owned, LocalOwnership.Empty.Lends(Access(Ref(parent), f.Kept)));
    }

    [Theory]
    [InlineData(true, ThisTier.None)]
    [InlineData(false, ThisTier.Owned)]
    public void AnInferredLocalAClosureCapturesIsNotHeldExclusively(bool captured, ThisTier lends)
    {
        var s = new LocalVariable("s", f.Box) { InfersOwnership = true };
        var source = Ref(s);
        List<Statement> statements = [Declare(s, Fresh(f.Box)), Declare(new LocalVariable("o", f.Box) { Ownership = OwnershipKind.Borrowed }, source)];
        if(captured)
        {
            statements.Insert(1, Run(f.Lambda(Access(Ref(s), f.V))));
        }

        Assert.Equal(lends, LocalOwnership.For(HandBuiltModel.Block([.. statements])).Lends(source));
    }

    private Expression Source(string source)
    {
        var owned = new LocalVariable("p", f.Holder) { Ownership = OwnershipKind.Owned };
        var made = new Method("Make", f.Box, [], new MemberModifiers { IsStatic = true });
        return source switch
        {
            "static field" => Static(f.S),
            "plain field of this" => Access(new ThisExpression(HandBuiltModel.Node(), f.Holder), f.In),
            "plain field of an owned local" => Access(Ref(owned), f.In),
            "plain field of a borrowed parameter" => Access(Ref(Parameter("p", f.Holder, Tier("borrowed"))), f.In),
            "array element" => f.Element(Ref(new LocalVariable("arr", f.BoxArray))),
            "call not returning owned" => Call(made),
            "construction keeping this" => Leaky(),
            "owned field of a static holder" => Access(Static(f.Shared), f.Kept),
            "owned field of a construction keeping this" => Access(Leaky(), f.Kept),
            "selection reaching a static field" => new CoalesceExpression(HandBuiltModel.Node(), Static(f.S), Fresh(f.Box), f.Box),
            "owned local" => Ref(new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned }),
            "borrowed parameter" => Ref(Parameter("x", f.Box, Tier("borrowed"))),
            "frozen parameter" => Ref(Parameter("x", f.Box, Tier("frozen"))),
            "owned field of an owned local" => Access(Ref(owned), f.Kept),
            "owned field of this" => Access(new ThisExpression(HandBuiltModel.Node(), f.Holder), f.Kept),
            "fresh construction" => Fresh(f.Box),
            "call returning owned" => Call(new Method("Make", f.Box, [], new MemberModifiers { IsStatic = true }) { ReturnOwnership = OwnershipKind.Owned }),
            _ => Static(f.Name),
        };
    }
}