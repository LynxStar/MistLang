// Tier 1 (Unit): the type a declaration's own body gives a value of itself, read off hand-built
// definitions by SelfConstruction.Of. It has to be the interned construction the body's own type
// references reach, a pack has to fill its slot as the spread the body writes, and a nested definition
// is constructed over its own parameters with no enclosing instantiation, since that is how its own body
// names it.

using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class SelfConstructionTests
{
    private readonly TypeInterner interner = new();

    [Fact]
    public void ANonGenericDefinitionIsItself()
    {
        var plain = HandBuiltModel.Class("Plain");

        Assert.Same(plain, SelfConstruction.Of(plain, interner));
    }

    [Fact]
    public void ANonGenericDefinitionNestedInAGenericIsItself()
    {
        var outer = Generic("Outer<>", "T");
        var plain = new ClassType("Plain", "Probe", outer.Scope, outer, null);

        Assert.Same(plain, SelfConstruction.Of(plain, interner));
    }

    [Fact]
    public void AGenericDefinitionIsTheInternedConstructionOverItsOwnParameters()
    {
        var tag = Generic("Tag<>", "T");

        var self = Assert.IsType<ConstructedType>(SelfConstruction.Of(tag, interner));

        Assert.Same(interner.GetOrCreateConstructedType(tag, [tag.TypeParametersList[0]]), self);
        Assert.Equal("Probe.Tag<T>", self.FullyQualifiedName);
        Assert.Null(self.Enclosing);
    }

    [Fact]
    public void AGenericStructIsConstructedOverItsStructDefinition()
    {
        var pair = new StructType("Pair<>", "Probe", new Scope(null), null, null);
        pair.TypeParametersList.Add(new TypeParameter("T", 0, pair.Scope, pair));

        var self = Assert.IsType<ConstructedType>(SelfConstruction.Of(pair, interner));

        Assert.Same(pair, self.Definition);
    }

    [Theory]
    [InlineData(TypePackKind.Unbracketed)]
    [InlineData(TypePackKind.Bracketed)]
    public void APackFillsItsSlotAsTheSpreadOfItself(TypePackKind kind)
    {
        var bundle = HandBuiltModel.Class("Bundle<>");
        var domain = HandBuiltModel.Class("TypeArray");
        var pack = new TypeParameter("Ts", 0, bundle.Scope, bundle) { PackKind = kind, Domain = domain };
        bundle.TypeParametersList.Add(pack);

        var self = Assert.IsType<ConstructedType>(SelfConstruction.Of(bundle, interner));

        var slot = Assert.IsType<LiftedType>(Assert.Single(self.TypeArguments));
        Assert.Same(interner.LiftPack(domain, new TypePackValue([new SpreadType(pack)])), slot);
        Assert.Equal("Probe.Bundle<[Ts...]>", self.FullyQualifiedName);
    }

    [Fact]
    public void AFixedParameterBesideAPackKeepsItsOwnSlot()
    {
        var both = HandBuiltModel.Class("Both<>");
        var lead = new TypeParameter("T", 0, both.Scope, both);
        var pack = new TypeParameter("Ts", 1, both.Scope, both)
        {
            PackKind = TypePackKind.Unbracketed,
            Domain = HandBuiltModel.Class("TypeArray"),
        };
        both.TypeParametersList.AddRange([lead, pack]);

        var self = Assert.IsType<ConstructedType>(SelfConstruction.Of(both, interner));

        Assert.Equal(2, self.TypeArguments.Count);
        Assert.Same(lead, self.TypeArguments[0]);
        Assert.Equal("Probe.Both<T, [Ts...]>", self.FullyQualifiedName);
    }

    [Fact]
    public void APackWithNoResolvedDomainStillLiftsItsSpread()
    {
        var bundle = HandBuiltModel.Class("Bundle<>");
        var pack = new TypeParameter("Ts", 0, bundle.Scope, bundle) { PackKind = TypePackKind.Unbracketed };
        bundle.TypeParametersList.Add(pack);

        var self = Assert.IsType<ConstructedType>(SelfConstruction.Of(bundle, interner));

        Assert.Same(interner.LiftPack(pack, new TypePackValue([new SpreadType(pack)])), Assert.Single(self.TypeArguments));
    }

    [Fact]
    public void ANestedGenericIsConstructedOverItsOwnParameterWithNoEnclosingInstantiation()
    {
        var outer = Generic("Outer<>", "T");
        var inner = new ClassType("Inner<>", "Probe", outer.Scope, outer, null);
        var own = new TypeParameter("T", 0, inner.Scope, inner);
        inner.TypeParametersList.Add(own);

        var self = Assert.IsType<ConstructedType>(SelfConstruction.Of(inner, interner));

        Assert.Same(inner, self.Definition);
        Assert.Same(own, Assert.Single(self.TypeArguments));
        Assert.NotSame(outer.TypeParametersList[0], self.TypeArguments[0]);
        Assert.Null(self.Enclosing);
    }

    private static ClassType Generic(string name, string parameterName)
    {
        var type = HandBuiltModel.Class(name);
        type.TypeParametersList.Add(new TypeParameter(parameterName, 0, type.Scope, type));
        return type;
    }
}