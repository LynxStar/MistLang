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
        return Files(Path.Combine(outputDirectory, "runtimes"), outputDirectory);
    }

    public ImmutableArray<string> Listing(string directory)
    {
        return Files(directory, directory);
    }

    private static ImmutableArray<string> Files(string directory, string relativeTo)
    {
        try
        {
            return Directory.Exists(directory)
                ? [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(relativeTo, file))]
                : [];
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}