// Feature: drop-semantics, Property 18: Try/finally wrapping
// **Validates: Requirements 14.3**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

/// <summary>
/// Example-based tests for Property 18: Try/Finally wrapping for linear/relevant scopes.
///
/// Feature: drop-semantics, Property 18: Try/finally wrapping
/// **Validates: Requirements 14.3**
///
/// For any scope containing a linear or relevant local with a <c>Drop()</c> obligation,
/// the C# emitter SHALL wrap the remaining scope body in a <c>try/finally</c> block and
/// emit the scope-exit <c>Drop()</c> call inside the <c>finally</c> clause, so cleanup is
/// guaranteed even when an exception is thrown.
///
/// Substructural discipline lives on the TYPE. Each scenario declares a
/// <c>linear class</c> / <c>relevant class</c> with a <c>Drop()</c>, declares a local of
/// that type that reaches scope exit (no move, no return), compiles fully in-process via a
/// per-instance <see cref="CompilerDriver"/> that includes the <see cref="DropAnalyzer"/>,
/// and inspects the emitted C# text. Spawns 0 external processes.
/// </summary>
public sealed class TryFinallyDropWrappingTests : IDisposable
{
    private readonly string _outputDir;

    public TryFinallyDropWrappingTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistTryFinallyDropPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Compile helper (driver includes DropAnalyzer) ───────────────────

    private string CompileAndGetEmittedCSharp(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new Mist.Semantics.Model.SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(
                new TypeCheckerAnalyzer(),
                new ReadonlyAnalyzer(),
                new OwnershipAnalyzer(),
                new DropAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = driver.Compile(options);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation produced errors:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        Assert.NotNull(result.EmittedFiles);
        foreach(var (_, content) in result.EmittedFiles!)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        Assert.Fail("No emitted C# file containing 'namespace TestProgram' was found.");
        return ""; // unreachable
    }

    // ─── Test sources ────────────────────────────────────────────────────

    // A linear type: owned, single-holder, drops exactly once at scope exit.
    private const string LinearResourceType = """
        linear class LinearResource
        {
            public void Drop()
            {
            }

            public int GetId()
            {
                return 1;
            }
        }
        """;

    // A relevant type: aliasable, Drop() fires (idempotently) at scope exit.
    private const string RelevantResourceType = """
        relevant class RelevantResource
        {
            public void Drop()
            {
            }

            public int GetId()
            {
                return 2;
            }
        }
        """;

    private static string LinearProgram(string body) => $$"""
        {{LinearResourceType}}
        class Program
        {
            public static void Main()
            {
        {{body}}
            }
        }
        """;

    private static string RelevantProgram(string body) => $$"""
        {{RelevantResourceType}}
        class Program
        {
            public static void Main()
            {
        {{body}}
            }
        }
        """;

    // ─── Scenarios ─────────────────────────────────────────────────────────

    public static IEnumerable<object[]> WrappingScenarios()
    {
        // Linear local that reaches scope exit (no move, no return) → drop in finally.
        yield return new object[]
        {
            "linear local",
            LinearProgram("""
                    LinearResource r = new LinearResource();
                    int x = r.GetId();
            """),
            "r",
        };

        // Relevant local that reaches scope exit → drop in finally.
        yield return new object[]
        {
            "relevant local",
            RelevantProgram("""
                    RelevantResource r = new RelevantResource();
                    int x = r.GetId();
            """),
            "r",
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 18: Try/finally wrapping for linear/relevant scopes
    //
    // Asserts the emitted C# for Main contains a `try` and a `finally`, and that
    // the `<local>.Drop()` call appears INSIDE the finally block region.
    // **Validates: Requirements 14.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(WrappingScenarios))]
    public void LinearOrRelevantScope_WrappedInTryFinally_WithDropInFinally(
        string label, string source, string localName)
    {
        var csharp = CompileAndGetEmittedCSharp(source);

        // The scope must be wrapped in a try/finally.
        Assert.Contains("try", csharp);
        Assert.Contains("finally", csharp);

        // The scope-exit Drop() call must appear inside the finally block region.
        var dropCall = $"{localName}.Drop();";
        Assert.Contains(dropCall, csharp);

        var finallyIndex = csharp.IndexOf("finally", StringComparison.Ordinal);
        Assert.True(finallyIndex >= 0, "No 'finally' clause found in emitted C#.");

        // Locate the finally block's brace region: the first '{' after the
        // 'finally' keyword and its matching '}'.
        var finallyOpenBrace = csharp.IndexOf('{', finallyIndex);
        Assert.True(finallyOpenBrace >= 0, "No opening brace found for the finally clause.");

        var finallyCloseBrace = FindMatchingCloseBrace(csharp, finallyOpenBrace);
        Assert.True(finallyCloseBrace >= 0, "No matching closing brace found for the finally clause.");

        var finallyBody = csharp.Substring(
            finallyOpenBrace + 1,
            finallyCloseBrace - finallyOpenBrace - 1);

        Assert.True(
            finallyBody.Contains(dropCall, StringComparison.Ordinal),
            $"Scenario '{label}': expected '{dropCall}' inside the finally block, but the finally body was:\n{finallyBody}\n\nFull emitted C#:\n{csharp}");

        // Sanity: the `try` opening must precede the `finally` (well-formed try/finally).
        var tryIndex = csharp.IndexOf("try", StringComparison.Ordinal);
        Assert.True(tryIndex >= 0 && tryIndex < finallyIndex,
            "Expected a 'try' keyword before the 'finally' clause.");
    }

    // ─── Utility ─────────────────────────────────────────────────────────

    private static int FindMatchingCloseBrace(string source, int openBraceIndex)
    {
        var depth = 0;
        for(var i = openBraceIndex; i < source.Length; i++)
        {
            if(source[i] == '{')
            {
                depth++;
            }
            else if(source[i] == '}')
            {
                depth--;
            }

            if(depth == 0)
            {
                return i;
            }
        }
        return -1;
    }
}