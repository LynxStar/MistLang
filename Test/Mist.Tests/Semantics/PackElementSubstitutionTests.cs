using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 1: TypeSubstitution.Apply over `TArgs[index]`, with TArgs closed by the elements a case names.
// `Two...` spreads a pack declared Type[2] and `Open...` one of no fixed size.
[Trait("Tier", "1")]
public sealed class PackElementSubstitutionTests
{
    private readonly HandBuiltModel built = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 3 };
    private readonly TypeParameter two = new("Two", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };
    private readonly TypeParameter open = new("Open", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Theory]
    [InlineData("Int32, String, Boolean", 0, "Int32")]
    [InlineData("Int32, String, Boolean", 2, "Boolean")]
    [InlineData("Two..., Boolean", 0, "Two[0]")]
    [InlineData("Two..., Boolean", 1, "Two[1]")]
    [InlineData("Two..., Boolean", 2, "Boolean")]
    [InlineData("Int32, Two...", 2, "Two[1]")]
    [InlineData("Int32, Open...", 0, "Int32")]
    [InlineData("Int32, Open...", 1, "TArgs[1]")]
    [InlineData("Open..., Int32", 0, "TArgs[0]")]
    [InlineData("Int32", 2, "TArgs[2]")]
    public void AnIndexedElement_IsWhatTheClosingHoldsAtThatIndex(string closing, int index, string expected)
    {
        var substitution = TypeSubstitution.ForParameters(
            [pack], [new LiftedType(built.Type("Type"), new TypePackValue([.. closing.Split(", ").Select(Named)]))]);

        Assert.Equal(expected, substitution.Apply(pack.ElementAt(index), interner: null).Name);
    }

    [Fact]
    public void AnElementReachedThroughASpread_IsTheSpreadPacksOwnElement()
    {
        var substitution = TypeSubstitution.ForParameters(
            [pack], [new LiftedType(built.Type("Type"), new TypePackValue([built.Type("Int32"), new SpreadType(two)]))]);

        Assert.Same(two.ElementAt(0), substitution.Apply(pack.ElementAt(1), interner: null));
    }

    [Fact]
    public void AnUnclosedPack_LeavesItsElementAsItIs()
    {
        Assert.Same(pack.ElementAt(1), TypeSubstitution.ForParameters([two], [built.Type("Int32")]).Apply(pack.ElementAt(1), interner: null));
    }

    private IMistType Named(string name)
    {
        return name switch
        {
            "Two..." => new SpreadType(two),
            "Open..." => new SpreadType(open),
            _ => built.Type(name),
        };
    }
}