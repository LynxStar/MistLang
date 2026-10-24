using System.Runtime.InteropServices;
using Mist.Driver.Contracts;

namespace Mist.Driver;

internal static class TargetRuntime
{
    public static string Select(TargetPlatform? platform, string hostRid, Architecture hostArchitecture, string? rid)
    {
        if(rid is not null)
        {
            return rid;
        }

        if(platform is not { } named || named == PlatformOf(hostRid))
        {
            return hostRid;
        }

        return $"{OperatingSystemToken(named)}-{hostArchitecture.ToString().ToLowerInvariant()}";
    }

    private static TargetPlatform PlatformOf(string hostRid)
    {
        return TargetPlatforms.Of(
            hostRid.StartsWith("win", StringComparison.Ordinal),
            hostRid.StartsWith("osx", StringComparison.Ordinal));
    }

    private static string OperatingSystemToken(TargetPlatform platform)
    {
        return platform switch
        {
            TargetPlatform.Windows => "win",
            TargetPlatform.MacOS => "osx",
            _ => "linux",
        };
    }
}