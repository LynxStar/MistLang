using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class OptionalRuleGateTests
{
    [Fact]
    public void TheExhaustivenessRuleIsTheOptionalRuleOfItsAnalyzer()
    {
        Assert.Same(ProgramAnalysisDiagnostics.SwitchNotExhaustive, new ExhaustivenessAnalyzer().OptionalRule);
        Assert.Equal(PolicyClass.Optional, ProgramAnalysisDiagnostics.SwitchNotExhaustive.Policy);
        Assert.True(ProgramAnalysisDiagnostics.SwitchNotExhaustive.EnabledByDefault);
    }

    [Fact]
    public void ADisabledOptionalRuleDoesNotRunAndEveryOtherAnalyzerDoes()
    {
        var disabled = DiagnosticPolicy.Default with { Disabled = [ProgramAnalysisDiagnostics.SwitchNotExhaustive.Number] };

        Assert.False(ProgramAnalysisRunner.Runs(new ExhaustivenessAnalyzer(), disabled));
        Assert.True(ProgramAnalysisRunner.Runs(new ExhaustivenessAnalyzer(), DiagnosticPolicy.Default));
        Assert.True(ProgramAnalysisRunner.Runs(new CastAnalyzer(), disabled));
    }
}