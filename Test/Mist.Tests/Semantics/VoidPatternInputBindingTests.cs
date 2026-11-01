using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class VoidPatternInputBindingTests
{
    [Theory]
    [InlineData("unit")]
    [InlineData("optional")]
    [InlineData("unknown")]
    [InlineData("carrier")]
    public void ATypeTest_RetainsTheScrutineeTypeAlongsideAbsenceAndCarrierDecisions(string representation)
    {
        var f = new BindingFragments();
        var unit = f.Built.Type("Void");
        IMistType input = representation switch
        {
            "unit" => unit,
            "optional" => new UnionType([f.Built.Type("Int32"), unit]),
            "carrier" => new UnionType([f.Built.Type("Int32"), f.Built.Type("String"), unit]),
            _ => UnknownType.Instance,
        };
        var primary = BindingFragments.At<BareTypePatternPrimaryPatternNode>();
        primary.Type = BindingFragments.TypeName("Void");
        var syntax = new PatternNode
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };
        var bound = Assert.IsType<TypeTestPattern>(PatternBinder.Bind(syntax, input, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(input, bound.ScrutineeType);
        Assert.Equal(representation == "optional", bound.TestsAbsence);
        Assert.Same(representation == "carrier" ? unit : null, bound.Constituent);
    }
}