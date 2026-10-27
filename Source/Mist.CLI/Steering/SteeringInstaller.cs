using Mist.Common.Diagnostics;

namespace Mist.CLI.Steering;

/// <summary>
/// Result of a steering installation run.
/// </summary>
/// <param name="Succeeded">Whether installation completed successfully.</param>
/// <param name="Removed">Paths of previously-installed managed files that were deleted.</param>
/// <param name="Installed">Paths of files written during this run.</param>
/// <param name="SkippedUserFiles">
/// Paths of files in the reference directory that were left untouched because
/// they are not Mist-managed (the user's own files).
/// </param>
public sealed record SteeringInstallResult(
    bool Succeeded,
    Admission? FailureDiagnostic,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Installed,
    IReadOnlyList<string> SkippedUserFiles,
    IReadOnlyList<InstructionFileUpdate> InstructionFiles,
    bool ClaudeMdAbsent)
{
    private static readonly IReadOnlyList<string> Empty = [];

    public string? Error => FailureDiagnostic?.Occurrence.Message;

    /// <summary>Creates a failure result carrying the supplied <paramref name="error"/> message.</summary>
    public static SteeringInstallResult Failure(Admission error)
    {
        return new(false, error, Empty, Empty, Empty, [], false);
    }
}

public sealed record InstructionFileUpdate(string Path, ManagedBlockOutcome Outcome);

/// <summary>
/// Installs the Mist agent references bundled alongside the compiler binary into a
/// project's <c>.mist/agents/</c> directory, and points the project's AGENTS.md at them.
///
/// <para>
/// The installation is idempotent and user-safe. Every bundled reference file carries a
/// sentinel marker (<see cref="ManagedMarker"/>). On each run the installer:
/// </para>
/// <list type="number">
///   <item>Merges the bundled block into AGENTS.md, and into CLAUDE.md when one exists that does
///   not import AGENTS.md, through <see cref="ManagedBlock"/>. A broken fence in either fails the
///   run before anything is written.</item>
///   <item>Deletes every <c>*.md</c> in the reference directory, and in the <c>.kiro/steering/</c>
///   directory earlier versions installed into, that contains the marker.</item>
///   <item>Copies the current bundled set in fresh.</item>
/// </list>
/// <para>
/// Files that do not contain the marker — the user's own — are never touched. Running the
/// command repeatedly converges to exactly the bundled set plus whatever the user authored.
/// </para>
/// </summary>
public static class SteeringInstaller
{
    /// <summary>
    /// Sentinel substring that identifies a Mist-managed reference file. Must match the
    /// marker embedded in the distributed <c>mist-*.md</c> files. Any file containing
    /// this substring is considered installer-owned and is eligible for deletion.
    /// </summary>
    public const string ManagedMarker = "MIST-MANAGED-STEERING";

    public const string BlockTemplateFileName = "agents-block.md";

    public static string ReferenceDirectory(string projectFolder)
    {
        return Path.Combine(projectFolder, ".mist", "agents");
    }

    /// <summary>
    /// Installs the bundled reference files and block into <paramref name="projectFolder"/>.
    /// </summary>
    /// <param name="projectFolder">The project root. The reference directory is created if absent.</param>
    /// <returns>
    /// A result describing what was removed, installed, merged and skipped. On failure,
    /// <see cref="SteeringInstallResult.Succeeded"/> is <c>false</c> and
    /// <see cref="SteeringInstallResult.Error"/> carries the reason.
    /// </returns>
    public static SteeringInstallResult Install(string projectFolder)
    {
        return InstallFrom(projectFolder, Path.Combine(AppContext.BaseDirectory, "Steering"));
    }

    /// <summary>
    /// Installs from an explicit <paramref name="sourceRoot"/> into <paramref name="projectFolder"/>.
    /// The production entry point <see cref="Install(string)"/> anchors the source root at
    /// <see cref="AppContext.BaseDirectory"/>; this overload lets callers (and tests)
    /// supply an arbitrary bundled set.
    /// </summary>
    /// <param name="projectFolder">The project root. The reference directory is created if absent.</param>
    /// <param name="sourceRoot">Directory containing the bundled <c>mist-*.md</c> files and <see cref="BlockTemplateFileName"/>.</param>
    /// <returns>
    /// A result describing what was removed, installed, merged and skipped. On failure,
    /// <see cref="SteeringInstallResult.Succeeded"/> is <c>false</c> and
    /// <see cref="SteeringInstallResult.Error"/> carries the reason.
    /// </returns>
    public static SteeringInstallResult InstallFrom(string projectFolder, string sourceRoot)
    {
        string[] bundledFiles;
        try
        {
            bundledFiles = Directory.Exists(sourceRoot)
                ? Directory.GetFiles(sourceRoot, "mist-*.md", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringBundleReadFailed, new FileOnly(sourceRoot), new PathValue(sourceRoot), new TextValue(ex.Message)));
        }

