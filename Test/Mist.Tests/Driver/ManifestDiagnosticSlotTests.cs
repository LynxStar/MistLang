using Mist.Common.Diagnostics;
using Mist.Driver;
using Tomlyn.Model;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class ManifestDiagnosticSlotTests
{
    [Fact]
    public void ARejectedSelectorPreservesItsTypedFacts()
    {
        const string manifest = "/probe/mist.toml";
        var number = DriverDiagnostics.ProjectFileNotFound.Number.Value;
        var model = new TomlTable
        {
            ["diagnostics"] = new TomlTable { ["disable"] = new TomlArray { number } },
        };
        var bag = new DiagnosticBag();

        var policy = ProjectFileParser.ReadDiagnosticsPolicy(model, manifest, bag);

        Assert.Null(policy);
        var occurrence = Assert.Single(bag.Admitted);
        Assert.Same(DriverDiagnostics.DiagnosticsNumberNotOptional, occurrence.Descriptor);
        Assert.Equal(new FileOnly(manifest), occurrence.Location);
        Assert.Equal<SlotValue>(
            [new NameValue(number), new NameValue("disable"), new PathValue(manifest), new EnumValue("Fixed")],
            occurrence.Arguments);
        Assert.Equal(
            $"'{number}' in 'disable' of '[diagnostics]' in '{manifest}' is a Fixed diagnostic, which cannot be disabled or enabled; only an Optional rule can be switched.",
            occurrence.Message);
    }
}