using System.Collections.Immutable;
using Mist.CLI.Args;
using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Args;

/// <summary>
/// The controls are Requirement 12 of <c>specs/diagnostics/requirements.md</c> and the diagnostic-controls
/// paragraph of <c>book/src/tooling/cli-usage.md</c>; the ordering and first-error decisions are MIST-1868's.
/// </summary>
[Trait("Tier", "1")]
public sealed class DiagnosticControlParserDecisionTests
{
    private static readonly MistDiagnostic Promotable = DiagnosticFixtures.Descriptor("MIST9001", "promotable", DiagnosticSeverity.Warning, policy: PolicyClass.Promotable);
    private static readonly MistDiagnostic Optional = DiagnosticFixtures.Descriptor("MIST9004", "optional", DiagnosticSeverity.Warning, policy: PolicyClass.Optional);
    private static readonly MistDiagnostic OtherOptional = DiagnosticFixtures.Descriptor("MIST9005", "other optional", DiagnosticSeverity.Warning, policy: PolicyClass.Optional);

    [Theory]
    [InlineData("build")]
    [InlineData("run")]
    [InlineData("test")]
    [InlineData("inspect")]
    [InlineData("restore")]
    [InlineData("steering")]
    [InlineData("version")]
    public void BarePromotionLeavesAFollowingCommandWordToTheCommandLine(string command)
    {
        var parsed = DiagnosticControlParser.Parse(["--warnings-as-errors", command, "project"], Find);

        Assert.Null(parsed.Error);
        Assert.Equal([command, "project"], parsed.Remaining.ToArray());
        Assert.True(parsed.Controls.Policy!.PromoteAll);
        Assert.Empty(parsed.Controls.Policy.Promoted ?? []);
    }

    [Fact]
    public void BarePromotionAfterAListDropsTheList()
    {
        var policy = DiagnosticControlParser.Parse(["--warnings-as-errors", "MIST9001,MIST9004", "--warnings-as-errors"], Find).Controls.Policy!;

        Assert.True(policy.PromoteAll);
        Assert.Empty(policy.Promoted ?? []);
    }

    [Fact]
    public void AListAfterBarePromotionPromotesOnlyTheList()
    {
        var policy = DiagnosticControlParser.Parse(["--warnings-as-errors", "--warnings-as-errors", "MIST9001"], Find).Controls.Policy!;

        Assert.False(policy.PromoteAll);
        Assert.Equal(["MIST9001"], Values(policy.Promoted));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("MIST9001")]
    public void PromotionPreservesEarlierDiagnosticToggles(string? promotedNumber)
    {
        string[] args = ["--enable", "MIST9004", "--disable", "MIST9005", "--warnings-as-errors"];
        var parsed = DiagnosticControlParser.Parse(promotedNumber is null ? args : [.. args, promotedNumber], Find);

        Assert.Null(parsed.Error);
        var policy = Assert.IsType<DiagnosticPolicyOverrides>(parsed.Controls.Policy);
        Assert.Equal(promotedNumber is null, policy.PromoteAll);
        Assert.Equal(promotedNumber is null ? [] : [promotedNumber], Values(policy.Promoted));
        Assert.Equal(["MIST9004"], Values(policy.Enabled));
        Assert.Equal(["MIST9005"], Values(policy.Disabled));
    }

    [Theory]
    [InlineData("--disable", "--enable")]
    [InlineData("--enable", "--disable")]
    public void TheLaterSwitchMovesOnlyItsNumber(string first, string later)
    {
        var policy = DiagnosticControlParser.Parse([first, "MIST9004,MIST9005", later, "MIST9004"], Find).Controls.Policy!;

        Assert.Equal(["MIST9004"], Values(Switched(policy, later)));
        Assert.Equal(["MIST9005"], Values(Switched(policy, first)));
    }

    [Theory]
    [InlineData("--disable")]
    [InlineData("--enable")]
    public void RepeatingTheSameSwitchPreservesBothNumbers(string flag)
    {
        var parsed = DiagnosticControlParser.Parse([flag, "MIST9004", flag, "MIST9005"], Find);

        Assert.Null(parsed.Error);
        Assert.Equal(["MIST9004", "MIST9005"], Values(Switched(parsed.Controls.Policy!, flag)));
    }

    [Theory]
    [InlineData("--diagnostic-format")]
    [InlineData("--diagnostic-file")]
    [InlineData("--min-severity")]
    [InlineData("--disable")]
    [InlineData("--enable")]
    public void AFollowingOptionIsAMissingValueAndStaysOnTheCommandLine(string flag)
    {
        var parsed = DiagnosticControlParser.Parse([flag, "--quiet"], Find);

        Assert.Contains("Missing value", parsed.Error);
        Assert.Contains(flag, parsed.Error);
        Assert.Equal(["--quiet"], parsed.Remaining.ToArray());
    }

