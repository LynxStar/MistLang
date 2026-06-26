namespace Mist.CLI.Steering;

/// <summary>
/// Result of a steering installation run.
/// </summary>
/// <param name="Succeeded">Whether installation completed successfully.</param>
/// <param name="Error">A human-readable failure message when <paramref name="Succeeded"/> is <c>false</c>; otherwise <c>null</c>.</param>
/// <param name="Removed">Paths of previously-installed managed files that were deleted.</param>
/// <param name="Installed">Paths of files written during this run.</param>
/// <param name="SkippedUserFiles">
/// Paths of files in the steering directory that were left untouched because
/// they are not Mist-managed (the user's own steering files).
/// </param>
public sealed record SteeringInstallResult(
    bool Succeeded,
    string? Error,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> SkippedUserFiles)
{
    private static readonly IReadOnlyList<string> Empty = [];

    /// <summary>Creates a failure result carrying the supplied <paramref name="error"/> message.</summary>
    public static SteeringInstallResult Failure(string error) =>
        new(false, error, Empty, Empty, Empty);
}

/// <summary>
/// Installs the Mist steering files bundled alongside the compiler binary into a
/// project's <c>.kiro/steering/</c> directory.
///
/// <para>
/// The installation is idempotent and user-safe. Every bundled file carries a
/// sentinel marker (<see cref="ManagedMarker"/>). On each run the installer:
/// </para>
/// <list type="number">
///   <item>Deletes every <c>*.md</c> in the target directory that contains the marker
///   (files installed by a previous run, possibly under names no longer shipped).</item>
///   <item>Copies the current bundled set in fresh.</item>
/// </list>
/// <para>
/// Files that do not contain the marker — the user's own steering files — are never
/// touched. Running the command repeatedly converges to exactly the bundled set plus
/// whatever the user authored.
/// </para>
/// </summary>
public static class SteeringInstaller
{
    /// <summary>
    /// Sentinel substring that identifies a Mist-managed steering file. Must match the
    /// marker embedded in the distributed <c>mist-*.md</c> files. Any file containing
    /// this substring is considered installer-owned and is eligible for deletion.
    /// </summary>
    public const string ManagedMarker = "MIST-MANAGED-STEERING";

    /// <summary>
    /// Installs the bundled steering files into <paramref name="projectFolder"/>/.kiro/steering.
    /// </summary>
    /// <param name="projectFolder">The project root. The steering directory is created if absent.</param>
    /// <returns>
    /// A result describing what was removed, installed, and skipped. On failure,
    /// <see cref="SteeringInstallResult.Succeeded"/> is <c>false</c> and
    /// <see cref="SteeringInstallResult.Error"/> carries the reason.
    /// </returns>
    public static SteeringInstallResult Install(string projectFolder) =>
        InstallFrom(projectFolder, Path.Combine(AppContext.BaseDirectory, "Steering"));

    /// <summary>
    /// Installs steering files from an explicit <paramref name="sourceRoot"/> into
    /// <paramref name="projectFolder"/>/.kiro/steering. The production entry point
    /// <see cref="Install(string)"/> anchors the source root at
    /// <see cref="AppContext.BaseDirectory"/>; this overload lets callers (and tests)
    /// supply an arbitrary bundled set.
    /// </summary>
    /// <param name="projectFolder">The project root. The steering directory is created if absent.</param>
    /// <param name="sourceRoot">Directory containing the bundled <c>*.md</c> steering files to install.</param>
    /// <returns>
    /// A result describing what was removed, installed, and skipped. On failure,
    /// <see cref="SteeringInstallResult.Succeeded"/> is <c>false</c> and
    /// <see cref="SteeringInstallResult.Error"/> carries the reason.
    /// </returns>
    public static SteeringInstallResult InstallFrom(string projectFolder, string sourceRoot)
    {
        string[] bundledFiles;
        try
        {
            bundledFiles = Directory.Exists(sourceRoot)
                ? Directory.GetFiles(sourceRoot, "*.md", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch(Exception ex)
        {
            return SteeringInstallResult.Failure($"Failed to read bundled steering files from '{sourceRoot}': {ex.Message}");
        }

        if(bundledFiles.Length == 0)
        {
            return SteeringInstallResult.Failure($"No bundled steering files found at '{sourceRoot}'. The compiler installation may be incomplete.");
        }

        Array.Sort(bundledFiles, StringComparer.Ordinal);

        var steeringDir = Path.Combine(projectFolder, ".kiro", "steering");
        try
        {
            Directory.CreateDirectory(steeringDir);
        }
        catch(Exception ex)
        {
            return SteeringInstallResult.Failure($"Failed to create steering directory '{steeringDir}': {ex.Message}");
        }

        // Phase 1: remove previously-installed managed files. We identify them by the
        // embedded marker rather than by name, so files renamed or dropped between
        // versions are still cleaned up — and user-authored files are left alone.
        var removed = new List<string>();
        var skippedUserFiles = new List<string>();
        string[] existing;
        try
        {
            existing = Directory.GetFiles(steeringDir, "*.md", SearchOption.TopDirectoryOnly);
        }
        catch(Exception ex)
        {
            return SteeringInstallResult.Failure($"Failed to enumerate existing steering files in '{steeringDir}': {ex.Message}");
        }

        Array.Sort(existing, StringComparer.Ordinal);

        foreach(var path in existing)
        {
            bool isManaged;
            try
            {
                isManaged = File.ReadAllText(path).Contains(ManagedMarker, StringComparison.Ordinal);
            }
            catch(Exception ex)
            {
                return SteeringInstallResult.Failure($"Failed to read existing steering file '{path}': {ex.Message}");
            }

            if(isManaged)
            {
                try
                {
                    File.Delete(path);
                }
                catch(Exception ex)
                {
                    return SteeringInstallResult.Failure($"Failed to remove managed steering file '{path}': {ex.Message}");
                }
                removed.Add(path);
            }
            else
            {
                skippedUserFiles.Add(path);
            }
        }

        // Phase 2: copy the current bundled set in fresh.
        var installed = new List<string>();
        foreach(var source in bundledFiles)
        {
            var destination = Path.Combine(steeringDir, Path.GetFileName(source));
            try
            {
                File.Copy(source, destination, overwrite: true);
            }
            catch(Exception ex)
            {
                return SteeringInstallResult.Failure($"Failed to install steering file '{Path.GetFileName(source)}': {ex.Message}");
            }
            installed.Add(destination);
        }

        return new SteeringInstallResult(true, null, removed, installed, skippedUserFiles);
    }
}