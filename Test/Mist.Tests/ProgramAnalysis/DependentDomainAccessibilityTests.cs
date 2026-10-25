using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class DependentDomainAccessibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInaccessibleNamedDomainIsRefused(bool fromAnotherAssembly)
    {
        var f = new DeclarationFragments();
        var outer = f.Class("Outer");
        var domain = f.Alias("Small", HandBuiltModel.Refined(f.Built.Type("Int32")),
            fromAnotherAssembly ? null : outer,
            fromAnotherAssembly ? Accessibility.Internal : Accessibility.Private);

        if(fromAnotherAssembly)
        {
            domain.Assembly = f.Built.Model.AddReferenceAssembly("Measures", "/Measures/mist.toml");
        }

        var owner = f.Class("Buffer<>");
        var parameter = DeclarationFragments.Parameter(owner, owner.Scope, "N");
        parameter.IsDependent = true;
        parameter.Domain = domain;
        parameter.DomainTypeName = fromAnotherAssembly ? "Refusal.Small" : "Refusal.Outer.Small";
        var naming = new TypeNaming(domain, HandBuiltModel.Node(30), owner, MistAssembly.Synthetic);
        var refusal = AccessibilityAnalyzer.DecideNaming(naming);

        _ = Assert.NotNull(refusal);
        Assert.Equal("MIST0160", refusal.Value.Descriptor.Number.Value);
        Assert.Same(domain, refusal.Value.Blocking);
    }
}