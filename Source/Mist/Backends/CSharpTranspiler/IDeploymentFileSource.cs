using System.Collections.Immutable;

namespace Mist.Backends.CSharpTranspiler;

internal interface IDeploymentFileSource
{
    bool Exists(string path);

    DateTime LastWriteTimeUtc(string path);

    /// <summary>Every file beneath <paramref name="outputDirectory"/>'s <c>runtimes</c> folder, relative to <paramref name="outputDirectory"/> itself.</summary>
    ImmutableArray<string> RuntimesListing(string outputDirectory);
}