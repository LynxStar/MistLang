using Mist.CLI.Output;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "1")]
public sealed class InspectFeedbackTests
{
    private static readonly Diagnostic Package = Make("MIST_PKG_002", DiagnosticSeverity.Error, "Package 'BadPkg' version 'not-valid' could not be found.");
    private static readonly Diagnostic Framework = Make("MIST_FWREF_002", DiagnosticSeverity.Error, "Framework reference 'C' is not a known shared framework.");
    private static readonly Diagnostic Warning = Make("MIST_PKG_003", DiagnosticSeverity.Warning, "Package 'W' contributed no assemblies.");

    [Fact]
    public void AnError_IsWrittenOnceInTheBuildsText_AndFails()
    {
        using var error = new StringWriter();

        var failed = InspectFeedback.Report([Package], error);

        Assert.True(failed);
        Assert.Equal(DiagnosticFormatter.Format(Package) + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void AWarning_IsWrittenAndDoesNotFail()
    {
        using var error = new StringWriter();

        var failed = InspectFeedback.Report([Warning], error);

        Assert.False(failed);
        Assert.Equal(DiagnosticFormatter.Format(Warning) + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void PackageAndFrameworkDiagnosticsTogether_AreEachWrittenOnceInOrder()
    {
        using var error = new StringWriter();

        var failed = InspectFeedback.Report([Package, Warning, Framework], error);

        Assert.True(failed);
        Assert.Equal(
            [DiagnosticFormatter.Format(Package), DiagnosticFormatter.Format(Warning), DiagnosticFormatter.Format(Framework)],
            error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void NoDiagnostics_WritesNothing()
    {
        using var error = new StringWriter();

        Assert.False(InspectFeedback.Report([], error));
        Assert.Empty(error.ToString());
    }

    private static Diagnostic Make(string id, DiagnosticSeverity severity, string message)
    {
        return new Diagnostic(new DiagnosticId(id), severity, PipelineStage.Driver, message, default, "", null);
    }
}