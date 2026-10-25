using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: dependent closings the registry holds -- a construction, a forwarding, an alias's and a type
// named only for a static -- judged against their slot's domain by the analyzer, each at its own site.
[Trait("Tier", "2")]
public sealed class DependentClosingSiteTests
{
    [Fact]
    public void AValueOutsideItsSlotsDomainIsRefusedAtTheClosing()
    {
        var fragment = new DeclarationFragments();
        var box = Box(fragment, "Byte");
        fragment.Built.Model.ConstructorConstraintClosures.RecordType(box, [Lifted(fragment, 256L)], HandBuiltModel.Node(55));

        var finding = Assert.Single(fragment.Run(new RefinementAnalyzer()));
        Assert.Equal("MIST_DT_0003", finding.Number.Value);
        Assert.Equal(56, finding.Location.Start.Column);
        Assert.Contains("'256' of type 'int'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWideningForwardIsAdmittedAndANarrowingOneRefusedAtItsSite()
    {
        var fragment = new DeclarationFragments();
        var wide = Box(fragment, "Int64");
        var narrow = Box(fragment, "Int32", "Narrow");
        var derived = fragment.Class("Derived");
        var forwarded = DeclarationFragments.Parameter(derived, derived.Scope, "M");
        forwarded.IsDependent = true;
        forwarded.Domain = fragment.Built.Type("Int32");
        var reverse = fragment.Class("Reverse");
        var narrowed = DeclarationFragments.Parameter(reverse, reverse.Scope, "L");
        narrowed.IsDependent = true;
        narrowed.Domain = fragment.Built.Type("Int64");
        var registry = fragment.Built.Model.ConstructorConstraintClosures;
        registry.RecordType(wide, [forwarded], HandBuiltModel.Node(40));
        registry.RecordType(narrow, [narrowed], HandBuiltModel.Node(70));

        var finding = Assert.Single(fragment.Run(new RefinementAnalyzer()));
        Assert.Equal("MIST_DT_0004", finding.Number.Value);
        Assert.Equal(71, finding.Location.Start.Column);
        Assert.Contains("'L' of type 'long'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("parameter 'N' of type 'int'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAliasAndATypeNamedForAStaticAreJudgedLikeAConstruction()
    {
        var fragment = new DeclarationFragments();
        var box = Box(fragment, "Int32");
        var alias = fragment.Alias("Pick", fragment.Built.Type("Int32"));
        var picked = DeclarationFragments.Parameter(alias, alias.Scope, "V");
        picked.IsDependent = true;
        picked.Domain = fragment.Built.Type("Char");
        var registry = fragment.Built.Model.ConstructorConstraintClosures;
        registry.RecordStampableType(box, [new LiftedType(fragment.Built.Type("Boolean"), true)], HandBuiltModel.Node(30));
        registry.RecordType(alias, [Lifted(fragment, 65L)], HandBuiltModel.Node(60));

        Assert.Equal([31, 61], fragment.Run(new RefinementAnalyzer()).Select(finding => finding.Location.Start.Column).Order());
    }

    private static ClassType Box(DeclarationFragments fragment, string domain, string name = "Box")
    {
        var box = fragment.Class(name);
        var parameter = DeclarationFragments.Parameter(box, box.Scope, "N");
        parameter.IsDependent = true;
        parameter.Domain = fragment.Built.Type(domain);
        return box;
    }

    private static LiftedType Lifted(DeclarationFragments fragment, object value)
    {
        return new LiftedType(fragment.Built.Type("Int32"), value);
    }
}