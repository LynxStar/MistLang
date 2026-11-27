using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class StdlibSourceReadFailureTests
{
    [Fact]
    public void LoadFrom_ReportsUnreadableSourceAndLoadsLaterSource()
    {
        const string root = "Standard";
        const string contents = "namespace Standard; public class Readable { }\n";
        var unreadablePath = Path.Combine(root, "AUnreadable.mist");
        var readablePath = Path.Combine(root, "BReadable.mist");
        var diagnostics = new DiagnosticBag();

        var units = StdlibLoader.LoadFrom(root, diagnostics,
            _ => [unreadablePath, readablePath],
            path => path == unreadablePath ? throw new IOException("Read failed.") : contents);

        var diagnostic = Assert.Single(diagnostics.Admitted);
        Assert.Equal("MIST0031", diagnostic.Number.Value);
        Assert.Equal(unreadablePath, diagnostic.Location.Path);

        var unit = Assert.Single(units);
        Assert.Equal(readablePath, unit.Source.Path);
        Assert.Equal(contents, unit.Source.Contents);
        Assert.True(unit.IsStdlib);
    }
}