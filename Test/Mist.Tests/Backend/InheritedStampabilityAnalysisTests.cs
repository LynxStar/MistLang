using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class InheritedStampabilityAnalysisTests : ProgramModelProbe
{
    public InheritedStampabilityAnalysisTests()
        : base("MistInheritedStampability_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Ancestor { public string owner; }
        public struct Derived : Ancestor { }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("new", "MIST0225")]
    [InlineData("stackalloc", "MIST0226")]
    public void ProgramModelToAnalysis_InheritedReferenceStorageReachesTheAllocationGate(string allocation, string diagnosticId)
    {
        var model = BuildModel($"var actual = {allocation} Derived[2];", "", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        var finding = Assert.Single(findings.ToImmutableArray(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(diagnosticId, finding.Number.Value);
        Assert.Contains("field 'owner'", finding.Message, StringComparison.Ordinal);
    }
}