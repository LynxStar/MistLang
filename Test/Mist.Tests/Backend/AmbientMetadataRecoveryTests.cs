using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class AmbientMetadataRecoveryTests
{
    [Fact]
    public void AnAssemblyLoadFailureKeepsRecoveryAndSnapshotsItsPathAndUpstreamMessage()
    {
        using var metadata = new MetadataLoadContext(new PathAssemblyResolver([typeof(object).Assembly.Location]), "System.Private.CoreLib");
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var path = Path.GetFullPath("missing-ambient-" + Guid.NewGuid().ToString("N") + ".dll");

        var loaded = AmbientTypeProviderV2Factory.LoadAssemblies([path], metadata, diagnostics);

        Assert.Empty(loaded);
        var occurrence = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.AssemblyLoadFailed.Number, occurrence.Number);
        Assert.Equal(path, Assert.IsType<FileOnly>(occurrence.Location).Path);
        Assert.Equal(path, Assert.IsType<PathValue>(occurrence.Arguments[0]).Path);
        Assert.Contains(Path.GetFileName(path), Assert.IsType<TextValue>(occurrence.Arguments[1]).Text);
    }
}