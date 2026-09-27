using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
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
/// Tier 4, the boundary from emitted C# to the runtime: purity Requirement 13.2 says the members Standard
/// declares pure read no state that can change while the program runs, and the current culture is such state
/// (Requirement 7.5); setting it through the environment is only the easiest way to vary it for a test. The
/// emitted text names the invariant culture (InvariantCultureEmissionTests); only running the program under a
/// culture that formats, orders and case-maps differently shows the CLR honoring it. One compile, under German
/// so a literal the compiler read by the current culture would print wrong (Requirement 13.3), and two runs;
/// each run first prints the culture it got, so a machine without ICU or one that ignores LANG fails here
/// instead of passing for nothing.
/// </summary>
[Trait("Tier", "4")]
public sealed class CultureInvariantEmissionTests : IDisposable
{
    private const string Program = """
        using Standard;
        using System.Globalization;

        namespace Invariant;

        type Meters = new double;

        type Letter = new char;

        public class Program
        {
            public static pure string Up(string s)
            {
                return s.ToUpper();
            }

            public static pure string Shown(double d)
            {
                return $"{d}";
            }

            public static pure int Parsed(string s)
            {
                return Convert.ToInt32(s);
            }

            public static pure string Sorted()
            {
                owned var words = new List<string>();
                words.Add("b");
                words.Add("a");
                words.Add("B");
                words.Add("I");
                words.Add("i");
                words.Sort();
                return words[0] + words[1] + words[2] + words[3] + words[4];
            }

            public static pure bool Insensitive(string s)
            {
                return `(?i)i`.IsMatch(s);
            }

            public static string Held<T>(T value)
            {
                return $"{value}";
            }

            public static T Least<T>(List<T> items)
            {
                items.Sort();
                return items[0];
            }

            public static string Grouped(string s)
            {
                func<string> up = s.ToUpper;
                return up();
            }

            public static void Main()
            {
                Console.WriteLine(CultureInfo.CurrentCulture.Name);
                Console.WriteLine(Up("i"));
                Console.WriteLine(Shown(1.5));
                Console.WriteLine(Parsed("1234"));
                Console.WriteLine(Sorted());
                Console.WriteLine(Insensitive("I"));
                Console.WriteLine("d=" + -2.5);
                Console.WriteLine(Held((1.5, -2)));
                Console.WriteLine(3.25);
                var letters = new List<string>();
                letters.Add("I");
                letters.Add("i");
                Console.WriteLine(Least(letters));
                var pairs = new List<(string, int)>();
                pairs.Add(("I", 1));
                pairs.Add(("i", 2));
                pairs.Sort();
                Console.WriteLine($"{pairs[0]}");
                Console.WriteLine(Grouped("i"));
                double? maybe = 2.5;
                Console.WriteLine(maybe?.ToString());
                Console.WriteLine((Meters)(-4.5));
                var prices = new System.Collections.Generic.Dictionary<string, double>();
                prices.Add("tea", 1.5);
                foreach(var entry in prices)
                {
                    Console.WriteLine(entry);
                }

                Console.WriteLine('a');
                Console.WriteLine((Letter)'q');
                var moment = new System.DateTime(2026, 9, 27, 13, 5, 0);
                Console.WriteLine(moment.ToString());
                var lazy = new System.Lazy<double>(1.5);
                Console.WriteLine($"{lazy}");
                var priced = new { Price = 1.5, Delta = -2 };
                Console.WriteLine(priced);
                Console.WriteLine(System.DayOfWeek.Monday);
            }
        }
        """;

    private static readonly string[] Invariant =
        ["I", "1.5", "1234", "abBiI", "True", "d=-2.5", "(1.5, -2)", "3.25", "i", "(i, 2)", "I", "2.5", "-4.5", "[tea, 1.5]",
            "a", "q", "09/27/2026 13:05:00", "1.5", "{ Price = 1.5, Delta = -2 }", "Monday"];

    private readonly string scratchDir;

    public CultureInvariantEmissionTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-culture-invariant-" + Guid.NewGuid().ToString("N"));
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
    public void AProgramRunUnderTurkishAndGermanCultures_PrintsTheInvariantText()
    {
        var artifact = Compile();

        Assert.Equal(["tr-TR", .. Invariant], Run(artifact, "tr_TR.UTF-8"));
        Assert.Equal(["de-DE", .. Invariant], Run(artifact, "de_DE.UTF-8"));
    }

    private string Compile()
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, Program);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Invariant",
            NamespaceRoot: "Invariant",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental);

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        CompilerResult result;

        try
        {
            Assert.Equal(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            result = driver.Compile(options);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Compilation failed:\n{string.Join("\n", errors)}");
        Assert.NotNull(result.PrimaryOutputArtifactPath);

        return result.PrimaryOutputArtifactPath;
    }

    private static string[] Run(string artifact, string locale)
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

        start.ArgumentList.Add(artifact);
        start.Environment["LANG"] = locale;
        start.Environment["LC_ALL"] = locale;
        _ = start.Environment.Remove("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT");

        using var process = Process.Start(start);

        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        _ = process.WaitForExit(60000);

        Assert.True(process.ExitCode == 0, stderr);

        return stdout.ReplaceLineEndings("\n").TrimEnd().Split('\n');
    }
}