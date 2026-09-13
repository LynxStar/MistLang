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
/// Tier 4, the boundary from emitted C# to the runtime: a newtype's conversions reach private state through
/// UnsafeAccessors the runtime binds by signature only when the call runs. An accessor declared on a closed
/// generic, or one whose name or signature misses its target, compiles and throws there, so only running the
/// conversions tells a working accessor from one that is not.
/// </summary>
public sealed class CloneConversionRuntimeTests : IDisposable
{
    private const string ConvertsEachKindOfConstructedSource = """
        using Standard;

        namespace Constructed;

        public struct Pair<A, B>
        {
            private A first;
            public B Second { get; set; }

            public Pair(A first, B second)
            {
                this.first = first;
                this.Second = second;
            }

            public A First()
            {
                return first;
            }
        }

        public class Ledger<T>
        {
            private T serial;

            public Ledger(T serial)
            {
                this.serial = serial;
            }

            public T Serial()
            {
                return serial;
            }
        }

        public readonly class Money<T> : Ledger<T>
        {
            private string currency;

            public Money(string currency, T serial) : base(serial)
            {
                this.currency = currency;
            }

            public string Currency()
            {
                return currency;
            }
        }

        public class Cell<T> : IClonable<Cell<T>>
        {
            private static int made = 0;
            private T content;
            private string label { get; set; }

            public Cell(T content)
            {
                this.content = content;
                this.label = "fresh";
            }

            private Cell(T content, string label)
            {
                this.content = content;
                this.label = label;
            }

            public owned Cell<T> Clone()
            {
                made = made + 1;
                return new Cell<T>(content, $"{label}#{made}");
            }

            public string Describe()
            {
                return $"{content} {label} made={made}";
            }
        }

        public type IntPair = new Pair<int, string>;
        public type IntMoney = new Money<int>;
        public type IntCell = new Cell<int>;

        public class Program
        {
            public static void Main()
            {
                Pair<int, string> left = new IntPair(1, "one");
                IntPair back = (IntPair)new Pair<int, string>(2, "two");
                Money<int> spent = new IntMoney("EUR", 7);
                IntMoney earned = (IntMoney)new Money<int>("USD", 8);
                var cell = new Cell<int>(3);
                var entered = (IntCell)cell;
                var returned = (Cell<int>)entered;

                Console.WriteLine($"{left.First()},{left.Second} {back.First()},{back.Second}");
                Console.WriteLine($"{spent.Currency()},{spent.Serial()} {earned.Currency()},{earned.Serial()}");
                Console.WriteLine($"{entered.Describe()} | {returned.Describe()}");
            }
        }
        """;

    private const string ReachesEveryKindOfPrivateMember = """
        using Standard;

        namespace Reached;

        public abstract IShow
        {
            string Show();
        }

        public class Word : IShow
        {
            private string text;

            public Word(string text)
            {
                this.text = text;
            }

            public string Show()
            {
                return text;
            }
        }

        public class Tiger : IClonable<Tiger>
        {
            private int stripes;
            private string? note;

            private int Doubled
            {
                get { return stripes * 2; }
                set { stripes = value / 2; }
            }

            private string? Note
            {
                get { return note; }
                set { note = value; }
            }

            private int this[int index]
            {
                get { return stripes + index; }
                set { stripes = value - index; }
            }

            public Tiger(int stripes)
            {
                this.stripes = stripes;
            }

            private T Echo<T>(T value)
            {
                return value;
            }

            private int Three()
            {
                return 3;
            }

            private void Bump()
            {
                stripes = stripes + 100;
            }

            private static int Seven()
            {
                return 7;
            }

            private string Shown<U>(U item) where U : IShow
            {
                return item.Show();
            }

            public owned Tiger Clone()
            {
                var copy = new Tiger(Echo<int>(stripes));
                Tiger? maybe = copy;
                int? viaConditional = maybe?.stripes;
                Doubled = Doubled + 2;
                copy.Note ??= copy.Shown<Word>(new Word("noted"));
                Note ??= Shown<Word>(new Word("self"));
                copy[1] = this[1] + 10;
                func<int> three = Three;
                func<int> seven = Seven;
                func<int> copyThree = copy.Three;
                maybe?.Bump();
                int? maybeThree = maybe?.Three();
                Bump();
                int conditional = viaConditional ?? 0;
                int fromMaybe = maybeThree ?? 0;
                copy.stripes = copy.stripes + three() + conditional + copy.Echo<int>(1) + seven() + copyThree() + fromMaybe;
                return copy;
            }

            public string Describe()
            {
                return $"stripes={stripes} note={note}";
            }
        }

        public type Jaguar = new Tiger;

        public class Program
        {
            public static void Main()
            {
                var tiger = new Tiger(4);
                var jaguar = (Jaguar)tiger;
                var back = (Tiger)jaguar;

                Console.WriteLine($"{tiger.Describe()} | {jaguar.Describe()} | {back.Describe()}");
            }
        }
        """;

    private readonly string scratchDir;

    public CloneConversionRuntimeTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-constructed-clone-" + Guid.NewGuid().ToString("N"));
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
    public void EveryConversionOfAConstructedSource_CopiesItsPrivateStateAtRuntime()
    {
        Assert.Equal(
            "1,one 2,two\nEUR,7 USD,8\n3 fresh#1 made=1 | 3 fresh#1#1 made=1",
            CompileAndRun(ConvertsEachKindOfConstructedSource).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void EveryKindOfPrivateMemberAConversionReaches_RunsThroughItsAccessor()
    {
        Assert.Equal(
            "stripes=105 note=self | stripes=237 note=noted | stripes=400 note=noted",
            CompileAndRun(ReachesEveryKindOfPrivateMember));
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
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
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