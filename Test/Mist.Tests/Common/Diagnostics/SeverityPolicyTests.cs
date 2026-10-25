using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class SeverityPolicyTests
{
    public static TheoryData<PolicyClass, DiagnosticSeverity, string, DiagnosticSeverity, bool> Combinations()
    {
        var data = new TheoryData<PolicyClass, DiagnosticSeverity, string, DiagnosticSeverity, bool>();
        foreach(var policy in Enum.GetValues<PolicyClass>())
        {
            foreach(var severity in Enum.GetValues<DiagnosticSeverity>())
            {
                if(policy != PolicyClass.Fixed && severity == DiagnosticSeverity.Error)
                {
                    continue;
                }

                foreach(var shape in new[] { "none", "all", "listed", "other-listed" })
                {
                    var promotable = policy != PolicyClass.Fixed;
                    var promoted = promotable && (shape == "listed" || (shape == "all" && severity == DiagnosticSeverity.Warning));
                    data.Add(policy, severity, shape, promoted ? DiagnosticSeverity.Error : severity, promoted);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void ApplyNeverLowersNeverTouchesFixedAndPromotesOnlyWhatPolicyNames(
        PolicyClass policyClass, DiagnosticSeverity defaultSeverity, string shape, DiagnosticSeverity expected, bool changed)
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST_FIXTURE_POLICY", "probe", defaultSeverity, policy: policyClass);
        var other = DiagnosticNumber.Mint("MIST_FIXTURE_OTHER");
        var policy = shape switch
        {
            "all" => DiagnosticPolicy.Default with { PromoteAllWarnings = true },
            "listed" => DiagnosticPolicy.Default with { Promoted = [descriptor.Number] },
            "other-listed" => DiagnosticPolicy.Default with { Promoted = [other], PromoteAllWarnings = false },
            _ => DiagnosticPolicy.Default,
        };

        var (effective, reason) = SeverityPolicy.Apply(descriptor, policy);

        Assert.Equal(expected, effective);
        Assert.True(effective >= defaultSeverity);
        Assert.Equal(changed, reason is not null);
        if(policyClass == PolicyClass.Fixed)
        {
            Assert.Equal(defaultSeverity, effective);
        }
    }

    [Fact]
    public void EffectiveSeverityIsComputedOnceAtAdmissionAndRecordedWithItsReason()
    {
        var descriptor = DiagnosticFixtures.Descriptor("MIST_FIXTURE_PROMOTED", "probe", DiagnosticSeverity.Warning, policy: PolicyClass.Promotable);
        var operation = new DiagnosticOperation { Policy = DiagnosticPolicy.Default with { PromoteAllWarnings = true } };
        var bag = new DiagnosticBag(operation);

        bag.ReportAt(descriptor, new Absent(AbsenceReason.Unavailable));
        operation.Policy = DiagnosticPolicy.Default;
        var later = new DiagnosticBag(operation);
        later.Add(bag.Admitted[0]);

        var occurrence = Assert.Single(bag.Admitted);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Equal(DiagnosticSeverity.Warning, occurrence.DefaultSeverity);
        Assert.Contains("warnings_as_errors", occurrence.SeverityReason);
        Assert.Equal(DiagnosticSeverity.Error, later.Admitted[0].Severity);
        Assert.True(bag.HasErrors);
    }

    [Fact]
    public void HasErrorsReadsEffectiveSeverityAndAnInfoNeverCounts()
    {
        var info = DiagnosticFixtures.Descriptor("MIST_FIXTURE_INFO", "note", DiagnosticSeverity.Info, policy: PolicyClass.Promotable);
        var warning = DiagnosticFixtures.Descriptor("MIST_FIXTURE_WARN", "warn", DiagnosticSeverity.Warning, policy: PolicyClass.Promotable);
        var all = new DiagnosticBag(new DiagnosticOperation { Policy = DiagnosticPolicy.Default with { PromoteAllWarnings = true } });
        var none = new DiagnosticBag();

        all.ReportAt(info, new Absent(AbsenceReason.Unavailable));
        none.ReportAt(warning, new Absent(AbsenceReason.Unavailable));
        Assert.False(all.HasErrors);
        Assert.False(none.HasErrors);

        all.ReportAt(warning, new Absent(AbsenceReason.Unavailable));
        Assert.True(all.HasErrors);
    }

    [Theory]
    [InlineData(PolicyClass.Fixed, true, false, false, true)]
    [InlineData(PolicyClass.Promotable, true, true, false, true)]
    [InlineData(PolicyClass.Optional, true, false, false, true)]
    [InlineData(PolicyClass.Optional, true, true, false, false)]
    [InlineData(PolicyClass.Optional, false, false, false, false)]
    [InlineData(PolicyClass.Optional, false, false, true, true)]
    [InlineData(PolicyClass.Optional, false, true, true, true)]
    public void OnlyAnOptionalRuleCanBeSwitched(PolicyClass policyClass, bool enabledByDefault, bool disabled, bool enabled, bool expected)
    {
        var descriptor = new MistDiagnostic(
            DiagnosticNumber.Mint("MIST_FIXTURE_SWITCH"), "Switch probe", "probe", "A probe.", DiagnosticCategory.Typ,
            policyClass, DiagnosticSeverity.Warning, EnabledByDefault: enabledByDefault);
        var policy = DiagnosticPolicy.Default with
        {
            Disabled = disabled ? [descriptor.Number] : [],
            Enabled = enabled ? [descriptor.Number] : [],
        };

        Assert.Equal(expected, policy.IsEnabled(descriptor));
    }

    [Fact]
    public void TheSeveritySetIsInfoWarningAndError()
    {
        Assert.Equal(["Info", "Warning", "Error"], Enum.GetNames<DiagnosticSeverity>());
        Assert.Equal(["Fixed", "Promotable", "Optional"], Enum.GetNames<PolicyClass>());
    }
}