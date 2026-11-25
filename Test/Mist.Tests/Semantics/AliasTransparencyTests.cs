using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class AliasTransparencyTests
{
    private readonly HandBuiltModel built = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void AConstructionOverAnAbsorbingPackAliasBindsEveryArgumentIntoThePack(int count)
    {
        var arguments = new IMistType[] { built.Type("Int32"), built.Type("String"), built.Type("Boolean") }[..count];
        var action = Construct(Action(), arguments);

        var expanded = Assert.IsType<FunctionType>(AliasTransparency.Expand(action, built.Interner));

        Assert.Equal<IMistType>(arguments, expanded.ParameterTypes);
        Assert.Same(built.Type("Void"), expanded.ReturnType);
    }

    [Fact]
    public void AConstructionOverAnAliasNotYetResolvedIsLeftAsWritten()
    {
        var action = Action();
        action.AliasedType = null;
        var constructed = Construct(action, [built.Type("Int32")]);

        Assert.Same(constructed, AliasTransparency.Expand(constructed, built.Interner));
    }

    [Fact]
    public void AConstructionOverATypeThatIsNoAliasIsLeftAsWritten()
    {
        var box = HandBuiltModel.Class("Box<>");
        box.TypeParametersList.Add(new TypeParameter("T", 0, box.Scope, box));
        var constructed = Construct(box, [built.Type("Int32")]);

        Assert.Same(constructed, AliasTransparency.Expand(constructed, built.Interner));
    }

    private TypeAlias Action()
    {
        var domain = built.Interner.GetOrCreateConstructedType(
            built.Type("Array<,>"), [built.Type("Type"), built.Interner.GetOrCreateUnknownValue(built.Type("Int32"))]);
        var action = new TypeAlias("Action<>", "Standard", null, null, new TypeAliasDeclarationNode());
        var pack = new TypeParameter("TArgs", 0, action.Scope, action) { PackKind = TypePackKind.Unbracketed, Domain = domain };
        action.TypeParametersList.Add(pack);
        action.AliasedType = new FunctionType([new SpreadType(pack)], built.Type("Void"));
        return action;
    }

    private ConstructedType Construct(IMistType definition, IReadOnlyList<IMistType> arguments)
    {
        return built.Interner.GetOrCreateConstructedType(definition, arguments);
    }
}