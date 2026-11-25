using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ConformancePropertySlotTests
{
    private readonly BindingFragments f = new();
    private readonly ClassType box = HandBuiltModel.Class("Box");
    private readonly AbstractType sized;

    public ConformancePropertySlotTests()
    {
        sized = new AbstractType("ISized", "Probe", f.Types, null, null);
        f.Register(box);
        f.Register(sized);
    }

    [Fact]
    public void APropertySlotOfAPlainAbstractIsAnsweredByAPropertyOfItsType()
    {
        var slot = Property(sized, "Size", f.Built.Type("Int32"));
        var count = Property(box, "Count", f.Built.Type("Int32"));

        var conformance = Conform(("Count", "Size"));

        var mapping = Assert.IsType<PropertyMapping>(Assert.Single(conformance.Mappings));
        Assert.Same(count, mapping.Source);
        Assert.Equal("Size", mapping.Target.Name);
        Assert.Same(slot.Type, mapping.Target.Type);
        Assert.Same(slot.Getter, mapping.Target.Getter);
        Assert.Same(sized, mapping.Declarer);
        Assert.Empty(conformance.UnresolvedTargets);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void APropertyOfAnotherTypeLeavesThePropertySlotUnresolved()
    {
        _ = Property(sized, "Label", f.Built.Type("String"));
        _ = Property(box, "Weight", f.Built.Type("Int32"));

        var conformance = Conform(("Weight", "Label"));

        Assert.Empty(conformance.Mappings);
        Assert.Equal(["Label"], conformance.UnresolvedTargets);
        Assert.Equal("MIST0402", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    private static Property Property(MemberType owner, string name, IMistType type)
    {
        var property = new Property(name, type, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor(),
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    private ConformanceDeclaration Conform(params (string Source, string Target)[] mappings)
    {
        var declaration = BindingFragments.At<WithDeclarationNode>();
        declaration.TypeName = BindingFragments.Token("Box");
        declaration.AbstractName = BindingFragments.Token("ISized");
        declaration.Mappings = [.. mappings.Select(mapping => new MemberMappingNode
        {
            SourceName = BindingFragments.Token(mapping.Source),
            TargetName = BindingFragments.Token(mapping.Target),
        })];
        f.Builder.PendingConformances.Add(new PendingConformance(declaration, f.Types, "Probe"));

        new ConformanceBuilder(f.Builder, f.Builder.Resolver).BuildAll();

        return Assert.Single(box.ConformancesList);
    }
}