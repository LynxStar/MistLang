using System.Diagnostics;
using System.Text;

namespace Mist.CLI.Tests;

/// <summary>
/// The real CLI, four times over one disposable project: lex, lex again, lex into gen/ inside the
/// project, then a build that names the standard pipeline. The first two runs clean the output the run before wrote, so a
/// run that read that output back as source would name files the clean has deleted, and two sources
/// named Foo.mist would share one set of artifacts. The last two runs do not share an output, so only
/// the marker each lex leaves keeps the next run from reading the other's reconstructed sources.
/// <see cref="Layout.ProjectLayoutTests"/> and <see cref="MistFileDiscoveryTests"/> hold the decisions;
/// only whole invocations show that the commands write the marker and apply the decisions to the
/// directories they clean, which is why this is Tier 5 and why it is the only one. util/Helper.mist
/// sorts after out/, so a run reading its own output reaches a deleted file rather than one it has just
/// rewritten. The project lives in a temp folder rather than under samples/ because the CLI writes into it.
/// The build keeps its progress output because only the real command composes the driver's
/// stages with the CLI's own Stage 0 and summary: one header per stage, 0 to 6, and one summary.
/// </summary>
[Trait("Tier", "5")]
public sealed class RepeatedCliInvocationTests : IDisposable
{
    private readonly string project = Path.Combine(Path.GetTempPath(), $"mist_repeated_cli_{Guid.NewGuid():N}");

    public RepeatedCliInvocationTests()
    {
        Write("mist.toml", "name = \"Repro\"\n");
        Write("Program.mist", """
            using Standard;
            using Repro.util;

            namespace Repro;

            public class Program
            {
                public static void Main(string[] args)
                {
                    Console.WriteLine(new Repro.Alpha.Foo().Describe());
                    Console.WriteLine(new Repro.Beta.Foo().Describe());
                    Console.WriteLine(Helper.Greeting());
                }
            }
            """);
        Write(Path.Combine("Alpha", "Foo.mist"), Foo("alpha"));
        Write(Path.Combine("Beta", "Foo.mist"), Foo("beta"));
        Write(Path.Combine("util", "Helper.mist"), """
            using Standard;

            public class Helper
            {
                public static string Greeting()
                {
                    return "helper";
                }
            }
            """);
    }

    public void Dispose()
    {
        if(Directory.Exists(project))
        {
            Directory.Delete(project, recursive: true);
        }
    }

    [Fact]
    public async Task RepeatedLexesThenStandardBuild_ReadOnlyTheProjectsOwnSources()
    {
        for(var run = 1; run <= 2; run++)
        {
            var lex = await RunMist("build", project, "--mode", "lex", "--quiet");

            Assert.True(lex.ExitCode == 0, $"lex run {run} exited {lex.ExitCode}:\n{lex.Stdout}\n{lex.Stderr}");
            Assert.Equal(4, lex.Stdout.Split('\n').Count(line => line.StartsWith("Lexed ", StringComparison.Ordinal)));
        }

        var lexer = Path.Combine(project, "out", "lexer");
        Assert.Equal(Foo("alpha"), await File.ReadAllTextAsync(Path.Combine(lexer, "Alpha", "Foo.mist")));
        Assert.Equal(Foo("beta"), await File.ReadAllTextAsync(Path.Combine(lexer, "Beta", "Foo.mist")));
        Assert.True(File.Exists(Path.Combine(lexer, "Alpha", "Foo.tokens")));
        Assert.True(File.Exists(Path.Combine(lexer, "Beta", "Foo.tokens")));

        var custom = await RunMist("build", project, "--mode", "lex", "--output", Path.Combine(project, "gen"), "--quiet");

        Assert.True(custom.ExitCode == 0, $"lex into gen exited {custom.ExitCode}:\n{custom.Stdout}\n{custom.Stderr}");
        Assert.Equal(4, custom.Stdout.Split('\n').Count(line => line.StartsWith("Lexed ", StringComparison.Ordinal)));
        Assert.True(File.Exists(Path.Combine(project, "gen", "lexer", ".mist-generated")));

        var build = await RunMist("build", project, "--pipeline", "standard");

        Assert.True(build.ExitCode == 0, $"standard build exited {build.ExitCode}:\n{build.Stdout}\n{build.Stderr}");
        var lines = build.Stdout.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        Assert.Equal(
            ["Stage 0 - Cleaning", "Stage 1 - Loading", "Stage 2 - Lexing", "Stage 3 - Parsing", "Stage 4 - Program Model", "Stage 5 - Analysis", "Stage 6 - Emit"],
            lines.Where(line => line.StartsWith("Stage ", StringComparison.Ordinal)));
        _ = Assert.Single(lines, line => line.StartsWith("Build ", StringComparison.Ordinal));
    }

    private static string Foo(string word)
    {
        return $$"""
            using Standard;

            public class Foo
            {
                public string Describe()
                {
                    return "{{word}}";
                }
            }
            """;
    }

    private void Write(string relativePath, string contents)
    {
        var path = Path.Combine(project, relativePath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunMist(params string[] arguments)
    {
        var quoted = string.Join(" ", arguments.Select(argument => $"\"{argument}\""));
        var startInfo = new ProcessStartInfo("dotnet", $"run --project \"{CliProject()}\" --no-build -v q -- {quoted}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch(OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    private static string CliProject()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while(!File.Exists(Path.Combine(directory.FullName, "Mist.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException("Mist.slnx was not found above the test assembly.");
        }

        return Path.Combine(directory.FullName, "Source", "Mist.CLI", "Mist.CLI.csproj");
    }
}