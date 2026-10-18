// Structural guard over the fix for the suite-wide flake recorded in Carbonek as ARCH-135/ARCH-136. Asserted on the
// SOURCE rather than on behaviour, because the thing it protects is unobservable at runtime: what the
// fix removes is a race, so a run that passes proves nothing and a regression would reappear as
// occasional unrelated failures in whichever tests happened to be reading the standard library.

namespace Mist.Tests.Driver;

/// <summary>
/// No test may trigger a build of the Mist CLI. Doing so starts a SECOND MSBuild inside the one
/// running the suite, which re-runs Mist.CLI's Content copy of Source/Standard into the directory the
/// compiler it launches reads through AppContext.BaseDirectory -- rewriting the standard library out
/// from under a concurrently starting process.
/// <para>
/// Both halves are checked because neither works alone. Without the no-build flag the test builds the
/// CLI itself; without the build-order reference nothing guarantees a CLI exists for it to run.
/// </para>
/// </summary>
public sealed class NestedBuildBanTest
{
    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find repository root (Mist.slnx) from AppContext.BaseDirectory.");
    }

    [Fact]
    public void NoTestSourceSpawnsDotnetRunWithoutNoBuild()
    {
        var testRoot = Path.Combine(FindRepoRoot(), "Test");
        var offenders = new List<string>();

        foreach(var file in Directory.EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories))
        {
            // This file is skipped because it spells the pattern it looks for, and matching itself
            // would make the guard permanently red rather than red on a regression.
            if(file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.GetFileName(file) == "NestedBuildBanTest.cs")
            {
                continue;
            }

            // Per LINE, not per file. A file-wide search matches the prose explaining why the flag is
            // there, so removing it from the command would leave the test green on its own comment.
            foreach(var line in File.ReadLines(file))
            {
                if(line.Contains("run --project", StringComparison.Ordinal)
                    && !line.Contains("--no-build", StringComparison.Ordinal))
                {
                    offenders.Add($"{file}: {line.Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A test spawns `dotnet run --project` without --no-build, which builds the CLI from inside "
                + "the suite and rewrites Source/Standard under a running compiler. See "
                + "Carbonek ARCH-135/ARCH-136:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheTestProjectCarriesTheCliBuildOrderReference()
    {
        var project = File.ReadAllText(
            Path.Combine(FindRepoRoot(), "Test", "Mist.Tests", "Mist.Tests.csproj"));

        Assert.Contains("Mist.CLI.csproj", project, StringComparison.Ordinal);
        Assert.Contains("ReferenceOutputAssembly=\"false\"", project, StringComparison.Ordinal);
    }
}