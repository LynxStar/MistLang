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
/// Tier 4, two lowerings with no C# syntax of their own: a <c>with</c> block, whose value is
/// dropped on the way out however the block is left, and a generated array, whose elements come
/// from a factory rather than from zero-filling.
/// <para>
/// THE EARLY RETURN IS THE CASE THAT SEPARATES IMPLEMENTATIONS. Emitting the drop as a plain
/// statement after the body is right for a block that falls off its end, and silently wrong for one
/// that returns -- and the ordinary case cannot tell the two apart, because both print the same
/// thing. Run rather than inspected for the same reason: both spellings compile.
/// </para>
/// </summary>
public sealed class WithBlockEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public WithBlockEmissionTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-withblock-" + Guid.NewGuid().ToString("N"));
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

    private const string DropsOnEveryExit = """
        using Standard;

        namespace Ctx;

        public class Token
        {
            public string Name;

            public Token(string name)
            {
                this.Name = name;
            }

            public void Drop()
            {
                Console.WriteLine($"drop:{Name}");
            }
        }

        public class Program
        {
            public static void FallsOffTheEnd()
            {
                with(new Token("end"))
                {
                    Console.WriteLine("in:end");
                }
            }

            public static void ReturnsEarly()
            {
                with(new Token("early"))
                {
                    Console.WriteLine("in:early");
                    return;
                }
            }

            public static void Main()
            {
                FallsOffTheEnd();
                ReturnsEarly();
                Console.WriteLine("done");
            }
        }
        """;

    /// <summary>
    /// A generated array, whose elements come from the factory rather than from zero-filling. Every
    /// index is read back, because filling only the first -- or none -- still compiles and still
    /// prints an array of the right length.
    /// </summary>
    private const string FillsAnArrayFromAFactory = """
        using Standard;

        namespace Ctx;

        public class Program
        {
            public static void Main()
            {
                var labels = new Array<string, 3>(i => $"item-{i}");

                Console.WriteLine($"{labels[0]}|{labels[1]}|{labels[2]}|{labels.Length}");
            }
        }
        """;

    [Fact]
    public void AGeneratedArrayTakesEveryElementFromItsFactory()
    {
        Assert.Equal("item-0|item-1|item-2|3", CompileAndRun(FillsAnArrayFromAFactory));
    }

    [Fact]
    public void AWithBlockDropsItsValueOnEveryExit()
    {
        Assert.Equal(
            "in:end\ndrop:end\nin:early\ndrop:early\ndone",
            CompileAndRun(DropsOnEveryExit).ReplaceLineEndings("\n"));
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
            ProjectName: "Ctx",
            NamespaceRoot: "Ctx",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"With-block emission failed:\n{string.Join("\n", errors)}");
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