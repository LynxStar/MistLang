using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 4, the boundary from a refinement declaration to compiled C#: that a refinement is spelled
/// as the type it refines everywhere except a TEST, where the predicate itself has to survive.
/// <para>
/// RUN RATHER THAN INSPECTED, because the failure this is guarding against is not a malformed file.
/// A refinement erased at a test emits `x is int`, which compiles, is true of every int, and turns
/// a runtime check into a constant -- a program that answers `true` where it should answer `false`.
/// Only the printed answer separates a real predicate from an erased one, which is why every case
/// here is chosen so that erasure would change the output rather than break the build.
/// </para>
/// </summary>
public sealed class RefinementEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public RefinementEmissionTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-refinement-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Both answers from one predicate. A value that satisfies it and one that does not, so a test
    /// that always succeeds and a test that always fails are each ruled out by a different digit.
    /// </summary>
    private const string TestsARelationalRefinement = """
        using Standard;

        namespace Refine;

        type AdultAge = int >= 18;

        public class Program
        {
            public static void Main()
            {
                Console.WriteLine($"{Check(25)}|{Check(10)}|{Check(18)}");
            }

            public static int Check(int value)
            {
                if (value is AdultAge ok)
                {
                    return ok;
                }

                return -1;
            }
        }
        """;

    /// <summary>
    /// A property pattern over a readonly type, which is the other predicate shape a refinement may
    /// take -- and the one whose base test is a real type test rather than a tautology.
    /// </summary>
    private const string TestsAPropertyRefinement = """
        using Standard;

        namespace Refine;

        public readonly class Interval
        {
            public int Start;
            public int End;

            public Interval(int start, int end)
            {
                this.Start = start;
                this.End = end;
            }
        }

        type ValidInterval = Interval { Start: >= 0 };

        public class Program
        {
            public static void Main()
            {
                Console.WriteLine($"{Check(new Interval(10, 50))}|{Check(new Interval(-5, 10))}");
            }

            public static int Check(Interval candidate)
            {
                if (candidate is ValidInterval ok)
                {
                    return ok.End;
                }

                return -1;
            }
        }
        """;

    /// <summary>
    /// The erasure half: a refinement in a field, a parameter and a return type is the base type in
    /// C#, and a program using all three runs unchanged.
    /// </summary>
    private const string CarriesARefinementThroughSignatures = """
        using Standard;

        namespace Refine;

        type Percentage = int >= 0 and <= 100;

        public class Gauge
        {
            public Percentage Level;

            public Gauge(Percentage level)
            {
                this.Level = level;
            }
        }

        public class Program
        {
            public static int Double(Percentage p)
            {
                return p + p;
            }

            public static Percentage Full()
            {
                return 100;
            }

            public static void Main()
            {
                var g = new Gauge(40);
                Console.WriteLine($"{g.Level}|{Double(30)}|{Full()}");
            }
        }
        """;

    [Fact]
    public void ARelationalRefinementIsTestedAtRuntime()
    {
        Assert.Equal("25|-1|18", CompileAndRun(TestsARelationalRefinement));
    }

    [Fact]
    public void APropertyRefinementIsTestedAtRuntime()
    {
        Assert.Equal("50|-1", CompileAndRun(TestsAPropertyRefinement));
    }

    [Fact]
    public void ARefinementInASignatureIsSpelledAsItsBase()
    {
        Assert.Equal("40|60|100", CompileAndRun(CarriesARefinementThroughSignatures));
    }

    private string CompileAndRun(string source)
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Refine",
            NamespaceRoot: "Refine",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Refinement emission failed:\n{string.Join("\n", errors)}");
        Assert.NotNull(result.PrimaryOutputArtifactPath);

        return Run(result.PrimaryOutputArtifactPath);
    }

    private static string Run(string artifact)
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
        _ = process.WaitForExit(60000);

        return stdout.Trim();
    }
}