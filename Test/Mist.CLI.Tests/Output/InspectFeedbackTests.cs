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
        var feedback = new InspectFeedback(error);

        feedback.Report([Package]);

        Assert.True(feedback.Failed);
        Assert.Equal(DiagnosticFormatter.Format(Package) + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void AWarning_IsWrittenAndDoesNotFail()
    {
        using var error = new StringWriter();
        var feedback = new InspectFeedback(error);

        feedback.Report([Warning]);

        Assert.False(feedback.Failed);
        Assert.Equal(DiagnosticFormatter.Format(Warning) + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void PackageAndFrameworkDiagnosticsTogether_AreEachWrittenOnceInOrder()
    {
        using var error = new StringWriter();
        var feedback = new InspectFeedback(error);

        feedback.Report([Package, Warning, Framework]);

        Assert.True(feedback.Failed);
        Assert.Equal(
            [DiagnosticFormatter.Format(Package), DiagnosticFormatter.Format(Warning), DiagnosticFormatter.Format(Framework)],
            Lines(error));
    }

    [Fact]
    public void AnErrorWrittenBeforeTheRestore_IsNotWrittenAgainWithWhatFollowed_AndStillFails()
    {
        using var error = new StringWriter();
        var feedback = new InspectFeedback(error);

        feedback.Report([Package]);
        var beforeRestore = Lines(error);
        feedback.Report([Package, Warning]);
        feedback.Report([Package, Warning]);

        Assert.Equal([DiagnosticFormatter.Format(Package)], beforeRestore);
        Assert.Equal([DiagnosticFormatter.Format(Package), DiagnosticFormatter.Format(Warning)], Lines(error));
        Assert.True(feedback.Failed);
    }

    [Fact]
    public void NoDiagnostics_WritesNothing()
    {
        using var error = new StringWriter();
        var feedback = new InspectFeedback(error);

        feedback.Report([]);

        Assert.False(feedback.Failed);
        Assert.Empty(error.ToString());
    }

    private static string[] Lines(StringWriter error)
    {
        return error.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static Diagnostic Make(string id, DiagnosticSeverity severity, string message)
    {
        return DiagnosticFixtures.Occurrence(id, severity, PipelineStage.Driver, message, default, "", null);
    }
}