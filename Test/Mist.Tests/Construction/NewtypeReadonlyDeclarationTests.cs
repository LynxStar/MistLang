using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class NewtypeReadonlyDeclarationTests
{
    [Fact]
    public void ACloneWhoseNewtypeWritesReadonly_IsReadonly()
    {
        Assert.True(NewtypeCloner.DeclaresReadonly(Clone("Frozen", new ReadonlyModifierModifierNode())));
    }

    [Fact]
    public void APlainNewtypesClone_IsNotReadonly()
    {
        Assert.False(NewtypeCloner.DeclaresReadonly(Clone("Plain", new PublicModifierModifierNode())));
    }

    [Fact]
    public void AMisplacedPurePromise_DoesNotMakeTheCloneReadonly()
    {
        Assert.False(NewtypeCloner.DeclaresReadonly(Clone("Promised", new PureModifierModifierNode())));
    }

    [Fact]
    public void ACloneOfAReadonlyClone_IsReadonly()
    {
        var frozen = Clone("Frozen", new ReadonlyModifierModifierNode());
        var again = Clone("Again", new PublicModifierModifierNode());
        again.CloneSourceType = frozen;

        Assert.True(NewtypeCloner.DeclaresReadonly(again));
    }

    [Fact]
    public void ACloneOfAnInstanceOfAReadonlyGenericClone_IsReadonly()
    {
        var model = new HandBuiltModel();
        var frozen = Clone("Frozen", new ReadonlyModifierModifierNode());
        frozen.TypeParametersList.Add(new TypeParameter("T", 0, frozen.Scope, frozen));
        var again = Clone("Again");
        again.CloneSourceType = model.Interner.GetOrCreateConstructedType(frozen, [model.Type("Int32")]);

        Assert.True(NewtypeCloner.DeclaresReadonly(again));
    }

    [Fact]
    public void ACloneOfAPlainClone_IsNotReadonly()
    {
        var plain = Clone("Plain");
        var again = Clone("Again");
        again.CloneSourceType = plain;

        Assert.False(NewtypeCloner.DeclaresReadonly(again));
    }

    private static ClassType Clone(string name, params ModifierNode[] modifiers)
    {
        var source = HandBuiltModel.Class("Mutable");
        var declaration = BindingFragments.At<NewtypeDeclarationNode>();
        declaration.Modifiers = [.. modifiers];

        return new ClassType(name, "Probe", source.Scope, null, null)
        {
            CloneSource = source,
            CloneSourceType = source,
            NewtypeDeclaration = declaration,
        };
    }
}