using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "3")]
public sealed class RestoreReferenceDiagnosticRoutingTests
{
    [Fact]
    public void MissingReference_IsDeliveredOnceThroughTheSelectedChannel()
    {
        const string referenceName = "Bridge";
        var projectFolder = Directory.CreateTempSubdirectory("mist-restore-reference-");
        try
        {
            var manifestPath = Path.Combine(projectFolder.FullName, "mist.toml");
            var missingPath = Path.Combine(projectFolder.FullName, "Bridge.csproj");
            var reference = new ProjectReference(referenceName, missingPath, ReferenceKind.CSharpProject);
            var reported = new List<Diagnostic>();
            using var output = new StringWriter();

            var callbackErrors = DependencyRestore.RestoreReferences(
                projectFolder.FullName, [reference], output, unexpectedRestore, report: reported.Add);

            var diagnostic = Assert.Single(reported);
            Assert.Empty(callbackErrors);
            Assert.Same(DriverDiagnostics.ReferenceFileNotFound, diagnostic.Descriptor);
            Assert.Equal("MIST0072", diagnostic.Number.Value);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal(PipelineStage.Driver, diagnostic.Stage);
            Assert.Equal(new ProjectLocation(manifestPath, null), diagnostic.Location);
            Assert.Equal<SlotValue>([new NameValue(referenceName), new PathValue(missingPath)], diagnostic.Arguments);

            var textErrors = DependencyRestore.RestoreReferences(projectFolder.FullName, [reference], output, unexpectedRestore);

            Assert.Equal(
                $"{manifestPath}: error MIST0072: Reference '{referenceName}' names '{missingPath}', which does not exist.",
                Assert.Single(textErrors));
            Assert.Empty(output.ToString());
        }
        finally
        {
            projectFolder.Delete();
        }

        static (int ExitCode, string Stdout, string Stderr) unexpectedRestore(string project, string directory)
        {
            throw new InvalidOperationException($"Unexpected dotnet restore of '{project}' in '{directory}'.");
        }
    }
}