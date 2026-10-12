using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 5, kept because the signatures it calls live in the C# fixture project, which a sample's mist.toml cannot
/// reference, so the orchestrator cannot make the claim: a ValueTuple a CLR signature hands over or gets back
/// lands each value in the slot the signature said, including every slot past the seventh, which travels inside
/// a Rest. One compile and one run, read in three sections.
/// </summary>
[Trait("Tier", "5")]
public sealed class ClrTupleSignatureRuntimeTests : IDisposable
{
    private const string Program = """
        using Standard;
        using System.Collections.Generic;
        using Mist.Tests.InteropFixtures;

        namespace TupleInterop;

        public class Program
        {
            public static void Main()
            {
                Console.WriteLine("== passed");
                Console.WriteLine(TupleSignatures.DescribePair((7, "seven")));
                Console.WriteLine(TupleSignatures.Area((3, 4)));
                Console.WriteLine(TupleSignatures.Area((Width: 2, Height: 5)));
                (int, int) size = (6, 7);
                Console.WriteLine(TupleSignatures.Area(size));
                Console.WriteLine(TupleSignatures.DescribeLocation(((5, 6), "dock")));
                Console.WriteLine(TupleSignatures.DescribeNonet((1, 2, 3, 4, 5, 6, 7, 8, "nine")));
                Console.WriteLine(TupleSignatures.DescribeSixteen((1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, (15, "fifteen"), "sixteen")));
                List<(string, int)> entries = new List<(string, int)>();
                entries.Add(("ada", 3));
                entries.Add(("grace", 4));
                Console.WriteLine(TupleSignatures.Total(entries));
                (int, int) swapped = (1, 2);
                TupleSignatures.Swap(ref swapped);
                Console.WriteLine($"{swapped.Item1} {swapped.Item2}");

                Console.WriteLine("== returned");
                (int, int) division = TupleSignatures.Divide(17, 5);
                Console.WriteLine($"{division.Item1} {division.Item2}");
                var (quotient, remainder) = TupleSignatures.Divide(23, 4);
                Console.WriteLine($"{quotient} {remainder}");
                (int q, int r) = TupleSignatures.Divide(9, 2);
                Console.WriteLine($"{q} {r}");
                var named = TupleSignatures.Divide(10, 3);
                Console.WriteLine($"{named.Quotient} {named.Remainder}");
                var (number, text) = TupleSignatures.MakePair(8, "eight");
                Console.WriteLine($"{number} {text}");
                var location = TupleSignatures.Locate(1, 2, "home");
                Console.WriteLine($"{location.Label} {location.Origin.X} {location.Origin.Y} {location.Item1.Item2}");
                var partly = TupleSignatures.PartlyNamed(4);
                Console.WriteLine($"{partly.Item1} {partly.Label}");
                Console.WriteLine($"{TupleSignatures.Author.First} {TupleSignatures.Author.Last} {TupleSignatures.Bounds.Max}");
                foreach (var score in TupleSignatures.Scores())
                {
                    Console.WriteLine($"{score.Name}={score.Score}");
                }
                var grid = TupleSignatures.Grid();
                Console.WriteLine(grid[(0, 1)].Name);
                var window = TupleSignatures.Span(true);
                if (window is (int Low, int High) found)
                {
                    Console.WriteLine($"{Low}..{High} {found.Item2}");
                }

                Console.WriteLine("== long");
                var octet = TupleSignatures.Octet(10);
                Console.WriteLine($"{octet.One} {octet.Eight} {octet.Item8}");
                var nonet = TupleSignatures.Nonet("tail");
                Console.WriteLine($"{nonet.A} {nonet.H} {nonet.Item9} {nonet.Tail}");
                var (a, b, c, d, e, f, g, h, i) = nonet;
                Console.WriteLine($"{a + b + c + d + e + f + g + h} {i}");
                (int, int, int, int, int, int, int, int, string) typedNonet = TupleSignatures.Nonet("typed");
                Console.WriteLine(typedNonet.Item9);
                var sixteen = TupleSignatures.Sixteen("last");
                Console.WriteLine($"{sixteen.Item1} {sixteen.Item14} {sixteen.Pair.Inner} {sixteen.Item15.Label} {sixteen.Last} {sixteen.Item16}");
                Console.WriteLine(TupleSignatures.DescribeSixteen(TupleSignatures.Sixteen("again")));
            }
        }
        """;

    private readonly string scratchDir;

    public ClrTupleSignatureRuntimeTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-clrtuple-" + Guid.NewGuid().ToString("N"));
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
    public void TuplesCrossingAClrSignature_LandEveryElementWhereItSaid()
    {
        var output = CompileAndRun();

        Assert.Equal(
            [
                "7:seven",
                "12",
                "10",
                "42",
                "dock@5,6",
                "nine=36",
                "1..14 fifteen sixteen",
                "7",
                "2 1",
            ],
            Section(output, "passed"));

        Assert.Equal(
            [
                "3 2",
                "5 3",
                "4 1",
                "3 1",
                "8 eight",
                "home 1 2 2",
                "4 #4",
                "Ada Lovelace 10",
                "ada=3",
                "grace=5",
                "rook",
                "2..9 9",
            ],
            Section(output, "returned"));

        Assert.Equal(
            [
                "10 17 17",
                "1 8 tail tail",
                "36 tail",
                "typed",
                "1 14 15 fifteen last last",
                "1..14 fifteen again",
            ],
            Section(output, "long"));
    }

    private static IReadOnlyList<string> Section(string[] output, string name)
    {
        var start = Array.IndexOf(output, $"== {name}");

        Assert.True(start >= 0, $"No '{name}' section in:\n{string.Join("\n", output)}");

        return output.Skip(start + 1).TakeWhile(line => !line.StartsWith("== ", StringComparison.Ordinal)).ToList();
    }

    private string[] CompileAndRun()
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, Program);

        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TupleInterop",
            NamespaceRoot: "TupleInterop")
        {
            AssemblyPaths = [typeof(TupleSignatures).Assembly.Location],
        });

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Compilation failed:\n{string.Join("\n", errors)}");
        Assert.NotNull(result.PrimaryOutputArtifactPath);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = result.PrimaryOutputArtifactPath,
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

        Assert.True(process.ExitCode == 0, stderr);

        return stdout.ReplaceLineEndings("\n").TrimEnd().Split('\n');
    }
}