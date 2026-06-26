// Feature: drop-semantics, Property 19: Conditional move branch-sensitive drop
//
// **Validates: Requirements 4.6, 13.5**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Tests for Property 19: Conditional move branch-sensitive drop.
///
/// Feature: drop-semantics, Property 19: Conditional move branch-sensitive drop
///
/// **Validates: Requirements 4.6, 13.5**
///
/// For any linear variable that is moved in one branch of a conditional but not another,
/// the compiler SHALL insert <c>Drop()</c> only in the branches where the variable was
/// NOT moved. This ensures exactly-once semantics: the variable is either moved (and the
/// callee is responsible) or dropped (and the caller cleans up), never both.
///
/// These tests compile in-process via a shared <see cref="CompilerDriver"/> and inspect
/// the emitted C# text. They spawn 0 external processes.
/// </summary>
public sealed class ConditionalMoveBranchDropTests : IDisposable
{
    private readonly string outputDir;

    public ConditionalMoveBranchDropTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistCondMoveDrop_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver ───────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Compile helper ──────────────────────────────────────────────────

    private string? CompileAndGetEmittedCSharp(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation produced errors:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        if(result.EmittedFiles is null)
        {
            return null;
        }

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        return null;
    }

    // ─── Test sources ────────────────────────────────────────────────────

    /// <summary>
    /// A linear type with a void Drop() and a consuming function that takes ownership.
    /// When a linear variable is passed to <c>Sink.Consume</c>, the variable is moved
    /// (ownership transfers) and the caller should NOT drop it.
    /// </summary>
    private const string LinearTypeAndSink = """
        namespace TestProgram;

        linear class Resource
        {
            public void Drop()
            {
            }

            public int GetId()
            {
                return 1;
            }
        }

        class Sink
        {
            public static void Consume(Resource r)
            {
            }
        }
        """;

    /// <summary>
    /// Program where a linear variable is moved in the if-branch (passed to a consuming
    /// function) but NOT in the else-branch. Drop() should appear only in the else-branch.
    /// </summary>
    private static string ProgramWithMoveInIfBranch => LinearTypeAndSink + """

        class Program
        {
            public static void Main(string[] args)
            {
                Resource res = new Resource();
                if (args.Length > 0)
                {
                    Sink.Consume(res);
                }
                else
                {
                    int x = res.GetId();
                }
            }
        }
        """;

