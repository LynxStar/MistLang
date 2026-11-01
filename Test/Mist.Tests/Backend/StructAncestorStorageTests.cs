using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class StructAncestorStorageTests
{
    [Fact]
    public void TheFactoryTransportsOnlyInstanceStorage()
    {
        var owner = HandBuiltModel.Struct("Owner");
        var held = new Field("Held", owner, OwnershipKind.Unrestricted, new MemberModifiers());
        var bound = new Field("MaxValue", owner, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true });
        var property = new Property("Shared", owner, new MemberModifiers { IsStatic = true }) { Getter = new PropertyAccessor() };
        var flattened = new FlattenedStruct([
            new FlattenedField(held, owner, Accessibility.Public) { DeclaringStruct = owner },
            new FlattenedField(bound, owner, Accessibility.Public) { DeclaringStruct = owner },
        ], [], [property]);

        Assert.Equal("Held", Assert.Single(StructAncestorFactory.Members(flattened)).Name);
    }
}