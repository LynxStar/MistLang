namespace Mist.Driver.Contracts;

public enum TargetPlatform
{
    Windows,
    Linux,
    MacOS,
}

public static class TargetPlatforms
{
    public static TargetPlatform Host { get; } = Of(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS());

    internal static TargetPlatform Of(bool windows, bool macOS)
    {
        return (windows, macOS) switch
        {
            (true, _) => TargetPlatform.Windows,
            (_, true) => TargetPlatform.MacOS,
            _ => TargetPlatform.Linux,
        };
    }
}