using Mist.CLI.Steering;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Steering;

[Trait("Tier", "3")]
public sealed class SteeringInstallerTests : IDisposable
{
    private readonly string root;
    private readonly string sourceRoot;
    private readonly string projectFolder;

    public SteeringInstallerTests()
    {
        root = Path.Combine(Path.GetTempPath(), $"mist_steering_test_{Guid.NewGuid():N}");
        sourceRoot = Path.Combine(root, "bundled");
        projectFolder = Path.Combine(root, "project");
        _ = Directory.CreateDirectory(sourceRoot);
        _ = Directory.CreateDirectory(projectFolder);
        File.WriteAllText(Path.Combine(sourceRoot, SteeringInstaller.BlockTemplateFileName), BlockBody);
    }

    public void Dispose()
    {
        if(Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string BlockBody = "Read `.mist/agents/mist-language.md`.";

    private string SteeringDir => SteeringInstaller.ReferenceDirectory(projectFolder);

    private string AgentsMd => Path.Combine(projectFolder, "AGENTS.md");

    private string ClaudeMd => Path.Combine(projectFolder, "CLAUDE.md");

    private void WriteBundled(string fileName, string body = "content")
    {
        var text = $"<!-- {SteeringInstaller.ManagedMarker} -->\n{body}\n";
        File.WriteAllText(Path.Combine(sourceRoot, fileName), text);
    }

    private void WriteExisting(string fileName, string text)
    {
        _ = Directory.CreateDirectory(SteeringDir);
        File.WriteAllText(Path.Combine(SteeringDir, fileName), text);
    }

    [Fact]
    public void InstallFrom_FreshProject_CreatesDirectoryAndCopiesFiles()
    {
        WriteBundled("mist-language.md");
        WriteBundled("mist-ownership.md");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.True(Directory.Exists(SteeringDir));
        Assert.Equal(2, result.Installed.Count);
        Assert.Empty(result.Removed);
        Assert.Empty(result.SkippedUserFiles);
        Assert.True(File.Exists(Path.Combine(SteeringDir, "mist-language.md")));
        Assert.True(File.Exists(Path.Combine(SteeringDir, "mist-ownership.md")));
    }

    [Fact]
    public void InstallFrom_CopiesBundledContentVerbatim()
    {
        WriteBundled("mist-language.md", body: "the language reference body");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        var installed = File.ReadAllText(Path.Combine(SteeringDir, "mist-language.md"));
        var bundled = File.ReadAllText(Path.Combine(sourceRoot, "mist-language.md"));
        Assert.Equal(bundled, installed);
        Assert.Contains("the language reference body", installed, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallFrom_RunTwice_IsIdempotent()
    {
        WriteBundled("mist-language.md");
        WriteBundled("mist-ownership.md");

        var first = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);
        Assert.True(first.Succeeded);

        var second = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(second.Succeeded);
        // The second run removes the two files the first run installed, then reinstalls them.
        Assert.Equal(2, second.Removed.Count);
        Assert.Equal(2, second.Installed.Count);
        Assert.Empty(second.SkippedUserFiles);

        var present = Directory.GetFiles(SteeringDir, "*.md").Select(p => Path.GetFileName(p)!).OrderBy(n => n).ToArray();
        Assert.Equal(["mist-language.md", "mist-ownership.md"], present);
    }

    [Fact]
    public void InstallFrom_LeavesUserFilesUntouched()
    {
        WriteBundled("mist-language.md");
        WriteExisting("my-rules.md", "# My own steering\nuser content\n");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        _ = Assert.Single(result.SkippedUserFiles);
        Assert.Contains("my-rules.md", result.SkippedUserFiles[0], StringComparison.Ordinal);

        var userFile = Path.Combine(SteeringDir, "my-rules.md");
        Assert.True(File.Exists(userFile));
        Assert.Equal("# My own steering\nuser content\n", File.ReadAllText(userFile));
    }

    [Fact]
    public void InstallFrom_DoesNotRemoveUserFileEvenWhenNameClashesAreAbsent()
    {
        WriteBundled("mist-language.md");
        WriteExisting("notes.md", "no marker here");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Removed);
        Assert.True(File.Exists(Path.Combine(SteeringDir, "notes.md")));
    }

    [Fact]
    public void InstallFrom_UnmarkedUserFileWithBundledName_IsLeftUntouched()
    {
        const string userText = "# My own notes\nuser content, no marker\n";
        WriteBundled("mist-example.md", body: "bundled body");
        WriteBundled("mist-language.md");
        WriteExisting("mist-example.md", userText);

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(userText, File.ReadAllText(Path.Combine(SteeringDir, "mist-example.md")));
        Assert.Empty(result.Removed);
        var skipped = Assert.Single(result.SkippedUserFiles);
        Assert.Equal("mist-example.md", Path.GetFileName(skipped));
        var installed = Assert.Single(result.Installed);
        Assert.Equal("mist-language.md", Path.GetFileName(installed));
        Assert.True(File.Exists(Path.Combine(SteeringDir, "mist-language.md")));
    }

    [Fact]
    public void InstallFrom_UnmarkedUserFileWithBundledName_SurvivesRepeatedRuns()
    {
        const string userText = "my version of the example";
        WriteBundled("mist-example.md");
        WriteBundled("mist-language.md");
        WriteExisting("mist-example.md", userText);

        var first = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);
        var second = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(userText, File.ReadAllText(Path.Combine(SteeringDir, "mist-example.md")));
        Assert.Equal("mist-language.md", Path.GetFileName(Assert.Single(second.Removed)));
        Assert.Equal("mist-language.md", Path.GetFileName(Assert.Single(second.Installed)));
        Assert.Equal("mist-example.md", Path.GetFileName(Assert.Single(second.SkippedUserFiles)));
    }

    [Fact]
    public void InstallFrom_NameClash_StillRefreshesManagedFilesAndKeepsOtherUserFiles()
    {
        WriteExisting("mist-example.md", "user example");
        WriteExisting("mist-language.md", $"<!-- {SteeringInstaller.ManagedMarker} -->\nstale body\n");
        WriteExisting("my-rules.md", "user rules");
        WriteBundled("mist-example.md", body: "bundled example");
        WriteBundled("mist-language.md", body: "fresh body");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Equal("user example", File.ReadAllText(Path.Combine(SteeringDir, "mist-example.md")));
        Assert.Equal("user rules", File.ReadAllText(Path.Combine(SteeringDir, "my-rules.md")));
        Assert.Equal(File.ReadAllText(Path.Combine(sourceRoot, "mist-language.md")), File.ReadAllText(Path.Combine(SteeringDir, "mist-language.md")));
        Assert.Equal("mist-language.md", Path.GetFileName(Assert.Single(result.Removed)));
        Assert.Equal("mist-language.md", Path.GetFileName(Assert.Single(result.Installed)));
        Assert.Equal(["mist-example.md", "my-rules.md"], result.SkippedUserFiles.Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void InstallFrom_RemovesStaleManagedFileNoLongerBundled()
    {
        WriteExisting("mist-old-removed.md", $"<!-- {SteeringInstaller.ManagedMarker} -->\nold\n");
        WriteBundled("mist-language.md");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Contains(result.Removed, p => p.EndsWith("mist-old-removed.md", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(SteeringDir, "mist-old-removed.md")));
        Assert.True(File.Exists(Path.Combine(SteeringDir, "mist-language.md")));
    }

    [Fact]
    public void InstallFrom_OverwritesExistingManagedFileWithNewContent()
    {
        WriteExisting("mist-language.md", $"<!-- {SteeringInstaller.ManagedMarker} -->\nstale body\n");
        WriteBundled("mist-language.md", body: "fresh body");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        var installed = File.ReadAllText(Path.Combine(SteeringDir, "mist-language.md"));
        Assert.Contains("fresh body", installed, StringComparison.Ordinal);
        Assert.DoesNotContain("stale body", installed, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallFrom_MixedDirectory_RemovesManagedKeepsUserReinstallsBundle()
    {
        WriteExisting("mist-language.md", $"<!-- {SteeringInstaller.ManagedMarker} -->\nstale\n");
        WriteExisting("mist-old-removed.md", $"<!-- {SteeringInstaller.ManagedMarker} -->\nstale\n");
        WriteExisting("my-rules.md", "user content");
        WriteBundled("mist-language.md", body: "fresh");
        WriteBundled("mist-advanced-features.md", body: "fresh");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);

        var present = Directory.GetFiles(SteeringDir, "*.md").Select(p => Path.GetFileName(p)!).OrderBy(n => n).ToArray();
        Assert.Equal(["mist-advanced-features.md", "mist-language.md", "my-rules.md"], present);

        Assert.Equal("user content", File.ReadAllText(Path.Combine(SteeringDir, "my-rules.md")));
        Assert.False(File.Exists(Path.Combine(SteeringDir, "mist-old-removed.md")));
    }

    [Fact]
    public void InstallFrom_MissingSourceRoot_ReturnsFailure()
    {
        var missing = Path.Combine(root, "does-not-exist");

        var result = SteeringInstaller.InstallFrom(projectFolder, missing);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Installed);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void InstallFrom_EmptySourceRoot_ReturnsFailure()
    {
        // The constructor leaves sourceRoot holding only the block template, no mist-*.md file.
        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Contains("No bundled steering files", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallFrom_FailureLeavesNoPartialState_WhenSourceMissing()
    {
        WriteExisting("my-rules.md", "user content");
        var missing = Path.Combine(root, "does-not-exist");

        var result = SteeringInstaller.InstallFrom(projectFolder, missing);

        Assert.False(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(SteeringDir, "my-rules.md")));
    }

    [Fact]
    public void InstallFrom_MissingBlockTemplate_ReturnsFailure()
    {
        WriteBundled("mist-language.md");
        File.Delete(Path.Combine(sourceRoot, SteeringInstaller.BlockTemplateFileName));

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.False(result.Succeeded);
        Assert.Contains(SteeringInstaller.BlockTemplateFileName, result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallFrom_NoAgentsMd_CreatesOneHoldingTheBlock()
    {
        WriteBundled("mist-language.md");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(ManagedBlock.Merge(null, BlockBody).Text, File.ReadAllText(AgentsMd));
        Assert.Contains(result.InstructionFiles, f => f.Path == AgentsMd && f.Outcome == ManagedBlockOutcome.Created);
    }

    [Fact]
    public void InstallFrom_ExistingAgentsMd_KeepsTheUserText()
    {
        WriteBundled("mist-language.md");
        File.WriteAllText(AgentsMd, "# Our rules\n");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(ManagedBlock.Merge("# Our rules\n", BlockBody).Text, File.ReadAllText(AgentsMd));
    }

    [Fact]
    public void InstallFrom_BrokenFenceInAgentsMd_FailsBeforeWritingAnything()
    {
        WriteBundled("mist-language.md");
        var broken = "# Our rules\n<!-- BEGIN MIST-MANAGED -->\n";
        File.WriteAllText(AgentsMd, broken);

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.False(result.Succeeded);
        Assert.Contains("AGENTS.md", result.Error!, StringComparison.Ordinal);
        Assert.Equal(broken, File.ReadAllText(AgentsMd));
        Assert.False(Directory.Exists(SteeringDir));
    }

    [Fact]
    public void InstallFrom_NoClaudeMd_DoesNotCreateOne()
    {
        WriteBundled("mist-language.md");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.True(result.ClaudeMdAbsent);
        Assert.False(File.Exists(ClaudeMd));
    }

    [Fact]
    public void InstallFrom_ClaudeMdImportingAgentsMd_IsLeftAlone()
    {
        WriteBundled("mist-language.md");
        File.WriteAllText(ClaudeMd, "@AGENTS.md\n");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Equal("@AGENTS.md\n", File.ReadAllText(ClaudeMd));
        Assert.DoesNotContain(result.InstructionFiles, f => f.Path == ClaudeMd);
    }

    [Fact]
    public void InstallFrom_ClaudeMdNotImportingAgentsMd_GetsTheBlock()
    {
        WriteBundled("mist-language.md");
        File.WriteAllText(ClaudeMd, "# Claude rules\n");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.Equal(ManagedBlock.Merge("# Claude rules\n", BlockBody).Text, File.ReadAllText(ClaudeMd));
    }

    [Fact]
    public void InstallFrom_RemovesManagedFilesFromKiroSteering_KeepsTheUsers()
    {
        var kiro = Path.Combine(projectFolder, ".kiro", "steering");
        _ = Directory.CreateDirectory(kiro);
        File.WriteAllText(Path.Combine(kiro, "mist-language.md"), $"<!-- {SteeringInstaller.ManagedMarker} -->\nold\n");
        File.WriteAllText(Path.Combine(kiro, "my-rules.md"), "user content");
        WriteBundled("mist-language.md");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(Path.Combine(kiro, "mist-language.md")));
        Assert.Equal("user content", File.ReadAllText(Path.Combine(kiro, "my-rules.md")));
        Assert.Empty(result.SkippedUserFiles);
    }

    [Fact]
    public void InstallFrom_FileWhereReferenceDirectoryBelongs_RefusesBeforeWritingInstructions()
    {
        WriteBundled("mist-language.md");
        _ = Directory.CreateDirectory(Path.Combine(projectFolder, ".mist"));
        File.WriteAllText(SteeringDir, "not a directory");

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.False(result.Succeeded);
        var failure = result.FailureDiagnostic!.Value.Occurrence;
        Assert.Same(CommandDiagnostics.SteeringDirectoryCreateFailed, failure.Descriptor);
        Assert.Equal(new FileOnly(SteeringDir), failure.Location);
        Assert.Equal(new PathValue(SteeringDir), failure.Arguments[0]);
        Assert.Equal("not a directory", File.ReadAllText(SteeringDir));
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void InstallFrom_DirectoryAtBundledDestination_RefusesBeforeWritingInstructions()
    {
        WriteBundled("mist-language.md");
        var destination = Path.Combine(SteeringDir, "mist-language.md");
        _ = Directory.CreateDirectory(destination);

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.False(result.Succeeded);
        var failure = result.FailureDiagnostic!.Value.Occurrence;
        Assert.Same(CommandDiagnostics.SteeringFileInstallFailed, failure.Descriptor);
        Assert.Equal(new FileOnly(destination), failure.Location);
        Assert.Equal(new NameValue("mist-language.md"), failure.Arguments[0]);
        Assert.True(Directory.Exists(destination));
        Assert.False(File.Exists(AgentsMd));
    }

    [Fact]
    public void InstallFrom_DirectoryAtAgentsMd_RefusesTheInstructionWrite()
    {
        WriteBundled("mist-language.md");
        _ = Directory.CreateDirectory(AgentsMd);

        var result = SteeringInstaller.InstallFrom(projectFolder, sourceRoot);

        Assert.False(result.Succeeded);
        var failure = result.FailureDiagnostic!.Value.Occurrence;
        Assert.Same(CommandDiagnostics.SteeringInstructionsWriteFailed, failure.Descriptor);
        Assert.Equal(new FileOnly(AgentsMd), failure.Location);
        Assert.Equal(new PathValue(AgentsMd), failure.Arguments[0]);
        Assert.True(Directory.Exists(AgentsMd));
    }
}