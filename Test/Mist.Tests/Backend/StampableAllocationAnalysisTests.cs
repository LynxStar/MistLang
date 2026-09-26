using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class StampableAllocationAnalysisTests : ProgramModelProbe
{
    public StampableAllocationAnalysisTests()
        : base("MistStampableAllocations_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("new", "int", true)]
    [InlineData("stackalloc", "int", true)]
    [InlineData("new", "string", false)]
    [InlineData("stackalloc", "string", false)]
    public void ProgramModelToAnalysis_ClosedArgumentsDecideAllocationStampability(string allocation, string argument, bool accepted)
    {
        var model = BuildModel($"var actual = {allocation} Box<{argument}>[2];", "public struct Box<T> { public T Item; }", "", out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        AssertVerdict(findings, allocation, accepted);
    }

    [Theory]
    [InlineData("new", "", false)]
    [InlineData("stackalloc", "", false)]
    [InlineData("new", "where T : struct", false)]
    [InlineData("stackalloc", "where T : struct", false)]
    [InlineData("new", "where T : stampable", true)]
    [InlineData("stackalloc", "where T : stampable", true)]
    public void ProgramModelToAnalysis_OpenStorageUsesItsConstraintProof(string allocation, string constraints, bool accepted)
    {
        var model = Build($$"""
            namespace Probe;
            public struct Box<T> { public T Item; }
            public class Factory
            {
                public static void Make<T>() {{constraints}}
                {
                    var actual = {{allocation}} Box<T>[2];
                }
            }
            """, out var diagnostics);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        AssertVerdict(findings, allocation, accepted);
    }

    private static void AssertVerdict(DiagnosticBag findings, string allocation, bool accepted)
    {
        var errors = findings.ToImmutableArray().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

        if(accepted)
        {
            Assert.Empty(errors);
        }
        else
        {
            var finding = Assert.Single(errors);
            Assert.Equal(allocation == "new" ? "MIST_PA_ARRAY_REQUIRES_INITIALIZER" : "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE", finding.Id.Value);
            Assert.Contains("Item", finding.Message, StringComparison.Ordinal);
        }
    }
}