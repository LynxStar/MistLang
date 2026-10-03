using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 4, the boundary from emitted C# to the runtime: a field initializer runs once per construction, with a
/// declared constructor, through a base type and in a struct. The emitted text shows only which assignments a
/// constructor writes; how many times an initializer runs is C#'s field-initializer semantics meeting them, which
/// only running the program shows. Every logging type takes a constructor argument: a type with no argument-taking
/// constructor is defaultable, and purity Requirement 12.1 holds its initializers pure, so the constructorless case
/// can show its values here but leaves "once" to DefaultableInitializerEmissionTests, on the emitted text.
/// </summary>
[Trait("Tier", "4")]
public sealed class FieldInitializerRuntimeTests : IDisposable
{
    private const string LeavesATrail = """
        using Standard;

        namespace Initialized;

        public class Log
        {
            public static string Trail = "";

            public static string Show(string tag) { Trail = Trail + tag; return tag; }

            public static int Num(string tag, int n) { Trail = Trail + tag; return n; }
        }

        public class Bare
        {
            public string Field = Log.Show("a");
            public int Count = Log.Num("b", 7);
            public string Plain;

            public Bare(int seed)
            {
            }
        }

        public class Quiet
        {
            public string Field = "q";
            public int Count = 7;
            public string Plain;
        }

        public class Box<T> { public T Value; }

        public class Widget
        {
            public int Value;
            default { Value = 7 }
        }

        public class Creator<T> where T : new()
        {
            public T Create() { return new T(); }
        }

        public class Builder<T> where T : new(int)
        {
            public T Create(int value) { return new T(value); }
        }

        public class Initializing<T> where T : Widget, new()
        {
            public T Create(int value) { return new T() { Value = value }; }
        }

        public class Defaults<T> where T : default
        {
            public T Get() { return default(T); }
        }

        public class Chosen<T> where T : default
        {
            public int Value;
            public T Child;
            public Chosen(int value) { Value = value; Child = default(T); }
            public T Get() { return default(T); }
            default { Value = 4 }
        }

        public class Declared
        {
            public string Field = Log.Show("a");

            public Declared(int seed)
            {
                Log.Trail = Log.Trail + "C";
            }
        }

        public class Derived : Bare
        {
            public string Extra = Log.Show("x");

            public Derived(int seed) : base(seed)
            {
            }
        }

        public struct Point
        {
            public int X = Log.Num("p", 3);
            public int Y;

            public Point(int seed)
            {
                Log.Trail = Log.Trail + "S";
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
                Log.Trail = "";
                var bare = new Bare(0);
                Console.WriteLine(Line("new-bare", $"{bare.Field} {bare.Count} [{bare.Plain}]"));
                var quiet = new Quiet();
                Console.WriteLine(Line("new-quiet", $"{quiet.Field} {quiet.Count} [{quiet.Plain}]"));
                var fresh = new Box<Widget>();
                var other = default(Box<Widget>);
                fresh.Value.Value = 9;
                Console.WriteLine(Line("generic-new", $"{fresh.Value.Value} {other.Value.Value}"));
                var creator = new Creator<Box<Widget>>();
                Console.WriteLine(Line("generic-constructor", $"{creator.Create().Value.Value}"));
                var builder = new Builder<Chosen<Widget>>();
                var built = builder.Create(11);
                Console.WriteLine(Line("parameterized-constructor", $"{built.Value} {built.Child.Value}"));
                var initializing = new Initializing<Widget>();
                Console.WriteLine(Line("constructed-initializer", $"{initializing.Create(13).Value}"));
                var defaults = new Defaults<Widget>();
                var first = defaults.Get();
                first.Value = 12;
                Console.WriteLine(Line("class-default", $"{first.Value} {defaults.Get().Value}"));
                var chosen = new Chosen<string>(8);
                var zero = default(Chosen<string>);
                Console.WriteLine(Line("explicit-default", $"{chosen.Value} {zero.Value} [{chosen.Get()}]"));
                var declared = new Declared(0);
                Console.WriteLine(Line("new-declared", declared.Field));
                var derived = new Derived(0);
                Console.WriteLine(Line("new-derived", $"{derived.Field} {derived.Extra}"));
                var point = new Point(0);
                Console.WriteLine(Line("new-point", $"{point.X} {point.Y}"));
            }
        }
        """;

    private readonly string scratchDir;

    public FieldInitializerRuntimeTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-field-initializers-" + Guid.NewGuid().ToString("N"));
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
    public void AFieldInitializer_RunsOncePerConstruction()
    {
        Assert.Equal(
            string.Join("\n",
            [
                "new-bare a 7 [] ab",
                "new-quiet q 7 [] ",
                "generic-new 9 7 ",
                "generic-constructor 7 ",
                "parameterized-constructor 11 7 ",
                "constructed-initializer 13 ",
                "class-default 12 7 ",
                "explicit-default 8 4 [] ",
                "new-declared a aC",
                "new-derived a x xab",
                "new-point 3 0 pS",
            ]),
            CompileAndRun(LeavesATrail).ReplaceLineEndings("\n"));
    }

    private string CompileAndRun(string source)
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, source);

        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Initialized",
            NamespaceRoot: "Initialized"));

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