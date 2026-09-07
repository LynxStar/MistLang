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
/// Tier 4, the boundary from a backtick pattern to compiled C#: that a regex survives as a matcher
/// rather than as the characters it was written with.
/// <para>
/// RUN RATHER THAN INSPECTED, because every failure this covers compiles. A pattern escaped twice
/// is a valid C# string that matches the wrong thing; a regex pattern whose guard is dropped is a
/// valid C# type test that is true of every string. Both produce a well-formed file and a wrong
/// answer, so only the printed answer separates them.
/// </para>
/// </summary>
public sealed class RegexEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public RegexEmissionTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-regex-" + Guid.NewGuid().ToString("N"));
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
    /// A backslash class through both spellings. The backticked form is raw regex and the quoted
    /// shorthand is already written as a string body, so a single escaping rule applied to both
    /// gets one of them wrong -- and gets it wrong by MATCHING NOTHING, not by failing to build.
    /// </summary>
    private const string MatchesThroughBothSpellings = """
        using Standard;

        namespace Rx;

        public class Program
        {
            public static void Main()
            {
                var ticked = `\d+`;
                regex quoted = "\\d+";

                Console.WriteLine($"{ticked.IsMatch("123")}{ticked.IsMatch("abc")}{quoted.IsMatch("123")}{ticked.Pattern}");
            }
        }
        """;

    /// <summary>
    /// The full-match derivation, which is the whole difference between IsMatch and IsFullMatch:
    /// a partial match of "abc123" succeeds where a full one does not.
    /// </summary>
    private const string DistinguishesPartialFromFull = """
        using Standard;

        namespace Rx;

        public class Program
        {
            public static void Main()
            {
                var digits = `\d+`;

                Console.WriteLine($"{digits.IsMatch("abc123")}{digits.IsFullMatch("abc123")}{digits.IsFullMatch("123")}");
            }
        }
        """;

    /// <summary>
    /// A regex in PATTERN position, twice in one scope. Twice deliberately: the guard binds a
    /// temporary, and a refinement's predicate is written once in its alias yet reached by every
    /// test of it, so a name taken from the predicate's own node repeats and C# rejects the
    /// duplicate local.
    /// </summary>
    private const string TestsPatternsAndARefinedType = """
        using Standard;

        namespace Rx;

        type Email = string `^[^@]+@[^@]+\.[^@]+$`;

        public class Program
        {
            public static void Main()
            {
                string good = "user@example.com";
                string bad = "not-an-email";

                bool a = good is Email okA;
                bool b = bad is Email okB;
                bool c = "hello" is `^[a-z]+$`;
                bool d = "hello123" is `^[a-z]+$`;

                var category = "WORLD" switch {
                    `^\d+$` => "number",
                    `^[A-Z]+$` => "uppercase",
                    var _ => "other",
                };

                Console.WriteLine($"{a}{b}{c}{d}|{category}");
            }
        }
        """;

    [Fact]
    public void BothSpellingsOfAPatternMatchTheSameText()
    {
        Assert.Equal("TrueFalseTrue\\d+", CompileAndRun(MatchesThroughBothSpellings));
    }

    [Fact]
    public void AFullMatchIsAnchoredWhereAPartialMatchIsNot()
    {
        Assert.Equal("TrueFalseTrue", CompileAndRun(DistinguishesPartialFromFull));
    }

    [Fact]
    public void ARegexInPatternPositionIsMatchedAndBindsItsOwnTemporary()
    {
        Assert.Equal("TrueFalseTrueFalse|uppercase", CompileAndRun(TestsPatternsAndARefinedType));
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
            ProjectName: "Rx",
            NamespaceRoot: "Rx",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Regex emission failed:\n{string.Join("\n", errors)}");
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