    [Fact]
    public void BarePromotionBeforeAnOptionLeavesTheOptionOnTheCommandLine()
    {
        var parsed = DiagnosticControlParser.Parse(["--warnings-as-errors", "-q"], Find);

        Assert.Null(parsed.Error);
        Assert.True(parsed.Controls.Policy!.PromoteAll);
        Assert.Equal(["-q"], parsed.Remaining.ToArray());
    }

    [Fact]
    public void TheFirstErrorIsKeptAndLaterControlsStillApply()
    {
        var parsed = DiagnosticControlParser.Parse(["--min-severity", "fatal", "project", "--diagnostic-format", "yaml",
            "--diagnostic-format", "json", "--disable", "MIST9004", "--quiet"], Find);

        Assert.Contains("fatal", parsed.Error);
        Assert.DoesNotContain("yaml", parsed.Error);
        Assert.Equal(DiagnosticFormat.Json, parsed.Controls.Format);
        Assert.Equal(DiagnosticSeverity.Info, parsed.Controls.Minimum);
        Assert.Equal(["MIST9004"], Values(parsed.Controls.Policy!.Disabled));
        Assert.Equal(["project", "--quiet"], parsed.Remaining.ToArray());
    }

    [Fact]
    public void ADiagnosticFilePathWithANulCharacterIsRefusedAndNotKept()
    {
        var parsed = DiagnosticControlParser.Parse(["--diagnostic-file", "out\0.json", "--diagnostic-format", "json"], Find);

        Assert.Contains("diagnostic file path", parsed.Error);
        Assert.Null(parsed.Controls.File);
        Assert.Equal(DiagnosticFormat.Json, parsed.Controls.Format);
    }

    [Fact]
    public void CommandRefusalPrecedesLexRefusalWhichPrecedesTheParseError()
    {
        var parsed = DiagnosticControlParser.Parse(["--disable", "MIST9999"], Find);
        Assert.NotNull(parsed.Error);

        Assert.Equal("Unknown diagnostic option for 'steering'.", DiagnosticControlParser.ValidateCommand(parsed, CliCommand.Steering, CompilerMode.Lex));
        Assert.Equal("Unknown policy option for lex mode.", DiagnosticControlParser.ValidateCommand(parsed, CliCommand.Build, CompilerMode.Lex));
        Assert.Equal(parsed.Error, DiagnosticControlParser.ValidateCommand(parsed, CliCommand.Build, CompilerMode.Compile));
    }

    [Fact]
    public void LexModeReportsTheParseErrorOfAnOutputControl()
    {
        var parsed = DiagnosticControlParser.Parse(["--min-severity", "fatal"], Find);

        Assert.Equal(parsed.Error, DiagnosticControlParser.ValidateCommand(parsed, CliCommand.Build, CompilerMode.Lex));
    }

    [Fact]
    public void CreateOutputAppliesThePolicyToTheDefault()
    {
        var overrides = new DiagnosticPolicyOverrides(PromoteAll: true, Disabled: [Optional.Number]);
        using var error = new StringWriter();

        var operation = new DiagnosticControls(Policy: overrides).CreateOutput(error, "build", "project").Operation;

        Assert.Equal(overrides, operation.Overrides);
        Assert.True(operation.Policy.PromoteAllWarnings);
        Assert.Equal("command line", operation.Policy.PromotionSource);
        Assert.Equal(["MIST9004"], Values(operation.Policy.Disabled));
    }

    [Fact]
    public void CreateOutputWithoutAPolicyHasEmptyOverridesAndTheDefaultPolicy()
    {
        using var error = new StringWriter();

        var operation = new DiagnosticControls().CreateOutput(error, "build", "project").Operation;

        Assert.Equal(new DiagnosticPolicyOverrides(), operation.Overrides);
        Assert.False(operation.Policy.PromoteAllWarnings);
        Assert.Equal(DiagnosticPolicy.Default.PromotionSource, operation.Policy.PromotionSource);
        Assert.Empty(operation.Policy.Promoted);
        Assert.Empty(operation.Policy.Disabled);
        Assert.Empty(operation.Policy.Enabled);
    }

    private static ImmutableHashSet<DiagnosticNumber>? Switched(DiagnosticPolicyOverrides policy, string flag)
    {
        return flag == "--disable" ? policy.Disabled : policy.Enabled;
    }

    private static string[] Values(ImmutableHashSet<DiagnosticNumber>? numbers)
    {
        return [.. (numbers ?? []).Select(n => n.Value).Order()];
    }

    private static MistDiagnostic? Find(string number)
    {
        return new[] { Promotable, Optional, OtherOptional }.FirstOrDefault(d => d.Number.Value == number);
    }
}