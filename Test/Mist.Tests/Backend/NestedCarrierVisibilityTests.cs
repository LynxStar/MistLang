using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class NestedCarrierVisibilityTests
{
    [Theory]
    [InlineData(Accessibility.Internal, false)]
    [InlineData(Accessibility.Public, true)]
    public void PublicNestedEnumFollowsItsEnclosingTypeVisibility(Accessibility enclosingAccessibility, bool expectedPublic)
    {
        var enclosing = new ClassType("Container", "Probe", parentScope: null, declaringType: null, declarationNode: null)
        {
            Modifiers = new() { Accessibility = enclosingAccessibility },
        };
        var payload = new EnumType("Mode", "Probe", enclosing.Scope, enclosing, declarationNode: null)
        {
            Modifiers = new() { Accessibility = Accessibility.Public },
        };

        Assert.Equal(expectedPublic, CarrierVisibility.IsPublic(payload));
    }
}