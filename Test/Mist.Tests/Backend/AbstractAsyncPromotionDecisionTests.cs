using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class AbstractAsyncPromotionDecisionTests
{
    private readonly HandBuiltModel built = new();

    [Fact]
    public void ASyncAbstractDefaultKeepsItsSlotAndDoesNotPromoteItsCaller()
    {
        var contract = new AbstractType("IReader", "Probe", null, null, null);
        var method = new Method("Read", built.Type("Int32"), [], new MemberModifiers())
        {
            DeclaringType = contract,
            Body = HandBuiltModel.Block(),
        };
        var caller = new Method("Use", built.Type("Int32"), [], new MemberModifiers());
        built.Model.TaskDrops.RecordDrop(method);
        built.Model.TaskDrops.RecordCall(caller, method);

        using var scope = AsyncPromotion.Enter(built.Model);

        Assert.False(AsyncPromotion.IsPromoted(method));
        Assert.False(AsyncPromotion.EmitsAsync(method));
        Assert.False(AsyncPromotion.IsPromoted(caller));
    }

    [Fact]
    public void AClosedPromotedCalleeCarriesItsDeclarationObligationToTheCaller()
    {
        var declaration = new Method("Read", built.Type("Int32"), [], new MemberModifiers());
        var closed = new Method("Read", built.Type("Int32"), [], new MemberModifiers()) { SourceDeclaration = declaration };
        var caller = new Method("Use", built.Type("Int32"), [], new MemberModifiers());
        built.Model.TaskDrops.RecordDrop(declaration);
        built.Model.TaskDrops.RecordCall(caller, closed);

        using var scope = AsyncPromotion.Enter(built.Model);

        Assert.True(AsyncPromotion.IsPromoted(declaration));
        Assert.True(AsyncPromotion.IsPromoted(closed));
        Assert.True(AsyncPromotion.IsPromoted(caller));
    }

    [Fact]
    public void AClosedCallerIsPromotedUnderItsCanonicalDeclaration()
    {
        var callee = new Method("Read", built.Type("Int32"), [], new MemberModifiers());
        var caller = new Method("Use", built.Type("Int32"), [], new MemberModifiers());
        var closed = new Method("Use", built.Type("Int32"), [], new MemberModifiers()) { SourceDeclaration = caller };
        built.Model.TaskDrops.RecordDrop(callee);
        built.Model.TaskDrops.RecordCall(closed, callee);

        using var scope = AsyncPromotion.Enter(built.Model);

        Assert.True(AsyncPromotion.IsPromoted(caller));
        Assert.True(AsyncPromotion.IsPromoted(closed));
    }
}