    /// <summary>
    /// Program where a linear variable is moved in the else-branch but NOT in the
    /// if-branch. Drop() should appear only in the if-branch.
    /// </summary>
    private static string ProgramWithMoveInElseBranch => LinearTypeAndSink + """

        class Program
        {
            public static void Main(string[] args)
            {
                Resource res = new Resource();
                if (args.Length > 0)
                {
                    int x = res.GetId();
                }
                else
                {
                    Sink.Consume(res);
                }
            }
        }
        """;

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether a specific local's Drop() call appears within a given region of text.
    /// Uses word-boundary matching to avoid false positives from longer names.
    /// </summary>
    private static bool ContainsDropCall(string text, string localName)
    {
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(localName)}\.Drop\(\)";
        return Regex.IsMatch(text, pattern);
    }

    /// <summary>
    /// Extracts the body of a block (the text between its opening and closing braces)
    /// starting from a given position.
    /// </summary>
    private static string? ExtractBlockBody(string source, int searchStart)
    {
        var openBrace = source.IndexOf('{', searchStart);
        if(openBrace < 0)
        {
            return null;
        }

        var closeBrace = FindMatchingCloseBrace(source, openBrace);
        if(closeBrace < 0)
        {
            return null;
        }

        return source.Substring(openBrace + 1, closeBrace - openBrace - 1);
    }

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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 19: Conditional move branch-sensitive drop
    //
    // **Validates: Requirements 4.6, 13.5**
    //
    // When a linear variable is moved in one branch of a conditional but not the
    // other, Drop() is inserted ONLY in the branch where the variable was NOT moved.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A linear variable moved in the if-branch should have Drop() emitted only
    /// in the else-branch (where it was not moved).
    /// **Validates: Requirements 4.6, 13.5**
    /// </summary>
    [Fact]
    public void LinearVariable_MovedInIfBranch_DropsOnlyInElseBranch()
    {
        var emitted = CompileAndGetEmittedCSharp(ProgramWithMoveInIfBranch);

        Assert.NotNull(emitted);

        // Find the if/else in the emitted Main method.
        // The emitted C# should contain "if" with the condition and an else block.
        // Drop() should appear in the else block (where the variable was NOT moved),
        // and should NOT appear in the if block (where the variable WAS moved via Sink.Consume).

        // Locate the if block and else block in the emitted C#.
        // We look for the if statement containing Consume (the move branch).
        var ifIndex = emitted.IndexOf("if (", StringComparison.Ordinal);
        Assert.True(ifIndex >= 0, "Expected an 'if' statement in the emitted C#.\n\nEmitted:\n" + emitted);

        // Find the then-branch body (first block after the if condition).
        var thenBody = ExtractBlockBody(emitted, ifIndex);
        Assert.NotNull(thenBody);

        // Find the else keyword after the then block.
        var thenEnd = emitted.IndexOf('{', ifIndex);
        var thenCloseBrace = FindMatchingCloseBrace(emitted, thenEnd);
        var elseIndex = emitted.IndexOf("else", thenCloseBrace, StringComparison.Ordinal);
        Assert.True(elseIndex >= 0, "Expected an 'else' clause in the emitted C#.\n\nEmitted:\n" + emitted);

        // Extract the else block body.
        var elseBody = ExtractBlockBody(emitted, elseIndex);
        Assert.NotNull(elseBody);

        // The then-branch (move branch) should NOT contain res.Drop().
        Assert.False(ContainsDropCall(thenBody!, "res"),
            "The if-branch (where 'res' is moved to Sink.Consume) should NOT contain res.Drop().\n\n" +
            $"Then body:\n{thenBody}\n\nFull emitted C#:\n{emitted}");

        // The else-branch (non-move branch) SHOULD contain res.Drop().
        Assert.True(ContainsDropCall(elseBody!, "res"),
            "The else-branch (where 'res' is NOT moved) should contain res.Drop().\n\n" +
            $"Else body:\n{elseBody}\n\nFull emitted C#:\n{emitted}");
    }

    /// <summary>
    /// A linear variable moved in the else-branch should have Drop() emitted only
    /// in the if-branch (where it was not moved).
    /// **Validates: Requirements 4.6, 13.5**
    /// </summary>
    [Fact]
    public void LinearVariable_MovedInElseBranch_DropsOnlyInIfBranch()
    {
        var emitted = CompileAndGetEmittedCSharp(ProgramWithMoveInElseBranch);

        Assert.NotNull(emitted);

        // Locate the if block and else block.
        var ifIndex = emitted.IndexOf("if (", StringComparison.Ordinal);
        Assert.True(ifIndex >= 0, "Expected an 'if' statement in the emitted C#.\n\nEmitted:\n" + emitted);

        var thenBody = ExtractBlockBody(emitted, ifIndex);
        Assert.NotNull(thenBody);

        var thenEnd = emitted.IndexOf('{', ifIndex);
        var thenCloseBrace = FindMatchingCloseBrace(emitted, thenEnd);
        var elseIndex = emitted.IndexOf("else", thenCloseBrace, StringComparison.Ordinal);
        Assert.True(elseIndex >= 0, "Expected an 'else' clause in the emitted C#.\n\nEmitted:\n" + emitted);

        var elseBody = ExtractBlockBody(emitted, elseIndex);
        Assert.NotNull(elseBody);

        // The if-branch (non-move branch) SHOULD contain res.Drop().
        Assert.True(ContainsDropCall(thenBody!, "res"),
            "The if-branch (where 'res' is NOT moved) should contain res.Drop().\n\n" +
            $"Then body:\n{thenBody}\n\nFull emitted C#:\n{emitted}");

        // The else-branch (move branch) should NOT contain res.Drop().
        Assert.False(ContainsDropCall(elseBody!, "res"),
            "The else-branch (where 'res' is moved to Sink.Consume) should NOT contain res.Drop().\n\n" +
            $"Else body:\n{elseBody}\n\nFull emitted C#:\n{emitted}");
    }
}