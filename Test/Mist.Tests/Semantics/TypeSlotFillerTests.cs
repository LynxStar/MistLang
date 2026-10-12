using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 1: TypeSlots over an abstract IBag declaring `type Element;` and `type Pair<T>;`, filled by types
// and aliases nested in hand-built conformers.
[Trait("Tier", "1")]
public sealed class TypeSlotFillerTests
{
    private readonly HandBuiltModel built = new();
    private readonly AbstractType bag = new("IBag", "Probe", new Scope(null), null, null);
    private readonly TypeSlot element;
    private readonly TypeSlot pair;

    public TypeSlotFillerTests()
    {
        element = new TypeSlot("Element", 0, bag.Scope, bag);
        pair = new TypeSlot("Pair<>", 1, bag.Scope, bag);
        pair.TypeParametersList.Add(new TypeParameter("T", 0, pair.Scope, pair));
        bag.TypeSlotsList.AddRange([element, pair]);
    }

    [Fact]
    public void ANestedAlias_FillsASlotOfNoTypeParametersWithWhatItStandsFor()
    {
        var conformer = Conformer("Strings");
        Nest(conformer, Alias("Element", built.Type("String")));

        Assert.Same(built.Type("String"), TypeSlots.Filler(element, conformer));
    }

    [Fact]
    public void ANestedGenericAlias_OfTheSlotsArity_FillsAParameterizedSlotWithItself()
    {
        var conformer = Conformer("Pairs");
        var alias = Alias("Pair<>", built.Type("String"), "T");
        Nest(conformer, alias);

        Assert.Same(alias, TypeSlots.Filler(pair, conformer));
    }

    [Theory]
    [InlineData("Pair", "")]
    [InlineData("Pair<,>", "T,U")]
    [InlineData("Pairs<>", "T")]
    public void ANestedAlias_OfAnotherArityOrName_DoesNotFillAParameterizedSlot(string name, string parameters)
    {
        var conformer = Conformer("Pairs");
        Nest(conformer, Alias(name, built.Type("String"), parameters.Split(',', StringSplitOptions.RemoveEmptyEntries)));

        Assert.Null(TypeSlots.Filler(pair, conformer));
    }

    [Fact]
    public void ANestedGenericAlias_DoesNotFillASlotOfNoTypeParameters()
    {
        var conformer = Conformer("Strings");
        Nest(conformer, Alias("Element<>", built.Type("String"), "T"));

        Assert.Null(TypeSlots.Filler(element, conformer));
    }

    [Fact]
    public void AnAliasOnABase_FillsTheSlotForTheDeriver()
    {
        var parent = Conformer("Base");
        Nest(parent, Alias("Element", built.Type("Int32")));
        var conformer = Conformer("Numbers");
        conformer.BaseType = parent;

        Assert.Same(built.Type("Int32"), TypeSlots.Filler(element, conformer));
    }

    [Fact]
    public void ANestedType_StillFillsTheSlotItself()
    {
        var conformer = Conformer("Boxes");
        var nested = new ClassType("Element", "Probe", new Scope(null), conformer, null);
        Nest(conformer, nested);

        Assert.Same(nested, TypeSlots.Filler(element, conformer));
    }

    [Fact]
    public void TheClosing_BindsEveryFilledSlotOfNoTypeParametersAndNothingElse()
    {
        var conformer = Conformer("Strings");
        Nest(conformer, Alias("Element", built.Type("String")));
        var unfilled = new TypeSlot("Missing", 2, bag.Scope, bag);
        bag.TypeSlotsList.Add(unfilled);

        var closing = TypeSlots.Closing(conformer, [bag]);

        Assert.Same(built.Type("String"), closing.Apply(element, interner: null));
        Assert.Same(unfilled, closing.Apply(unfilled, interner: null));
        Assert.Same(pair, closing.Apply(pair, interner: null));
    }

    private static ClassType Conformer(string name)
    {
        return HandBuiltModel.Class(name);
    }

    private static TypeAlias Alias(string name, MistType aliased, params string[] parameters)
    {
        var alias = new TypeAlias(name, "Probe", new Scope(null), null, null!) { AliasedType = aliased };

        foreach(var parameter in parameters)
        {
            alias.TypeParametersList.Add(new TypeParameter(parameter, alias.TypeParametersList.Count, alias.Scope, alias));
        }

        return alias;
    }

    private static void Nest(MistType owner, MistType nested)
    {
        owner.NestedTypes.Add(nested);
    }
}