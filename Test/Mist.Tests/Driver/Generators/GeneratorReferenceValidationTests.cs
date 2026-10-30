using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "1")]
public sealed class GeneratorReferenceValidationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AProjectCanOnlyBeUsedUnderItsDeclaredKind(bool ordinaryEdge)
    {
        var target = new ProjectConfiguration("Target", "Target", [], ProjectType: ordinaryEdge ? MistProjectType.SourceGenerator : MistProjectType.Regular);
        var consumer = new ProjectConfiguration("Consumer", "Consumer",
            ordinaryEdge ? [new ProjectReference("Target", "/Target/mist.toml")] : [],
            Generators: ordinaryEdge ? [] : [new GeneratorReference("Target", "/Target/mist.toml", [], [], TimeSpan.FromSeconds(1))]);
        var diagnostics = new DiagnosticBag();
        GeneratorReferenceValidation.Validate(new GeneratorNode("/Consumer/mist.toml", consumer),
            new Dictionary<string, ProjectConfiguration> { ["/Target/mist.toml"] = target }, diagnostics);
        var diagnostic = Assert.Single(diagnostics.Admitted);
        Assert.Same(DriverDiagnostics.GeneratorReferenceInvalid, diagnostic.Descriptor);
        Assert.Equal(new ProjectLocation("/Consumer/mist.toml", null), diagnostic.Location);
    }
}