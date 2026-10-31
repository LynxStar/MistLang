using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.CLI;

public static class ArtifactLaunch
{
    private const string AspNetCoreFramework = "Microsoft.AspNetCore.App";

    /// <summary>
    /// A program that references ASP.NET Core is told its content root is the artifact's folder, where the build
    /// deploys <c>wwwroot/</c>; the working directory stays the user's, so no other program's relative paths move.
    /// </summary>
    public static ProcessStartInfo StartInfo(string artifactPath, ImmutableArray<FrameworkReference> frameworkReferences)
    {
        var hostsWeb = !frameworkReferences.IsDefault && frameworkReferences.Any(reference => reference.Name == AspNetCoreFramework);
        ImmutableArray<string> arguments = hostsWeb
            ? [artifactPath, "--contentRoot", Path.GetDirectoryName(Path.GetFullPath(artifactPath))!]
            : [artifactPath];
        return new ProcessStartInfo("dotnet", arguments) { UseShellExecute = false };
    }
}