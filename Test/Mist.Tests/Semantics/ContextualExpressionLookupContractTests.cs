using Mist.Common.Source;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ContextualExpressionLookupContractTests
{
    [Fact]
    public void AmbiguousInheritedPropertiesReportTheirCandidatesAtTheBareName()
    {
        var f = new BindingFragments();
        var first = Abstract("First");
        var second = Abstract("Second");
        _ = AddProperty(first, f);
        _ = AddProperty(second, f);
        var owner = Abstract("Combined");
        owner.BaseAbstractTypes.Add(first);
        owner.BaseAbstractTypes.Add(second);
        var ctx = new BindingContext(new BodyScope(owner.Scope), null);
        var name = BindingFragments.Identifier("Value");

        var bound = Assert.IsType<ErrorExpression>(IdentifierBinder.BindIdentifier(name, ctx, f.Builder));

        Assert.True(bound.IsReported);
        Assert.True(PriorFailure.Of(bound));
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_AMBIGUOUS_IDENTIFIER", diagnostic.Id.Value);
        Assert.Contains("property 'Probe.First.Value'", diagnostic.Message);
        Assert.Contains("property 'Probe.Second.Value'", diagnostic.Message);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 31), new LinePosition(1, 32)), diagnostic.Location);
    }

    [Fact]
    public void ASingleInheritedPropertyStillResolves()
    {
        var f = new BindingFragments();
        var parent = Abstract("Parent");
        var property = AddProperty(parent, f);
        var owner = Abstract("Child");
        owner.BaseAbstractTypes.Add(parent);

        var bound = IdentifierBinder.BindIdentifier(BindingFragments.Identifier("Value"), new BindingContext(new BodyScope(owner.Scope), null), f.Builder);

        Assert.Same(property, Assert.IsType<PropertyReferenceExpression>(bound).Property);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void RepeatedMethodNamesRemainOneOverloadGroup()
    {
        var f = new BindingFragments();
        var parent = Abstract("Parent");
        var first = BindingFragments.Member(parent, "Read", f.Built.Type("Int32"), f.Built.Type("Int32"));
        var owner = Abstract("Child");
        owner.BaseAbstractTypes.Add(parent);
        var second = BindingFragments.Member(owner, "Read", f.Built.Type("Int32"), f.Built.Type("String"));

        var group = Assert.IsType<MethodGroupExpression>(IdentifierBinder.BindIdentifier(
            BindingFragments.Identifier("Read"), new BindingContext(new BodyScope(owner.Scope), null), f.Builder));

        Assert.Equal(2, group.Candidates.Count);
        Assert.Contains(first, group.Candidates);
        Assert.Contains(second, group.Candidates);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ImportedTypeAmbiguityKeepsItsExistingSingleOwner()
    {
        var f = new BindingFragments();
        var assembly = f.Built.Model.AddSelfAssembly("Probe");
        foreach(var ns in new[] { "First", "Second" })
        {
            var type = new ClassType("Widget", ns, new Scope(null), null, null) { Assembly = assembly };
            _ = f.Built.Model.RegisterType(type);
            f.Types.DeclareImport(ns);
        }

        var bound = Assert.IsType<TypeReferenceExpression>(IdentifierBinder.BindIdentifier(BindingFragments.Identifier("Widget"), f.Binding, f.Builder));

        _ = Assert.IsType<ErrorType>(bound.ReferencedType);
        Assert.Equal("MIST_PM_AMBIGUOUS_TYPE", Assert.Single(f.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Fact]
    public void AnUnrepresentedBareEnumVariantRemainsUnfinished()
    {
        var f = new BindingFragments();
        var type = HandBuiltModel.Enum("Colour", f.Built.Type("Int32"));
        type.MembersList.Add(new EnumMember("Red", false, null));

        var bound = IdentifierBinder.BindIdentifier(BindingFragments.Identifier("Red"), new BindingContext(new BodyScope(type.Scope), null), f.Builder);

        _ = Assert.IsType<UnresolvedExpression>(bound);
        Assert.False(PriorFailure.Of(bound));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static AbstractType Abstract(string name)
    {
        return new AbstractType(name, "Probe", new Scope(null), null, null);
    }

    private static Property AddProperty(AbstractType owner, BindingFragments f)
    {
        var property = new Property("Value", f.Built.Type("Int32"), DeclarationFragments.Public) { DeclaringType = owner };
        owner.PropertiesList.Add(property);
        return property;
    }
}