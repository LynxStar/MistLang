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
/// Tier 4, the boundary from Standard's declarations to the analysis phase: purity Requirement 9.3 declares
/// List as an intrinsic with a pure constructor, local readonly readers and local mutators, and the spec's
/// own example programs are held to what those promises admit -- a pure scope owning a list it built, a pure
/// scope reading a frozen list, a constructorless type holding a list -- and to what they refuse. The probe
/// shells carry no Standard members, so this is compiled through the driver over the real Standard, and
/// run once to show the intrinsic reaches the CLR list.
/// </summary>
public sealed class StandardListPurityTests : IDisposable
{
    private const string CallsImpure = "MIST_PA_PURE_SCOPE_CALLS_IMPURE";
    private const string Writes = "MIST_PA_PURE_SCOPE_WRITES";

    private const string Admitted = """
        using Standard;

        namespace Listed;

        public class Holder
        {
            public List<int> Items = new List<int>();
        }

        public class Program
        {
            public static pure int Seeded()
            {
                owned var l = new List<int>();
                l.Add(1);
                return l.Count;
            }

            public static pure int Size(frozen List<int> xs)
            {
                return xs.Count;
            }

            public static void Main()
            {
                Console.WriteLine(Seeded());
                owned var xs = new List<int>();
                xs.Add(1);
                xs.Add(2);
                Console.WriteLine(Size(xs));
                var h = new Holder();
                h.Items.Add(3);
                Console.WriteLine(h.Items.Count);
            }
        }
        """;

    private const string ReadsAnUnrestrictedList = """
        using Standard;

        namespace Listed;

        public class Program
        {
            public static pure int Bad(List<int> xs)
            {
                return xs.Count;
            }

            public static void Main()
            {
                owned var xs = new List<int>();
                Console.WriteLine(Bad(xs));
            }
        }
        """;

    private const string WritesAFrozenList = """
        using Standard;

        namespace Listed;

        public class Program
        {
            public static pure int Poke(frozen List<int> xs)
            {
                xs[0] = 1;
                return 0;
            }

            public static void Main()
            {
                owned var xs = new List<int>();
                xs.Add(0);
                Console.WriteLine(Poke(xs));
            }
        }
        """;

    private readonly string scratchDir;

    public StandardListPurityTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-standard-list-purity-" + Guid.NewGuid().ToString("N"));
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
    public void APureScopeOwningOrFreezingAList_AndAConstructorlessTypeHoldingOne_CompileAndRun()
    {
        var result = Compile(Admitted, "admitted");

        Assert.Empty(Errors(result));
        Assert.NotNull(result.PrimaryOutputArtifactPath);
        Assert.Equal("1\n2\n1", Run(result.PrimaryOutputArtifactPath));
    }

    [Fact]
    public void APureScopeReadingAnUnrestrictedListsCount_IsRefusedForWantingAnOwnedOrFrozenReceiver()
    {
        var error = Assert.Single(Errors(Compile(ReadsAnUnrestrictedList, "unrestricted")));

        Assert.Equal(CallsImpure, error.Id.Value);
        Assert.Contains("Count", error.Message, StringComparison.Ordinal);
        Assert.Contains("local readonly", error.Message, StringComparison.Ordinal);
        Assert.Contains("owned or frozen receiver", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APureScopeWritingAnElementOfAFrozenList_IsRefused()
    {
        var error = Assert.Single(Errors(Compile(WritesAFrozenList, "frozen")));

        Assert.Equal(Writes, error.Id.Value);
        Assert.Contains("frozen", error.Message, StringComparison.Ordinal);
    }

    private static List<Diagnostic> Errors(CompilerResult result)
    {
        return [.. result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)];
    }

    private CompilerResult Compile(string source, string name)
    {
        var projectDir = Path.Combine(scratchDir, name);
        _ = Directory.CreateDirectory(projectDir);
        var sourcePath = Path.Combine(projectDir, "Program.mist");
        File.WriteAllText(sourcePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        return driver.Compile(new CompilerOptions(
            ProjectFolder: projectDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(projectDir, "out"),
            BinDirectory: Path.Combine(projectDir, "bin"),
            ProjectName: "Listed",
            NamespaceRoot: "Listed",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental));
    }

    private static string Run(string artifactPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = artifactPath,
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

        return stdout.Trim().ReplaceLineEndings("\n");
    }
}