using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 5, one compile and one run: a Mist conformer of a native interface with default members, which
/// the orchestrator cannot claim because its interfaces live in the C# interop fixture assembly no sample
/// can reference. Selection binds a call on the conformer to the interface's member, but C# gives a class
/// none of its interfaces' members, so only running the emitted program shows the direct call reaches the
/// default the interface call does, the most specific restatement included, and that a conformer's own
/// member wins both ways.
/// </summary>
[Trait("Tier", "5")]
public sealed class NativeDefaultInterfaceRuntimeTests(NativeDefaultInterfaceRuntimeFixture fixture)
    : IClassFixture<NativeDefaultInterfaceRuntimeFixture>
{
    [Fact]
    public void ADefaultAnAnswerAndARestatement_RunAlikeDirectlyAndThroughTheInterface()
    {
        Assert.Equal(
            ["hello plain", "hello plain", "hello plain", "hello plain/hello plain", "hello plain/hello plain", "hello polite", "hello polite"],
            fixture.Section("default"));
        Assert.Equal(["custom loud", "custom loud", "strict answer", "strict answer"], fixture.Section("answered"));
        Assert.Equal(["HELLO shouter", "HELLO shouter"], fixture.Section("restated"));
    }
}

public sealed class NativeDefaultInterfaceRuntimeFixture : IDisposable
{
    private const string Program = """
        using Standard;
        using Mist.Tests.InteropFixtures;

        namespace DefaultInterop;

        public abstract IPolite : IGreeter
        {
        }

        public class Plain : IGreeter
        {
            public string Name() { return "plain"; }

            public string Twice() { return Greet() + "/" + this.Greet(); }

            public string Grouped()
            {
                func<string> greet = Greet;
                return greet();
            }
        }

        public class Polite : IPolite
        {
            public string Name() { return "polite"; }
        }

        public class Loud : IGreeter
        {
            public string Name() { return "loud"; }

            public string Greet() { return "custom " + Name(); }
        }

        public class Strict : IStrictGreeter
        {
            public string Name() { return "strict"; }

            public string Greet() { return "strict answer"; }
        }

        public class Shouter : IShoutingGreeter
        {
            public string Name() { return "shouter"; }
        }

        public class Program
        {
            public static void Main()
            {
                Console.WriteLine("== default");
                var plain = new Plain();
                Console.WriteLine(plain.Greet());
                Console.WriteLine(Greeters.Through(plain));
                IGreeter greeter = plain;
                Console.WriteLine(greeter.Greet());
                Console.WriteLine(plain.Twice());
                func<string> grouped = plain.Greet;
                Console.WriteLine(grouped() + "/" + plain.Grouped());
                var polite = new Polite();
                Console.WriteLine(polite.Greet());
                Console.WriteLine(Greeters.Through(polite));

                Console.WriteLine("== answered");
                var loud = new Loud();
                Console.WriteLine(loud.Greet());
                Console.WriteLine(Greeters.Through(loud));
                var strict = new Strict();
                Console.WriteLine(strict.Greet());
                Console.WriteLine(Greeters.Through(strict));

                Console.WriteLine("== restated");
                var shouter = new Shouter();
                Console.WriteLine(shouter.Greet());
                Console.WriteLine(Greeters.Through(shouter));
            }
        }
        """;

    private readonly string scratchDir;
    private readonly string[] output;

    public NativeDefaultInterfaceRuntimeFixture()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-nativedefault-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);

        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, Program);

        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "DefaultInterop",
            NamespaceRoot: "DefaultInterop")
        {
            AssemblyPaths = [typeof(IGreeter).Assembly.Location],
        });

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Number.Value}: {d.Message}")
            .ToList();

        output = errors.Count == 0 && result.PrimaryOutputArtifactPath is { } artifact
            ? Run(artifact)
            : ["compile failed:", .. errors];
    }

    public IReadOnlyList<string> Section(string name)
    {
        var start = Array.IndexOf(output, $"== {name}");

        Assert.True(start >= 0, $"No '{name}' section in:\n{string.Join("\n", output)}");

        return output.Skip(start + 1).TakeWhile(line => !line.StartsWith("== ", StringComparison.Ordinal)).ToList();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(scratchDir, recursive: true);
        }
        catch(IOException)
        {
        }
    }

    private static string[] Run(string artifact)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = artifact,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        _ = process.WaitForExit(60000);

        return [.. stdout.ReplaceLineEndings("\n").TrimEnd().Split('\n'), .. stderr.Length == 0 ? [] : new[] { "stderr:", stderr }];
    }
}