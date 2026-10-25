using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ContextualExpressionStorageContractTests
{
    [Theory]
    [InlineData("GlobalField", typeof(FieldResult), "MIST0350")]
    [InlineData("GlobalProperty", typeof(PropertyResult), "MIST0350")]
    [InlineData("GlobalMethod", typeof(MethodGroupResult), "MIST0349")]
    [InlineData("Method", typeof(MethodGroupResult), "MIST0349")]
    [InlineData("Missing", typeof(NotFoundResult), "MIST0301")]
    public void APropertySubpatternDistinguishesUnusableMembersFromMissingMembers(string name, Type member, string diagnosticId)
    {
        var f = new BindingFragments();
        var owner = Widget(f);

        var bound = Bind(f, owner, name);

        Assert.True(bound.IsFailed);
        var subpattern = Assert.Single(bound.Subpatterns);
        Assert.IsType(member, subpattern.Member);
        _ = Assert.IsType<ErrorType>(subpattern.MemberType);
        Assert.True(subpattern.IsMemberFailed);
        Assert.True(subpattern.Pattern.IsFailed);
        Assert.True(f.Scope.Locals["kept"].IsFailed);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 46), new LinePosition(1, 47)), diagnostic.Position());
    }

    [Fact]
    public void AnEnumVariantIsNotPropertyPatternStorage()
    {
        var f = new BindingFragments();
        var colour = HandBuiltModel.Enum("Colour", f.Built.Type("Int32"));
        colour.MembersList.Add(new EnumMember("Red", false, null));

        var bound = Bind(f, colour, "Red");

        Assert.True(bound.IsFailed);
        _ = Assert.IsType<EnumMemberResult>(Assert.Single(bound.Subpatterns).Member);
        Assert.Equal("MIST0349", Assert.Single(f.Diagnostics.ToImmutableArray()).Number.Value);
    }

    [Theory]
    [InlineData("Field", typeof(FieldResult))]
    [InlineData("Property", typeof(PropertyResult))]
    [InlineData("Private", typeof(FieldResult))]
    public void InstanceStorageRetainsItsMemberAndDefersAccessibility(string name, Type member)
    {
        var f = new BindingFragments();

        var bound = Bind(f, Widget(f), name);

        Assert.False(bound.IsFailed);
        var subpattern = Assert.Single(bound.Subpatterns);
        Assert.IsType(member, subpattern.Member);
        Assert.Same(f.Built.Type("Int32"), subpattern.MemberType);
        Assert.False(subpattern.IsMemberFailed);
        Assert.False(f.Scope.Locals["kept"].IsFailed);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static PropertyPattern Bind(BindingFragments f, MistType owner, string name)
    {
        var capture = BindingFragments.At<VarPatternPrimaryPatternNode>();
        capture.Name = BindingFragments.Token("kept");
        var subpattern = BindingFragments.At<PropertySubpatternNode>(45);
        subpattern.Name = BindingFragments.Token(name);
        subpattern.Pattern = Pattern(capture);
        var property = BindingFragments.At<PropertyPatternPrimaryPatternNode>();
        property.Subpatterns = new PropertySubpatternListNode { PropertySubpattern = subpattern };
        return Assert.IsType<PropertyPattern>(PatternBinder.Bind(Pattern(property), owner, f.Binding, f.Builder));
    }

    private static PatternNode Pattern(PrimaryPatternNode primary)
    {
        return new PatternNode
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
    }

    private static ClassType Widget(BindingFragments f)
    {
        var owner = HandBuiltModel.Class("Widget");
        var integer = f.Built.Type("Int32");
        owner.FieldsList.Add(new Field("Field", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = owner });
        owner.PropertiesList.Add(new Property("Property", integer, DeclarationFragments.Public) { DeclaringType = owner });
        owner.FieldsList.Add(new Field("Private", integer, OwnershipKind.Unrestricted,
            DeclarationFragments.Public with { Accessibility = Accessibility.Private, IsReadonly = true })
        { DeclaringType = owner });
        var modifiers = DeclarationFragments.Public with { IsStatic = true };
        owner.FieldsList.Add(new Field("GlobalField", integer, OwnershipKind.Unrestricted, modifiers) { DeclaringType = owner });
        owner.PropertiesList.Add(new Property("GlobalProperty", integer, modifiers) { DeclaringType = owner });
        owner.MethodsList.Add(new Method("GlobalMethod", integer, [], modifiers) { DeclaringType = owner });
        _ = BindingFragments.Member(owner, "Method", integer);
        return owner;
    }
}