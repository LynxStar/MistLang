using Mist.Common.Diagnostics;
using Mist.Driver;
using Tomlyn;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class DiagnosticsPolicyTableTests
{
    private const string Manifest = "/probe/mist.toml";

    [Fact]
    public void AnAbsentTableIsTheDefaultPolicy()
    {
        var bag = new DiagnosticBag();

        var policy = ProjectFileParser.ReadDiagnosticsPolicy(Toml.ToModel("name = \"Probe\""), Manifest, bag);

        Assert.Same(DiagnosticPolicy.Default, policy);
        Assert.Empty(bag.Admitted);
    }

    [Fact]
    public void TheThreeKeysAreRead()
    {
        var optional = ProgramAnalysisDiagnostics.SwitchNotExhaustive.Number.Value;
        var promotable = DriverDiagnostics.GlobalUsingDuplicateNamespace.Number.Value;
        var bag = new DiagnosticBag();

        var policy = ProjectFileParser.ReadDiagnosticsPolicy(
            Toml.ToModel($"[diagnostics]\nwarnings_as_errors = [\"{promotable}\", \"{optional}\"]\ndisable = [\"{optional}\"]\nenable = []"), Manifest, bag);

        Assert.NotNull(policy);
        Assert.Empty(bag.Admitted);
        Assert.False(policy.PromoteAllWarnings);
        Assert.Equal(2, policy.Promoted.Count);
        Assert.Contains(ProgramAnalysisDiagnostics.SwitchNotExhaustive.Number, policy.Disabled);
        Assert.Empty(policy.Enabled);
        Assert.False(policy.IsEnabled(ProgramAnalysisDiagnostics.SwitchNotExhaustive));
    }

    [Fact]
    public void WarningsAsErrorsAcceptsABoolean()
    {
        var policy = ProjectFileParser.ReadDiagnosticsPolicy(Toml.ToModel("[diagnostics]\nwarnings_as_errors = true"), Manifest, new DiagnosticBag());

        Assert.True(policy!.PromoteAllWarnings);
    }

    [Theory]
    [InlineData("diagnostics = 3", "DiagnosticsTableExpected")]
    [InlineData("[diagnostics]\nwarnings_as_erors = true", "DiagnosticsKeyUnknown")]
    [InlineData("[diagnostics]\nwarnings_as_errors = \"yes\"", "DiagnosticsValueInvalid")]
    [InlineData("[diagnostics]\ndisable = \"MIST_PA_SWITCH_NOT_EXHAUSTIVE\"", "DiagnosticsValueInvalid")]
    [InlineData("[diagnostics]\ndisable = [7]", "DiagnosticsValueInvalid")]
    [InlineData("[diagnostics]\ndisable = [\"MIST9999\"]", "DiagnosticsNumberUnknown")]
    [InlineData("[diagnostics]\nwarnings_as_errors = [\"MIST_NOWHERE\"]", "DiagnosticsNumberUnknown")]
    public void AnInvalidEntryProducesOneConfigurationOccurrenceAndFailsTheManifest(string toml, string field)
    {
        var bag = new DiagnosticBag();

        var policy = ProjectFileParser.ReadDiagnosticsPolicy(Toml.ToModel(toml), Manifest, bag);

        Assert.Null(policy);
        var occurrence = Assert.Single(bag.Admitted);
        Assert.Equal(field, DiagnosticRegistry.Index.Entries.Single(entry => entry.Descriptor == occurrence.Descriptor).FieldName);
        Assert.Equal(DiagnosticCategory.Cfg, occurrence.Descriptor.Category);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Equal(new FileOnly(Manifest), occurrence.Location);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("enable")]
    public void SwitchingAFixedOrPromotableNumberFailsWithOneConfigurationOccurrence(string key)
    {
        var fixedNumber = DriverDiagnostics.ProjectFileNotFound.Number.Value;
        var promotableNumber = DriverDiagnostics.GlobalUsingDuplicateNamespace.Number.Value;
        var bag = new DiagnosticBag();

        var policy = ProjectFileParser.ReadDiagnosticsPolicy(Toml.ToModel($"[diagnostics]\n{key} = [\"{fixedNumber}\", \"{promotableNumber}\"]"), Manifest, bag);

        Assert.Null(policy);
        Assert.Equal(2, bag.Admitted.Count);
        Assert.All(bag.Admitted, occurrence => Assert.Same(DriverDiagnostics.DiagnosticsNumberNotOptional, occurrence.Descriptor));
        Assert.Contains("is a Fixed diagnostic", bag.Admitted[0].Message);
        Assert.Contains("is a Promotable diagnostic", bag.Admitted[1].Message);
    }

    [Fact]
    public void PromotingAnErrorDefaultIsAcceptedAndChangesNothing()
    {
        var bag = new DiagnosticBag();

        var policy = ProjectFileParser.ReadDiagnosticsPolicy(
            Toml.ToModel($"[diagnostics]\nwarnings_as_errors = [\"{DriverDiagnostics.ProjectFileNotFound.Number.Value}\"]"), Manifest, bag);

        Assert.NotNull(policy);
        Assert.Equal((DiagnosticSeverity.Error, null), SeverityPolicy.Apply(DriverDiagnostics.ProjectFileNotFound, policy));
    }
}