        if(bundledFiles.Length == 0)
        {
            return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringBundleMissing, new FileOnly(sourceRoot), new PathValue(sourceRoot)));
        }

        Array.Sort(bundledFiles, StringComparer.Ordinal);

        var templatePath = Path.Combine(sourceRoot, BlockTemplateFileName);
        string blockBody;
        try
        {
            blockBody = File.ReadAllText(templatePath);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringBlockReadFailed, new FileOnly(templatePath), new PathValue(templatePath), new TextValue(ex.Message)));
        }

        var agentsPath = Path.Combine(projectFolder, "AGENTS.md");
        var claudePath = Path.Combine(projectFolder, "CLAUDE.md");
        var merges = new List<(string Path, ManagedBlockMerge Merge)>();
        bool claudeMdAbsent;
        try
        {
            merges.Add((agentsPath, ManagedBlock.Merge(File.Exists(agentsPath) ? File.ReadAllText(agentsPath) : null, blockBody)));

            claudeMdAbsent = !File.Exists(claudePath);
            if(!claudeMdAbsent)
            {
                var claudeText = File.ReadAllText(claudePath);
                if(!ManagedBlock.ImportsAgentsMd(claudeText))
                {
                    merges.Add((claudePath, ManagedBlock.Merge(claudeText, blockBody)));
                }
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringInstructionsReadFailed, new ProjectLocation(Path.Combine(projectFolder, "mist.toml"), null), new TextValue(ex.Message)));
        }

        foreach(var (path, merge) in merges)
        {
            if(merge.Outcome == ManagedBlockOutcome.Malformed)
            {
                return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringMarkersMalformed, new FileOnly(path), new PathValue(path), new NameValue(merge.Error!)));
            }
        }

        var referenceDir = ReferenceDirectory(projectFolder);
        try
        {
            _ = Directory.CreateDirectory(referenceDir);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringDirectoryCreateFailed, new FileOnly(referenceDir), new PathValue(referenceDir), new TextValue(ex.Message)));
        }

        var removed = new List<string>();
        var skippedUserFiles = new List<string>();
        var error = RemoveManagedFiles(referenceDir, removed, skippedUserFiles);
        if(error is not null)
        {
            return SteeringInstallResult.Failure(error.Value);
        }

        var legacyDir = Path.Combine(projectFolder, ".kiro", "steering");
        if(Directory.Exists(legacyDir))
        {
            error = RemoveManagedFiles(legacyDir, removed, []);
            if(error is not null)
            {
                return SteeringInstallResult.Failure(error.Value);
            }
        }

        var installed = new List<string>();
        foreach(var source in bundledFiles)
        {
            var destination = Path.Combine(referenceDir, Path.GetFileName(source));
            try
            {
                File.Copy(source, destination, overwrite: true);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringFileInstallFailed, new FileOnly(destination), new NameValue(Path.GetFileName(source)), new TextValue(ex.Message)));
            }

            installed.Add(destination);
        }

        var instructionFiles = new List<InstructionFileUpdate>();
        foreach(var (path, merge) in merges)
        {
            if(merge.Outcome != ManagedBlockOutcome.Unchanged)
            {
                try
                {
                    File.WriteAllText(path, merge.Text);
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    return SteeringInstallResult.Failure(MakeFailure(CommandDiagnostics.SteeringInstructionsWriteFailed, new FileOnly(path), new PathValue(path), new TextValue(ex.Message)));
                }
            }

            instructionFiles.Add(new InstructionFileUpdate(path, merge.Outcome));
        }

        return new SteeringInstallResult(true, null, removed, installed, skippedUserFiles, instructionFiles, claudeMdAbsent);
    }

    private static Admission MakeFailure(MistDiagnostic descriptor, Location location, params ReadOnlySpan<SlotValue> arguments)
    {
        return DiagnosticFactory.Create(descriptor, location, PipelineStage.Driver, arguments);
    }

    private static Admission? RemoveManagedFiles(string directory, List<string> removed, List<string> skippedUserFiles)
    {
        // We identify managed files by the embedded marker rather than by name, so files renamed
        // or dropped between versions are still cleaned up — and user-authored files are left alone.
        string[] existing;
        try
        {
            existing = Directory.GetFiles(directory, "*.md", SearchOption.TopDirectoryOnly);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            return MakeFailure(CommandDiagnostics.SteeringFilesEnumerateFailed, new FileOnly(directory), new PathValue(directory), new TextValue(ex.Message));
        }

        Array.Sort(existing, StringComparer.Ordinal);

        foreach(var path in existing)
        {
            bool isManaged;
            try
            {
                isManaged = File.ReadAllText(path).Contains(ManagedMarker, StringComparison.Ordinal);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                return MakeFailure(CommandDiagnostics.SteeringFileReadFailed, new FileOnly(path), new PathValue(path), new TextValue(ex.Message));
            }

            if(isManaged)
            {
                try
                {
                    File.Delete(path);
                }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
                {
                    return MakeFailure(CommandDiagnostics.SteeringFileRemoveFailed, new FileOnly(path), new PathValue(path), new TextValue(ex.Message));
                }

                removed.Add(path);
            }
            else
            {
                skippedUserFiles.Add(path);
            }
        }

        return null;
    }
}