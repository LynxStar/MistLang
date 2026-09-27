using System.Diagnostics;
using System.Text;

namespace Mist.CLI.Tests;

/// <summary>
/// The real CLI, three times over one disposable project: lex, lex again, then an experimental build.
/// Each run cleans the output the run before it wrote, so a run that read that output back as source
/// would name files the clean has deleted, and two sources named Foo.mist would share one set of
/// artifacts. <see cref="Layout.ProjectLayoutTests"/> and <see cref="MistFileDiscoveryTests"/> hold the
/// decisions; only whole invocations show that the commands apply them to the directories they clean,
/// which is why this is Tier 5 and why it is the only one. util/Helper.mist sorts after out/, so a run
/// reading its own output reaches a deleted file rather than one it has just rewritten. The project
/// lives in a temp folder rather than under samples/ because the CLI writes into it.
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
    public async Task LexTwiceThenExperimentalBuild_ReadsOnlyTheProjectsOwnSources()
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

        var build = await RunMist("build", project, "--pipeline", "experimental", "--quiet");

        Assert.True(build.ExitCode == 0, $"experimental build exited {build.ExitCode}:\n{build.Stdout}\n{build.Stderr}");
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