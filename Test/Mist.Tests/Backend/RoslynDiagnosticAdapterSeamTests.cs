using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using RoslynApi = Microsoft.CodeAnalysis;

namespace Mist.Tests.Backend;

[Trait("Tier", "4")]
public sealed class RoslynDiagnosticAdapterSeamTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "mist-native-diagnostics-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if(Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GeneratedCSharpToRoslynRetainsEveryNativeDiagnosticAndWrapsOnlyErrors()
    {
        _ = Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "Program.mist.cs");
        File.WriteAllText(source, "public class Probe { public string Text; public void Run() { Missing(); } }");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var result = CSharpCompilationDriver.Compile(directory, Path.Combine(directory, "bin"), "Probe", diagnostics,
            outputType: ProjectOutputType.Library);

        Assert.False(result.Succeeded);
        Assert.Null(result.PrimaryArtifactPath);
        Assert.False(File.Exists(Path.Combine(directory, "bin", "Probe.dll")));
        Assert.Contains(result.Native, diagnostic => diagnostic.Code == "CS8618" && diagnostic.Severity == "warning");
        Assert.Contains(result.Native, diagnostic => diagnostic.Code == "CS0103" && diagnostic.Severity == "error");
        Assert.Equal(result.Native.Count(diagnostic => diagnostic.Severity == "error"), result.Diagnostics.Length);
        foreach(var occurrence in result.Diagnostics)
        {
            Assert.Equal(BackendDiagnostics.CSharpError.Number, occurrence.Number);
            Assert.Equal(DiagnosticSeverity.Error, occurrence.Native?.NativeSeverity);
            var generated = Assert.IsType<GeneratedLocation>(occurrence.Location);
            Assert.Equal(source, generated.Path);
            Assert.True(generated.Native.Start.Column > 0);
            Assert.Null(generated.Mapped);
        }

        var descriptor = new RoslynApi.DiagnosticDescriptor("CS1234", "native", "Upstream", "Compiler", RoslynApi.DiagnosticSeverity.Info, true);
        var locationFree = RoslynDiagnosticAdapter.Snapshot(RoslynApi.Diagnostic.Create(descriptor, RoslynApi.Location.None));
        var unavailable = Assert.IsType<GeneratedLocation>(locationFree.Location);
        Assert.Equal(string.Empty, unavailable.Path);
        Assert.Equal(default, unavailable.Native);
    }
}