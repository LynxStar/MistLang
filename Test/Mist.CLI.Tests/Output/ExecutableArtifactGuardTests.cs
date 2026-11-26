using Mist.CLI.Output;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests.Output;

[Trait("Tier", "1")]
public sealed class ExecutableArtifactGuardTests
{
    [Fact]
    public void FailedCompileKeepsItsOriginalDiagnostic()
    {
        var error = DiagnosticFixtures.Occurrence("MIST9001", DiagnosticSeverity.Error, PipelineStage.Driver,
            "Compilation failed.", new Absent(AbsenceReason.TestOrigin));
        var compiled = new CompilerResult(false, null, [error]);

        var guarded = CommandFeedback.RequireArtifact(compiled, new DiagnosticOutput(TextWriter.Null));

        Assert.False(guarded.Succeeded);
        Assert.Equal(error, Assert.Single(guarded.Diagnostics));
    }

    [Fact]
    public void SuccessfulCompileWithAnArtifactKeepsItsResult()
    {
        var location = new Absent(AbsenceReason.TestOrigin);
        var warning = DiagnosticFixtures.Occurrence("MIST9002", DiagnosticSeverity.Warning, PipelineStage.Driver,
            "Compilation warning.", location);
        var native = new NativeDiagnostic("test", "TEST001", "info", "Compiler note.", location);
        var compiled = new CompilerResult(true, "app.dll", [warning], [native]);

        var guarded = CommandFeedback.RequireArtifact(compiled, new DiagnosticOutput(TextWriter.Null));

        Assert.True(guarded.Succeeded);
        Assert.Equal("app.dll", guarded.PrimaryOutputArtifactPath);
        Assert.Equal(warning, Assert.Single(guarded.Diagnostics));
        Assert.Equal(native, Assert.Single(guarded.Native));
    }

    [Fact]
    public void SuccessfulCompileWithoutAnArtifactAppendsOneMissingArtifactError()
    {
        var warning = DiagnosticFixtures.Occurrence("MIST9002", DiagnosticSeverity.Warning, PipelineStage.Driver,
            "Compilation warning.", new Absent(AbsenceReason.TestOrigin));
        var compiled = new CompilerResult(true, null, [warning]);

        var guarded = CommandFeedback.RequireArtifact(compiled, new DiagnosticOutput(TextWriter.Null));

        Assert.False(guarded.Succeeded);
        Assert.Collection(guarded.Diagnostics,
            diagnostic => Assert.Equal(warning, diagnostic),
            diagnostic =>
            {
                Assert.Equal("MIST0451", diagnostic.Number.Value);
                Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            });
    }
}