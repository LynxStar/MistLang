using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 4, the boundary from a variadic declaration to compiled C#: that a family written ONCE
/// becomes one C# type per arity used, and that each behaves.
/// <para>
/// RUN RATHER THAN INSPECTED, because the interesting failure is not a malformed file. A
/// specialization that emits, compiles, and reports the wrong arity is exactly what a per-CONSTRUCTION
/// scheme would produce, or one that read the arity off the argument count -- both of which yield
/// well-formed C# that Roslyn accepts. Only the printed answer separates them.
/// </para>
/// </summary>
[Trait("Tier", "4")]
public sealed class MonomorphEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public MonomorphEmissionTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-monomorph-" + Guid.NewGuid().ToString("N"));
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
    /// Two arities of one family, and two constructions of the SAME arity -- the latter to pin that
    /// they share a specialization rather than getting one each.
    /// </summary>
    private const string UsesOneFamilyAtSeveralArities = """
        using Standard;

        namespace Mono;

        public class Box<params TArgs>
        {
            public int Count() { return TArgs.Length; }
        }

        public class Program
        {
            public static void Main()
            {
                var two = new Box<int, string>();
                var alsoTwo = new Box<bool, char>();
                var one = new Box<bool>();
                var none = new Box();

                Console.WriteLine($"{two.Count()}{alsoTwo.Count()}{one.Count()}{none.Count()}");
            }
        }
        """;

    /// <summary>
    /// The method axis, and the calling convention that goes with it: `params TArgs args` is ONE
    /// tuple parameter reached by a FLAT argument list, which C# has no notion of -- so the
    /// arguments the pack absorbs are packed at the call. The zero and one widths are included
    /// because C#'s tuple syntax cannot spell either.
    /// </summary>
    private const string CallsAVariadicMethodAtSeveralWidths = """
        using Standard;

        namespace Mono;

        public class Program
        {
            public static int Describe<params TArgs>(params TArgs args)
            {
                return TArgs.Length;
            }

            public static void Main()
            {
                Console.WriteLine($"{Describe(1, "x", true)}{Describe(5)}{Describe()}");
            }
        }
        """;

    /// <summary>
    /// A family implementing a variadic abstract, which is the case where the specialization has to
    /// reach a DIFFERENT declaration than its own: the base clause names ICountable at the width Tup
    /// was specialized to, and nothing constructs ICountable at that width directly.
    /// </summary>
    private const string ImplementsAVariadicAbstract = """
        using Standard;

        namespace Mono;

        public abstract ICountable<params TArgs>
        {
            public int Count();
        }

        public class Tup<params TArgs> : ICountable<TArgs...>
        {
            public int Count() { return TArgs.Length; }
        }

        public class Program
        {
            public static void Main()
            {
                ICountable<int, string, bool> three = new Tup<int, string, bool>();
                ICountable<char> one = new Tup<char>();

                Console.WriteLine($"{three.Count()}{one.Count()}");
            }
        }
        """;

    [Fact]
    public void AVariadicBaseIsSpecializedAlongsideTheTypeThatNamesIt()
    {
        Assert.Equal("31", CompileAndRun(ImplementsAVariadicAbstract));
    }

    [Fact]
    public void AVariadicMethodBecomesOnePerWidthAndTakesAFlatCall()
    {
        Assert.Equal("310", CompileAndRun(CallsAVariadicMethodAtSeveralWidths));
    }

    [Fact]
    public void AFamilyBecomesOneTypePerArityAndReportsItsOwn()
    {
        Assert.Equal("2210", CompileAndRun(UsesOneFamilyAtSeveralArities));
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
            ProjectName: "Mono",
            NamespaceRoot: "Mono"));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Specialization failed:\n{string.Join("\n", errors)}");
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