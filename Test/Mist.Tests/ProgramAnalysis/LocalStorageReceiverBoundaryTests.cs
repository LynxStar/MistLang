using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class LocalStorageReceiverBoundaryTests
{
    [Fact]
    public void InstanceFieldBelongsToTheLocalStorageOnlyForTheStructReceiver()
    {
        var payload = HandBuiltModel.Class("Payload");
        var valueField = AccessField(HandBuiltModel.Struct("ValueHolder"), payload);
        var referenceField = AccessField(HandBuiltModel.Class("ReferenceHolder"), payload);

        var valueRoot = new LocalStoragePath(valueField.Receiver.Local);
        Assert.Equal(new LocalStoragePath(valueField.Receiver.Local, valueRoot, valueField.Field),
            LocalStoragePath.Of(valueField.Access));
        Assert.Equal(new LocalStoragePath(referenceField.Receiver.Local), LocalStoragePath.Of(referenceField.Receiver));
        Assert.Null(LocalStoragePath.Of(referenceField.Access));
    }

    private static (LocalReferenceExpression Receiver, Field Field, MemberAccessExpression Access) AccessField(
        ConcreteType owner, IMistType payload)
    {
        var field = new Field("item", payload, default, default) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        var local = new LocalVariable("holder", owner);
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), local);
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, field.Name, field.Type,
            member: new FieldResult(field, owner, []));
        return (receiver, field, access);
    }
}