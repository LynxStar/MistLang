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
/// Tier 4, the boundary from emitted C# to the runtime: the conversions of a newtype over a constructed
/// source reach that source's private state through UnsafeAccessors the runtime binds only when the call
/// runs. An accessor declared on the closed construction compiles and throws MissingFieldException there,
/// so only running the conversions tells a holder over the definition's parameters from one that is not.
/// </summary>
public sealed class ConstructedCloneConversionTests : IDisposable
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

    private readonly string scratchDir;

    public ConstructedCloneConversionTests()
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
            ProjectName: "Constructed",
            NamespaceRoot: "Constructed",
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