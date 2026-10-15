// Tier 1 (Unit): what the scope chain answers for `this`, read off hand-built scopes. An instance
// member reaches its type's own definition, a static member, an initializer and a chain with no type
// each refuse in their own way, and a receiver a scope supplies already typed is answered as supplied.

using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ReceiverLookupTests
{
    [Fact]
    public void AnInstanceMemberReachesItsTypesDefinition()
    {
        var tag = HandBuiltModel.Class("Tag<>");
        var body = new BodyScope(new MethodScope(tag.Scope));

        Assert.Same(tag, Assert.IsType<ReceiverResult>(body.Lookup(LookupRequest.Receiver(null))).Type);
    }

    [Fact]
    public void ANestedTypesInstanceMemberReachesTheNestedDefinition()
    {
        var outer = HandBuiltModel.Class("Outer<>");
        var inner = new ClassType("Inner<>", "Probe", outer.Scope, outer, null);
        var body = new BodyScope(new MethodScope(inner.Scope));

        Assert.Same(inner, Assert.IsType<ReceiverResult>(body.Lookup(LookupRequest.Receiver(null))).Type);
    }

    [Fact]
    public void AStaticMemberHasNoInstance()
    {
        var tag = HandBuiltModel.Class("Tag<>");
        var body = new BodyScope(new MethodScope(tag.Scope) { IsStatic = true });

        _ = Assert.IsType<StaticReceiverResult>(body.Lookup(LookupRequest.Receiver(null)));
    }

    [Fact]
    public void AnInitializerHasNoInstance()
    {
        var tag = HandBuiltModel.Class("Tag<>");

        _ = Assert.IsType<StaticReceiverResult>(new InitializerScope(tag.Scope).Lookup(LookupRequest.Receiver(null)));
    }

    [Fact]
    public void AChainWithNoTypeAnswersNothing()
    {
        var body = new BodyScope(new MethodScope(new Scope(null)));

        _ = Assert.IsType<NotFoundResult>(body.Lookup(LookupRequest.Receiver(null)));
    }

    [Fact]
    public void ASuppliedReceiverIsAnsweredAsTyped()
    {
        var source = HandBuiltModel.Class("Box<>");
        source.TypeParametersList.Add(new TypeParameter("T", 0, source.Scope, source));
        var construction = new TypeInterner().GetOrCreateConstructedType(source, [HandBuiltModel.Class("Int32")]);
        var body = new BodyScope(new MethodScope(new ReceiverScope(source.Scope, construction)));

        Assert.Same(construction, Assert.IsType<ReceiverResult>(body.Lookup(LookupRequest.Receiver(null))).Type);
    }
}