using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ClosedEnclosingCarrierVisibilityTests
{
    [Theory]
    [InlineData(Accessibility.Internal, false)]
    [InlineData(Accessibility.Public, true)]
    public void UnionCarrierFollowsTheClosedEnclosingArgumentVisibility(Accessibility enclosingArgumentAccessibility, bool expectedPublic)
    {
        var outer = new ClassType("Outer<>", "Probe", parentScope: null, declaringType: null, declarationNode: null)
        {
            Modifiers = new() { Accessibility = Accessibility.Public },
        };
        outer.TypeParametersList.Add(new TypeParameter("T", 0, outer.Scope, outer));

        var nested = new ClassType("Nested<>", "Probe", outer.Scope, outer, declarationNode: null)
        {
            Modifiers = new() { Accessibility = Accessibility.Public },
        };
        nested.TypeParametersList.Add(new TypeParameter("U", 0, nested.Scope, nested));

        var enclosingArgument = new ClassType("EnclosingArgument", "Probe", parentScope: null, declaringType: null, declarationNode: null)
        {
            Modifiers = new() { Accessibility = enclosingArgumentAccessibility },
        };
        var publicArgument = new ClassType("PublicArgument", "Probe", parentScope: null, declaringType: null, declarationNode: null)
        {
            Modifiers = new() { Accessibility = Accessibility.Public },
        };

        var enclosing = new ConstructedType(outer, [enclosingArgument]);
        var payload = new ConstructedType(nested, [publicArgument], enclosing);
        var union = new UnionType([payload, publicArgument]);

        Assert.Equal(expectedPublic, CarrierVisibility.IsPublic(union));
    }
}