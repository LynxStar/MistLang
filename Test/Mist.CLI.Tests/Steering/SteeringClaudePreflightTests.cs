using Mist.CLI.Steering;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Tests.Steering;

[Trait("Tier", "3")]
public sealed class SteeringClaudePreflightTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("mist_steering_claude_preflight_");

    public void Dispose()
    {
        root.Delete(recursive: true);
    }

    [Fact]
    public void InstallFrom_MalformedClaudeMarkers_RefusesBeforeChangingProjectFiles()
    {
        var sourceRoot = Path.Combine(root.FullName, "bundled");
        var project = Path.Combine(root.FullName, "project");
        var references = Path.Combine(project, ".mist", "agents");
        _ = Directory.CreateDirectory(sourceRoot);
        _ = Directory.CreateDirectory(references);

        File.WriteAllText(Path.Combine(sourceRoot, "agents-block.md"), "Read `.mist/agents/mist-language.md`.\n");
        File.WriteAllText(Path.Combine(sourceRoot, "mist-language.md"), "<!-- MIST-MANAGED-STEERING -->\nFresh reference\n");

        var agentsPath = Path.Combine(project, "AGENTS.md");
        var claudePath = Path.Combine(project, "CLAUDE.md");
        var stalePath = Path.Combine(references, "mist-retired.md");
        var agents = "# Project rules\r\n"u8.ToArray();
        var claude = "# Claude rules\n<!-- BEGIN MIST-MANAGED -->\nUnclosed block\n"u8.ToArray();
        var stale = "<!-- MIST-MANAGED-STEERING -->\r\nRetired reference\r\n"u8.ToArray();
        File.WriteAllBytes(agentsPath, agents);
        File.WriteAllBytes(claudePath, claude);
        File.WriteAllBytes(stalePath, stale);

        var result = SteeringInstaller.InstallFrom(project, sourceRoot);

        Assert.False(result.Succeeded);
        var failure = Assert.IsType<Admission>(result.FailureDiagnostic).Occurrence;
        Assert.Same(CommandDiagnostics.SteeringMarkersMalformed, failure.Descriptor);
        Assert.Equal(new FileOnly(claudePath), failure.Location);
        Assert.Equal(new PathValue(claudePath), failure.Arguments[0]);
        var reason = Assert.IsType<NameValue>(failure.Arguments[1]);
        Assert.Contains("1 BEGIN and 0 END", reason.Name, StringComparison.Ordinal);

        Assert.Equal(agents, File.ReadAllBytes(agentsPath));
        Assert.Equal(claude, File.ReadAllBytes(claudePath));
        Assert.Equal(stale, File.ReadAllBytes(stalePath));
        Assert.False(File.Exists(Path.Combine(references, "mist-language.md")));
    }
}