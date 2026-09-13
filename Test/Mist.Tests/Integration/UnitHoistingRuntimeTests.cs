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
/// Tier 4, the boundary from emitted C# to the runtime: a call yielding nothing whose value is taken runs ahead
/// of its statement, and the trail each call leaves is the order the program ran them in -- which the emitted
/// text shows only as a claim about C#'s own evaluation order.
/// </summary>
public sealed class UnitHoistingRuntimeTests : IDisposable
{
    private const string LeavesATrail = """
        using Standard;

        namespace Hoisted;

        public class Log
        {
            public static string Trail = "";

            public static void F()
            {
                Trail = Trail + "f";
            }

            public static int H()
            {
                Trail = Trail + "h";
                return 1;
            }

            public static int K()
            {
                Trail = Trail + "k";
                return 2;
            }

            public static Log Make()
            {
                Trail = Trail + "m";
                return new Log();
            }

            public string Take(void unit, int value)
            {
                return $"take({unit},{value})";
            }

            public static string Read(string seen, void unit)
            {
                return $"read({seen},{unit})";
            }

            public static string G3(int first, void unit, int last)
            {
                return $"g3({first},{unit},{last})";
            }
        }

        public class Program
        {
            public static void Main()
            {
                var ordered = Log.G3(Log.K(), Log.F(), Log.H());
                var first = Log.Trail;

                Log.Trail = "x";
                var read = Log.Read(Log.Trail, Log.F());
                var second = Log.Trail;

                Log.Trail = "";
                var taken = Log.Make().Take(Log.F(), Log.H());
                var hole = $"{Log.F()}{Log.K()}";

                Console.WriteLine($"{ordered} {first} | {read} {second} | {taken} {hole} {Log.Trail}");
            }
        }
        """;

    private readonly string scratchDir;

    public UnitHoistingRuntimeTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-unit-hoisting-" + Guid.NewGuid().ToString("N"));
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
    public void AHoistedCall_RunsAfterEveryOperandBeforeItAndBeforeEveryOperandAfterIt()
    {
        Assert.Equal("g3(2,Void,1) kfh | read(x,Void) xf | take(Void,1) Void2 mfhfk", CompileAndRun(LeavesATrail));
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
            ProjectName: "Hoisted",
            NamespaceRoot: "Hoisted",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental));

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

        return stdout.Trim();
    }
}