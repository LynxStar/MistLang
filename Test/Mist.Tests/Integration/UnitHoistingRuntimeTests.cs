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

    private const string RunsWhereTheValueIsTaken = """
        using Standard;

        namespace Hoisted;

        public class Log
        {
            public static string Trail = "";

            public static Log Box = new Log();

            public string? Opt = void;

            public Spot Pt = new Spot();

            public Log()
            {
            }

            public static void F() { Trail = Trail + "f"; }

            public static void G() { Trail = Trail + "g"; }

            public static bool T() { Trail = Trail + "t"; return true; }

            public static bool No() { Trail = Trail + "n"; return false; }

            public static bool Check(void unit) { Trail = Trail + "c"; return true; }

            public static string Show(void unit) { Trail = Trail + "s"; return "s"; }

            public static string Pair(void unit, int n) { Trail = Trail + "p"; return $"p{n}"; }

            public static bool Under(void unit, int i) { Trail = Trail + $"u{i}"; return i < 2; }

            public static int Next(void unit, int i) { Trail = Trail + "x"; return i + 1; }

            public static int Idx() { Trail = Trail + "i"; return 0; }

            public static void Bump(ref int slot, void unit) { slot = slot + 1; }

            public static Log Make() { Trail = Trail + "m"; return Box; }

            public static Spot MakeSpot() { Trail = Trail + "k"; return new Spot(); }

            public static Log? Maybe(bool yes) { Trail = Trail + "y"; if(yes) { return new Log(); } return void; }

            public static string Shown => Log.Show(Log.F());

            public string Take(void unit) { Trail = Trail + "T"; return "took"; }

            public bool Has(void unit) { Trail = Trail + "H"; return true; }

            public void Poke(void unit) { Trail = Trail + "P"; }
        }

        public struct Spot
        {
            public string Name;

            public string Describe(void unit) { Log.Trail = Log.Trail + "D"; return "d"; }
        }

        public class Seeded
        {
            public string Value = Log.Show(Log.F());

            public Seeded()
            {
            }
        }

        public class Base
        {
            public string Seen = "";

            public Base(string seen)
            {
                Seen = seen;
            }
        }

        public class Derived : Base
        {
            public Derived(int n) : base(Log.Pair(Log.G(), n))
            {
            }
        }

        public class Program
        {
            public static string Line(string label, string values)
            {
                var line = $"{label} {values} {Log.Trail}";
                Log.Trail = "";
                return line;
            }

            public static void Main()
            {
                var conjoined = $"{Log.No() && Log.Check(Log.F())} {Log.T() && Log.Check(Log.F())}";
                Console.WriteLine(Line("and", conjoined));

                var disjoined = $"{Log.T() || Log.Check(Log.F())} {Log.No() || Log.Check(Log.F())}";
                Console.WriteLine(Line("or", disjoined));

                string? none = void;
                string? some = "x";
                var coalesced = $"{none ?? Log.Show(Log.F())} {some ?? Log.Show(Log.G())}";
                Console.WriteLine(Line("coalesce", coalesced));

                Log.Make().Opt ??= Log.Show(Log.F());
                Log.Make().Opt ??= Log.Show(Log.G());
                Console.WriteLine(Line("coalesce-assign", $"{Log.Box.Opt}"));

                var absent = Log.Maybe(false)?.Take(Log.F());
                var present = Log.Maybe(true)?.Take(Log.G());
                Console.WriteLine(Line("conditional-access", $"{absent} {present}"));

                int one = 1;
                var armed = one switch { 1 => Log.Show(Log.F()), _ => Log.Show(Log.G()) };
                var unarmed = one switch { 2 => Log.Show(Log.F()), _ => "none" };
                Console.WriteLine(Line("switch", $"{armed} {unarmed}"));

                one switch { 1 => Log.F(), _ => Log.G() };
                Console.WriteLine(Line("switch-statement", "-"));

                for(int i = 0; Log.Under(Log.F(), i); i = Log.Next(Log.G(), i)) { Log.Trail = Log.Trail + "B"; }
                Console.WriteLine(Line("for", "-"));

                int turns = 0;
                while(Log.Maybe(turns < 2) is Log looped && looped.Has(Log.F())) { looped.Poke(void); turns = turns + 1; }
                Console.WriteLine(Line("while", $"{turns}"));

                object log = new Log();
                object text = "text";
                if(log is Log matched && matched.Has(Log.F())) { matched.Poke(void); } else { Log.Trail = Log.Trail + "E"; }
                Log.Trail = Log.Trail + "|";
                if(text is Log unmatched && unmatched.Has(Log.F())) { unmatched.Poke(void); } else { Log.Trail = Log.Trail + "E"; }
                Console.WriteLine(Line("if-else", "-"));

                Func<int, bool> above = (int k) => k > 1 && Log.Check(Log.F());
                Log.Trail = Log.Trail + "|";
                Console.WriteLine(Line("lambda", $"{above(1)} {above(2)}"));

                Console.WriteLine(Line("getter", $"{Log.Shown} {Log.Shown}"));

                Console.WriteLine(Line("initializer", $"{new Seeded().Value} {new Seeded().Value}"));

                Console.WriteLine(Line("chain", new Derived(3).Seen));

                Log.Make().Pt.Name = Log.Show(Log.F());
                int[] slots = [5];
                Log.Bump(ref slots[Log.Idx()], Log.F());
                Console.WriteLine(Line("location", $"{Log.Box.Pt.Name} {slots[0]}"));

                var described = Log.MakeSpot().Describe(Log.F());
                Console.WriteLine(Line("receiver", described));
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

    [Fact]
    public void ALoweredCall_RunsOnceWhereItsValueIsTakenAndNotWhereItsBranchIsNot()
    {
        Assert.Equal(
            string.Join("\n",
            [
                "and False True ntfc",
                "or True True tnfc",
                "coalesce s x fs",
                "coalesce-assign s mfsm",
                "conditional-access Void took yygT",
                "switch s none fs",
                "switch-statement - f",
                "for - fu0Bgxfu1Bgxfu2",
                "while 2 yfHPyfHPy",
                "if-else - fHP|E",
                "lambda False True |fc",
                "getter s s fsfs",
                "initializer s s fsfs",
                "chain p3 gp",
                "location s 6 mfsif",
                "receiver d kfD",
            ]),
            CompileAndRun(RunsWhereTheValueIsTaken).ReplaceLineEndings("\n"));
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