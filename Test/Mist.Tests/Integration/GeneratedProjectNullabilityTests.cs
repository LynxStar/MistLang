using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 5, kept because the orchestrator compiles through the CLI and reads none of the build's warnings, while
/// the in-process compile reports errors only and reads none of the generated csproj: only MSBuild over that
/// project shows what its nullable context makes of the emitted spellings. One build, of a program putting an
/// optional reference in every declaring position, a union carrier and the invariant-text runtime into generated
/// .g.cs files, sized constituents and a wide tuple test.
/// </summary>
[Trait("Tier", "5")]
public sealed partial class GeneratedProjectNullabilityTests : IDisposable
{
    private const string Source = """
        using Standard;

        namespace Nullability;

        public class Node
        {
            public string Name;

            public Node(string name) { Name = name; }
        }

        public class Box<T> where T : class
        {
            public T? Held;

            public Box() { }

            public T? Get() { return Held; }
        }

        public class Holder
        {
            private string? note;

            public string? Label { get; set; }

            public Holder() { }

            public string? Find(string? key)
            {
                if(key is string found) { return found; }
                return note;
            }
        }

        public class Program
        {
            public static void Main()
            {
                var holder = new Holder();
                Node? node = void;
                int[]? numbers = void;
                func<int>? make = void;
                var box = new Box<Node>();
                int | string carried = "text";
                int[3] | int[5] sized = new int[5];
                unknown wide = (1, 2, 3, 4, 5, 6, 7, new int[3], "x");
                Console.WriteLine(holder.Find(void) ?? "none");
                Console.WriteLine(holder.Label ?? "unlabelled");
                Console.WriteLine(node?.Name ?? "no node");
                Console.WriteLine("count " + (numbers?.Length ?? 0));
                Console.WriteLine($"boxed {wide}");
                Console.WriteLine(make is void);
                Console.WriteLine(box.Get()?.Name ?? "empty");
                Console.WriteLine(carried switch { int n => "int", string s => s });
                if(sized is int[5] five) { Console.WriteLine("five " + five.Length); }
                if(wide is (int, int, int, int, int, int, int, int[3], string) nine) { Console.WriteLine(nine.Item9); }
            }
        }
        """;

    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), "mist-nullability-" + Guid.NewGuid().ToString("N"));

    public GeneratedProjectNullabilityTests()
    {
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch(IOException) { }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EmitterToMSBuild_TheGeneratedProjectRaisesNoNullableWarning()
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, Source);
        var output = Path.Combine(scratchDir, "out");

        var result = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser()).Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: output,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Nullability",
            NamespaceRoot: "Nullability"));

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Number.Value}: {d.Message}");
        Assert.True(!errors.Any(), string.Join("\n", errors));

        var (exitCode, log) = Build(Path.Combine(output, "Nullability.csproj"));
        var nullable = NullableWarning().Matches(log).Select(match => match.Value).Distinct().ToList();

        Assert.True(exitCode == 0, log);
        Assert.True(nullable.Count == 0, string.Join("\n", nullable));
    }

    private static (int ExitCode, string Log) Build(string project)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach(var argument in new[] { "build", project, "-nologo", "-nodeReuse:false", "-p:UseSharedCompilation=false" })
        {
            start.ArgumentList.Add(argument);
        }

        foreach(var inherited in start.Environment.Keys.Where(key => key.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _ = start.Environment.Remove(inherited);
        }

        using var process = Process.Start(start);
        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(300000), "dotnet build did not finish");

        return (process.ExitCode, stdout.Result + stderr.Result);
    }

    [GeneratedRegex(@"[^\s]+\(\d+,\d+\): warning CS86\d\d: [^\[\r\n]*")]
    private static partial Regex NullableWarning();
}