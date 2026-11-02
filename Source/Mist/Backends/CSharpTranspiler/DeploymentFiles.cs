using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler;

internal sealed class DeploymentFiles : IDeploymentFileSource
{
    public static DeploymentFiles Instance { get; } = new();

    public bool Exists(string path)
    {
        return File.Exists(path);
    }

    public DateTime LastWriteTimeUtc(string path)
    {
        return File.GetLastWriteTimeUtc(path);
    }

    public ImmutableArray<string> RuntimesListing(string outputDirectory)
    {
        var runtimes = Path.Combine(outputDirectory, "runtimes");
        try
        {
            return Directory.Exists(runtimes)
                ? [.. Directory.EnumerateFiles(runtimes, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(outputDirectory, file))]
                : [];
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}