using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Args;

[Trait("Tier", "2")]
public sealed class DiagnosticControlTests
{
    private static readonly MistDiagnostic Info = DiagnosticFixtures.Descriptor("MIST9001", "info", DiagnosticSeverity.Info, policy: PolicyClass.Promotable);
    private static readonly MistDiagnostic Warning = DiagnosticFixtures.Descriptor("MIST9002", "warning", DiagnosticSeverity.Warning, policy: PolicyClass.Promotable);
    private static readonly MistDiagnostic Fixed = DiagnosticFixtures.Descriptor("MIST9003", "fixed");
    private static readonly MistDiagnostic Optional = DiagnosticFixtures.Descriptor("MIST9004", "optional", DiagnosticSeverity.Warning, policy: PolicyClass.Optional);

    [Fact]
    public void SixControlsAreSeparatedFromCommandArguments()
    {
        var parsed = DiagnosticControlParser.Parse(["run", "project", "--diagnostic-format", "json", "--diagnostic-file", "out.json",
            "--warnings-as-errors", "MIST9001,MIST9002", "--disable", "MIST9004", "--enable", "MIST9004", "--min-severity", "error", "--quiet"], Find);
        Assert.Null(parsed.Error);
        Assert.Equal(["run", "project", "--quiet"], parsed.Remaining.ToArray());
        Assert.Equal(DiagnosticFormat.Json, parsed.Controls.Format);
        Assert.Equal("out.json", parsed.Controls.File);
        Assert.Equal(DiagnosticSeverity.Error, parsed.Controls.Minimum);
        var policy = parsed.Controls.Policy!.Apply(DiagnosticPolicy.Default);
        Assert.True(policy.Promoted.SetEquals([Info.Number, Warning.Number]));
        Assert.Empty(policy.Disabled);
        Assert.Contains(Optional.Number, policy.Enabled);
    }

    [Theory]
    [InlineData("--diagnostic-format", "yaml")]
    [InlineData("--diagnostic-format", "")]
    [InlineData("--diagnostic-file", "")]
    [InlineData("--min-severity", "fatal")]
    [InlineData("--min-severity", "")]
    [InlineData("--disable", "")]
    [InlineData("--enable", "")]
    [InlineData("--disable", "MIST9999")]
    [InlineData("--enable", "mist9004")]
    [InlineData("--disable", "MIST9004,")]
    [InlineData("--enable", "MIST9001")]
    [InlineData("--disable", "MIST9002")]
    [InlineData("--disable", "MIST9003")]
    [InlineData("--enable", "MIST9003")]
    [InlineData("--warnings-as-errors", "MIST9003")]
    [InlineData("--warnings-as-errors", "MIST9999")]
    [InlineData("--warnings-as-errors", "bad")]
    public void BadValuesAndForbiddenSelectorsHaveOneControlError(string flag, string value)
    {
        var parsed = DiagnosticControlParser.Parse([flag, value], Find);
        Assert.NotNull(parsed.Error);
    }

    [Theory]
    [InlineData(CliCommand.Build)]
    [InlineData(CliCommand.Run)]
    [InlineData(CliCommand.Test)]
    [InlineData(CliCommand.Inspect)]
    [InlineData(CliCommand.Restore)]
    public void CompileCommandsAcceptAllControls(CliCommand command)
    {
        var parsed = DiagnosticControlParser.Parse(["--diagnostic-format", "json", "--diagnostic-file", "out.json", "--min-severity", "warning",
            "--warnings-as-errors", "--disable", "MIST9004", "--enable", "MIST9004"], Find);
        Assert.Null(DiagnosticControlParser.ValidateCommand(parsed, command, CompilerMode.Compile));
    }

    [Theory]
    [InlineData(CliCommand.Steering)]
    [InlineData(CliCommand.Version)]
    public void NonCompilationCommandsRejectEveryControl(CliCommand command)
    {
        foreach(var args in new[] { new[] { "--diagnostic-format", "json" }, ["--diagnostic-file", "out.json"], ["--min-severity", "info"],
            ["--warnings-as-errors"], ["--disable", "MIST9004"], ["--enable", "MIST9004"] })
        {
            Assert.Contains("Unknown", DiagnosticControlParser.ValidateCommand(DiagnosticControlParser.Parse(args, Find), command, CompilerMode.Compile));
        }
    }

    [Fact]
    public void LexAcceptsOutputControlsAndRejectsPolicyControls()
    {
        var parsed = DiagnosticControlParser.Parse(["--diagnostic-format", "json", "--diagnostic-file", "out.json", "--min-severity", "error"], Find);
        Assert.Null(DiagnosticControlParser.ValidateCommand(parsed, CliCommand.Build, CompilerMode.Lex));
        foreach(var args in new[] { new[] { "--warnings-as-errors" }, ["--disable", "MIST9004"], ["--enable", "MIST9004"] })
        {
            Assert.Contains("Unknown", DiagnosticControlParser.ValidateCommand(DiagnosticControlParser.Parse(args, Find), CliCommand.Build, CompilerMode.Lex));
        }
    }

    [Fact]
    public void BarePromotionIncludesInfoAndWarningWhileFixedStaysFixed()
    {
        var parsed = DiagnosticControlParser.Parse(["--warnings-as-errors"], Find);
        var policy = parsed.Controls.Policy!.Apply(DiagnosticPolicy.Default);
        Assert.Equal(DiagnosticSeverity.Error, SeverityPolicy.Apply(Info, policy).Effective);
        Assert.Equal(DiagnosticSeverity.Error, SeverityPolicy.Apply(Warning, policy).Effective);
        Assert.Equal(Fixed.DefaultSeverity, SeverityPolicy.Apply(Fixed, policy).Effective);
        Assert.Contains("command line", SeverityPolicy.Apply(Info, policy).Reason);
    }

    [Fact]
    public void ListedPolicyReplacesManifestPromotionAndSwitchesOverrideByNumber()
    {
        var manifest = DiagnosticPolicy.Default with { PromoteAllWarnings = true, Disabled = [Optional.Number] };
        var parsed = DiagnosticControlParser.Parse(["--warnings-as-errors", "MIST9001", "--enable", "MIST9004"], Find);
        var policy = parsed.Controls.Policy!.Apply(manifest);
        Assert.Equal(DiagnosticSeverity.Error, SeverityPolicy.Apply(Info, policy).Effective);
        Assert.Equal(DiagnosticSeverity.Warning, SeverityPolicy.Apply(Warning, policy).Effective);
        Assert.True(policy.IsEnabled(Optional));
        var disabled = DiagnosticControlParser.Parse(["--disable", "MIST9004"], Find).Controls.Policy!.Apply(policy);
        Assert.False(disabled.IsEnabled(Optional));
    }

    private static MistDiagnostic? Find(string number)
    {
        return new[] { Info, Warning, Fixed, Optional }.FirstOrDefault(d => d.Number.Value == number);
    }
}