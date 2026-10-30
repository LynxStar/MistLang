using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 4, the boundary from the standard library's own declarations to the model: that
/// <c>Standard.Func</c> and the structural <c>func&lt;...&gt;</c> are one type.
/// <para>
/// AGAINST THE REAL STANDARD LIBRARY, deliberately, and this is the one invariant in the pack work
/// that could not be tested any other way. Func is declared <c>Func&lt;params TArgs, TResult&gt;</c>
/// and Action is an ALIAS spreading a pack into it, so what is asserted here needs the pack
/// binding, the spread splice and the collapse to agree -- three things a synthetic Standard would
/// have to reproduce faithfully in order to test at all, and whose fixture would then be the thing
/// under test. The probe harness models Func as seventeen fixed arities, which is what the family
/// used to be.
/// </para>
/// </summary>
[Trait("Tier", "4")]
public sealed class FunctionTypeBridgeTests : IDisposable
{
    private readonly string scratchDir;

    public FunctionTypeBridgeTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-funcbridge-" + Guid.NewGuid().ToString("N"));
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
    /// Every form the bridge has to cover in one program, because they share a mechanism and fail
    /// together: a lambda reading its parameter types out of a nominal Func, the empty pack, the
    /// Action alias spreading into Func, and a Func-typed value being invoked at all.
    /// </summary>
    private const string UsesFuncAsAFunction = """
        using Standard;

        namespace Bridge;

        public class Program
        {
            public static void Main()
            {
                Func<int, int> doubler = x => x * 2;
                Func<int, int, int> add = (a, b) => a + b;
                Func<string> greet = () => "hi";
                Action<int> act = n => Console.WriteLine($"{n}");

                Console.WriteLine($"{doubler(2)}{add(1, 2)}{greet()}");
                act(1);
            }
        }
        """;

    /// <summary>
    /// Forwarding a pack into a Func whose parameter list IS that pack, with the pack and the
    /// result both inferred from the call. Asserted only as far as the MODEL, because emitting a
    /// a symbolic spread must be specialized to a concrete arity before emission.
    /// </summary>
    private const string ForwardsAPackIntoFunc = """
        using Standard;

        namespace Bridge;

        public class Program
        {
            public static void Main()
            {
                Func<int, int, int> add = (a, b) => a + b;
                Console.WriteLine($"{Apply(add, 1, 2)}");
            }

            public static TResult Apply<params TArgs, TResult>(Func<TArgs..., TResult> f, params TArgs args)
            {
                return f(args...);
            }
        }
        """;

    private ImmutableArray<Diagnostic> Compile(string source)
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, source);

        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser(), ambientTypes: _ => StubAmbientTypeSource.Instance);

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Bridge",
            NamespaceRoot: "Bridge"));

        succeeded = result.Succeeded;
        return result.Diagnostics;
    }

    private bool succeeded;

    [Fact]
    public void NominalFuncBindsAndCompilesAsAFunctionType()
    {
        var errors = Compile(UsesFuncAsAFunction)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Number.Value}: {d.Message}")
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"Standard.Func did not behave as a function type:\n{string.Join("\n", errors)}");
        Assert.True(succeeded);
    }

    /// <summary>
    /// The spread's own proof: `args...` into a slot declared as that same pack is provable for
    /// every instantiation, so nothing in the MODEL may refuse it. That it cannot yet be EMITTED is
    /// a separate and honest gap, which is why this asserts on the model stage alone.
    /// </summary>
    [Fact]
    public void ForwardingAPackIntoAMatchingFunc_BindsWithoutModelErrors()
    {
        var modelErrors = Compile(ForwardsAPackIntoFunc)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Stage == PipelineStage.SemanticModelConstruction)
            .Select(d => $"  {d.Number.Value}: {d.Message}")
            .ToList();

        Assert.True(
            modelErrors.Count == 0,
            $"Pack forwarding did not bind:\n{string.Join("\n", modelErrors)}");
    }
}