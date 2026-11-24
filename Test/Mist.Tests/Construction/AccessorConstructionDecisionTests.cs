using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class AccessorConstructionDecisionTests
{
    private readonly BindingFragments f = new();

    [Theory]
    [InlineData("private")]
    [InlineData("static")]
    public void AModifierOnAConcretePropertysGetterIsRefused(string written)
    {
        _ = Getter(Modifier(written), Site());

        _ = AssertSingle("MIST0309");
    }

    [Theory]
    [InlineData("get")]
    [InlineData("set")]
    public void APrivateModifierOnAnAbstractPropertysAccessorIsRefusedAsAbstract(string accessor)
    {
        var site = Site(isAbstract: true);

        _ = accessor == "get" ? Getter(new PrivateModifierModifierNode(), site) : Setter(new PrivateModifierModifierNode(), site);

        _ = AssertSingle("MIST0313");
    }

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("protected")]
    public void ASetterModifierOtherThanPrivateIsRefusedAndLeavesTheSetterPublic(string written)
    {
        var setter = Setter(Modifier(written), Site());

        _ = AssertSingle("MIST0310");
        Assert.Equal(Accessibility.Public, setter.Accessibility);
    }

    [Fact]
    public void AnInitAccessorOnAnAbstractPropertyIsRefusedAndDropped()
    {
        var init = new AccessorBuilder(f.Builder).BuildInitAccessor(
            null, null, Site(isAbstract: true), HandBuiltModel.Node(30), isAuto: true, body: null, expression: null);

        Assert.Null(init);
        _ = AssertSingle("MIST0314");
    }

    [Theory]
    [InlineData("pure", "get")]
    [InlineData("local", "set")]
    public void APurityModifierWrittenInAnAccessorsModifierSlotIsRefusedOnlyAsMisplaced(string written, string accessor)
    {
        var accessorNode = accessor == "get" ? Getter(Modifier(written), Site()) : Setter(Modifier(written), Site());

        var diagnostic = AssertSingle("MIST0321");
        Assert.Equal($"'{written}' is not valid on an accessor.", diagnostic.Message);
        Assert.Equal(Accessibility.Public, accessorNode.Accessibility);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("local readonly")]
    public void ALocalPromiseOnAnAccessorOfAPureTypeIsRefused(string promise)
    {
        _ = Getter(null, Site(type: TypeWith(new MemberModifiers { IsPure = true })), Promise(promise));

        _ = AssertSingle("MIST0318");
    }

    [Fact]
    public void APureAccessorOfAPureTypeIsAdmittedAsPureAndReadonly()
    {
        var getter = Getter(null, Site(type: TypeWith(new MemberModifiers { IsPure = true })), Promise("pure"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var own = getter.OwnModifiers!.Value;
        Assert.True(own.IsPure);
        Assert.True(own.IsReadonly);
        Assert.False(own.IsLocal);
    }

    [Fact]
    public void APureAccessorOfALocalTypeReplacesTheTypesLocal()
    {
        var getter = Getter(null, Site(type: TypeWith(new MemberModifiers { IsLocal = true })), Promise("pure"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var own = getter.OwnModifiers!.Value;
        Assert.True(own.IsPure);
        Assert.False(own.IsLocal);
    }

    [Fact]
    public void AnAccessorOfAReadonlyTypeStaysReadonlyBesideItsOwnLocalPromise()
    {
        var setter = Setter(null, Site(type: TypeWith(new MemberModifiers { IsReadonly = true })), Promise("local"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var own = setter.OwnModifiers!.Value;
        Assert.True(own.IsLocal);
        Assert.True(own.IsReadonly);
    }

    [Theory]
    [InlineData("local")]
    [InlineData("local readonly")]
    public void ALocalPromiseOnAStaticPropertysAccessorIsRefused(string promise)
    {
        _ = Setter(null, Site(owner: new MemberModifiers { IsStatic = true }), Promise(promise));

        _ = AssertSingle("MIST0320");
    }

    private PropertyAccessor Getter(ModifierNode? modifier, AccessorSite site, AccessorPromiseNode? promise = null)
    {
        return new AccessorBuilder(f.Builder).BuildGetter(promise, modifier, site, HandBuiltModel.Node(30), isAuto: true, body: null, expression: null);
    }

    private PropertyAccessor Setter(ModifierNode? modifier, AccessorSite site, AccessorPromiseNode? promise = null)
    {
        return new AccessorBuilder(f.Builder).BuildSetter(promise, modifier, site, HandBuiltModel.Node(30), isAuto: true, isInit: false, body: null, expression: null);
    }

    private Mist.Common.Diagnostics.Diagnostic AssertSingle(string expected)
    {
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(expected, diagnostic.Number.Value);
        Assert.Equal(31, diagnostic.Position().Start.Column);
        return diagnostic;
    }

    private static AccessorSite Site(bool isAbstract = false, MemberModifiers owner = default, MemberType? type = null)
    {
        return new AccessorSite("P", owner, type, isAbstract);
    }

    private static ClassType TypeWith(MemberModifiers modifiers)
    {
        var type = HandBuiltModel.Class("Box");
        type.Modifiers = modifiers;
        return type;
    }

    private static ModifierNode Modifier(string written)
    {
        return written switch
        {
            "private" => new PrivateModifierModifierNode(),
            "public" => new PublicModifierModifierNode(),
            "internal" => new InternalModifierModifierNode(),
            "protected" => new ProtectedModifierModifierNode(),
            "static" => new StaticModifierModifierNode(),
            "pure" => new PureModifierModifierNode(),
            "local" => new LocalModifierModifierNode(),
            _ => throw new ArgumentException("Unknown modifier", nameof(written)),
        };
    }

    private static AccessorPromiseNode Promise(string written)
    {
        return written switch
        {
            "pure" => new PureAccessorPromiseAccessorPromiseNode(),
            "local" => new LocalAccessorPromiseAccessorPromiseNode(),
            "local readonly" => new LocalReadonlyAccessorPromiseAccessorPromiseNode(),
            _ => throw new ArgumentException("Unknown promise", nameof(written)),
        };
    }
}