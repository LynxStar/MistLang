// Tier 2 (Functional): accessor uses retain their direction, reporting site and enclosing construction context.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AccessorSiteTests
{
    [Theory]
    [InlineData("property-read-by-name-without-a-getter")]
    [InlineData("property-written-without-a-setter")]
    [InlineData("property-reassigned-from-itself-without-a-getter")]
    [InlineData("property-coalesced-without-a-setter")]
    [InlineData("private-setter-of-a-protected-property-written-in-a-derived-type")]
    [InlineData("index-read-without-a-getter")]
    [InlineData("index-coalesced-without-a-getter")]
    [InlineData("selected-indexer-without-a-setter")]
    [InlineData("initializer-entry-with-a-private-setter")]
    [InlineData("property-pattern-without-a-getter")]
    [InlineData("get-only-auto-property-of-a-base-written-in-a-derived-constructor")]
    [InlineData("get-only-auto-property-of-another-instance-written-in-a-constructor")]
    [InlineData("get-only-auto-property-written-in-a-lambda-in-its-constructor")]
    [InlineData("get-only-auto-property-written-in-a-local-function-in-its-constructor")]
    [InlineData("static-get-only-auto-property-written-in-an-instance-constructor")]
    [InlineData("init-only-property-of-another-instance-written-in-an-init-accessor")]
    [InlineData("init-only-property-written-in-a-get-accessor-beside-an-init-one")]
    [InlineData("init-only-property-written-in-a-set-accessor")]
    [InlineData("init-only-property-written-in-a-lambda-in-an-init-accessor")]
    [InlineData("init-only-property-written-in-a-lambda-in-its-constructor")]
    [InlineData("init-only-property-written-in-a-local-function-in-its-constructor")]
    [InlineData("init-only-property-written-in-a-method-of-its-type")]
    public void RefusalUsesTheAccessorsCallSiteAndConstructionFrame(string site)
    {
        var fragment = new AccessorFragments(site);
        var expected = Assert.Single(fragment.Decide());
        var diagnostic = Assert.Single(fragment.Analyze());

        Assert.Equal(expected.Number.Value, diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGetOnlyAutoPropertyCanBeAssignedByItsOwnMatchingConstructor(bool isStatic)
    {
        var model = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Counter");
        var integer = model.Type("Int32");
        var property = new Property("Count", integer, new MemberModifiers { IsStatic = isStatic })
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
        };
        var assigned = new AssignmentExpression(HandBuiltModel.Node(), new PropertyReferenceExpression(HandBuiltModel.Node(), property), HandBuiltModel.Literal(integer, 1L), integer);
        var body = HandBuiltModel.Block(new ExpressionStatement(assigned));
        var constructor = new Constructor("Counter", owner, [], new MemberModifiers { IsStatic = isStatic }) { Body = body, DeclaringType = owner };

        Assert.Empty(model.Analyze(new AccessorAnalyzer(), body, owner: owner, member: constructor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInitSetterCanBeCalledOnThisFromAConstructorOrAnInitAccessor(bool initAccessor)
    {
        var model = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Counter");
        var integer = model.Type("Int32");
        var property = new Property("Count", integer, default)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsInit = true },
        };
        var target = new MemberAccessExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), owner), "Count", integer,
            member: new PropertyResult(property, owner, []));
        var body = HandBuiltModel.Block(new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), target, HandBuiltModel.Literal(integer, 1L), integer)));
        object member = initAccessor
            ? new Property("Depth", integer, default) { DeclaringType = owner, Setter = new PropertyAccessor { IsInit = true, Body = body } }
            : new Constructor("Counter", owner, [], default) { Body = body, DeclaringType = owner };

        Assert.Empty(model.Analyze(new AccessorAnalyzer(), body, owner: owner, member: member));
    }
}