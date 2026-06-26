// Task 7.4 — Manual Drop() body completion with remaining field drops
// **Validates: Requirements 10.7, 10.8**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

/// <summary>
/// Tests that when a type provides a manual <c>Drop()</c> and has droppable fields,
/// the emitter tracks which fields were explicitly dropped in the body and emits
/// <c>Drop()</c> for the remaining droppable fields (declaration order) at the end
/// of the method body.
///
/// **Validates: Requirements 10.7, 10.8**
///
/// All compilation runs fully in-process. Spawns 0 external processes.
/// </summary>
public sealed class ManualDropFieldCompletionTests : IDisposable
{
    private readonly string outputDir;

    public ManualDropFieldCompletionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistManualDropCompletion_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Compile helper ──────────────────────────────────────────────────

    private string CompileAndGetEmittedCSharp(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
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
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
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

    // ─── Test: All fields un-dropped → emitter appends Drop() for each field ───

    [Fact]
    public void ManualDrop_NoExplicitFieldDrops_EmitsAllFieldDropsInDeclarationOrder()
    {
        // Container has two droppable fields (Handle, Log) and a manual Drop()
        // that does NOT explicitly drop either field.
        var source = """
            linear class Resource
            {
                int Id;

                public Resource(int id)
                {
                    this.Id = id;
                }

                public void Drop()
                {
                }
            }

            linear class Connection
            {
                Resource Handle;
                Resource Log;

                public Connection(int h, int l)
                {
                    this.Handle = new Resource(h);
                    this.Log = new Resource(l);
                }

                public void Drop()
                {
                    // No explicit field drops — compiler should append both.
                }
            }

            class Program
            {
                public static void Main()
                {
                    Connection c = new Connection(1, 2);
                }
            }
            """;

        var emitted = CompileAndGetEmittedCSharp(source);

        // The emitted Drop() body for Connection should contain:
        //   this.Handle.Drop();
        //   this.Log.Drop();
        // in declaration order.
        var dropBody = ExtractMethodBody(emitted, "Drop", "Connection");
        Assert.NotNull(dropBody);

        var handlePos = dropBody!.IndexOf("this.Handle.Drop()", StringComparison.Ordinal);
        var logPos = dropBody.IndexOf("this.Log.Drop()", StringComparison.Ordinal);

        Assert.True(handlePos >= 0, $"Expected this.Handle.Drop() in Drop() body. Got:\n{dropBody}");
        Assert.True(logPos >= 0, $"Expected this.Log.Drop() in Drop() body. Got:\n{dropBody}");
        Assert.True(handlePos < logPos,
            $"Expected Handle.Drop() before Log.Drop() (declaration order). Handle@{handlePos}, Log@{logPos}.\n{dropBody}");
    }

    // ─── Test: One field explicitly dropped → only remaining is auto-inserted ───

    [Fact]
    public void ManualDrop_OneFieldExplicitlyDropped_EmitsRemainingFieldDrop()
    {
        // Container has two droppable fields. The manual Drop() explicitly drops Handle
        // but not Log → compiler should only append Log.Drop().
        var source = """
            linear class Resource
            {
                int Id;

                public Resource(int id)
                {
                    this.Id = id;
                }

                public void Drop()
                {
                }
            }

            linear class Connection
            {
                Resource Handle;
                Resource Log;

                public Connection(int h, int l)
                {
                    this.Handle = new Resource(h);
                    this.Log = new Resource(l);
                }

                public void Drop()
                {
                    this.Handle.Drop();
                    // Log not explicitly dropped — compiler appends it.
                }
            }

            class Program
            {
                public static void Main()
                {
                    Connection c = new Connection(1, 2);
                }
            }
            """;

        var emitted = CompileAndGetEmittedCSharp(source);

        var dropBody = ExtractMethodBody(emitted, "Drop", "Connection");
        Assert.NotNull(dropBody);

        // Handle.Drop() should appear (explicit from user code) — it will be emitted as part
        // of the regular body statements. The auto-appended one is this.Log.Drop().
        Assert.Contains("this.Log.Drop()", dropBody);

        // Count how many times Handle.Drop() appears — should be exactly once (the explicit one).
        var handleDropCount = CountOccurrences(dropBody!, "Handle.Drop()");
        Assert.Equal(1, handleDropCount);
    }

    // ─── Test: All fields explicitly dropped → nothing appended ─────────────────

    [Fact]
    public void ManualDrop_AllFieldsExplicitlyDropped_NoAutoInsert()
    {
        // Container has two droppable fields. The manual Drop() explicitly drops both.
        // No auto-insertion needed.
        var source = """
            linear class Resource
            {
                int Id;

                public Resource(int id)
                {
                    this.Id = id;
                }

                public void Drop()
                {
                }
            }

            linear class Connection
            {
                Resource Handle;
                Resource Log;

                public Connection(int h, int l)
                {
                    this.Handle = new Resource(h);
                    this.Log = new Resource(l);
                }

                public void Drop()
                {
                    this.Handle.Drop();
                    this.Log.Drop();
                }
            }

            class Program
            {
                public static void Main()
                {
                    Connection c = new Connection(1, 2);
                }
            }
            """;

        var emitted = CompileAndGetEmittedCSharp(source);

        var dropBody = ExtractMethodBody(emitted, "Drop", "Connection");
        Assert.NotNull(dropBody);

        // Both Drop() calls should appear exactly once (the explicit ones).
        var handleDropCount = CountOccurrences(dropBody!, "Handle.Drop()");
        var logDropCount = CountOccurrences(dropBody!, "Log.Drop()");
        Assert.Equal(1, handleDropCount);
        Assert.Equal(1, logDropCount);
    }

    // ─── Test: Non-droppable fields are skipped ────────────────────────────────

    [Fact]
    public void ManualDrop_MixedFields_OnlyDroppableFieldsAutoInserted()
    {
        // Container has one droppable field (Handle) and one non-droppable (Count).
        // Manual Drop() doesn't drop Handle → compiler appends Handle.Drop().
        // Count (int) is never dropped.
        var source = """
            linear class Resource
            {
                int Id;

                public Resource(int id)
                {
                    this.Id = id;
                }

                public void Drop()
                {
                }
            }

            linear class Container
            {
                int Count;
                Resource Handle;

                public Container(int c, int h)
                {
                    this.Count = c;
                    this.Handle = new Resource(h);
                }

                public void Drop()
                {
                    // Count is int (no Drop), Handle is droppable but not explicitly dropped.
                }
            }

            class Program
            {
                public static void Main()
                {
                    Container c = new Container(5, 1);
                }
            }
            """;

        var emitted = CompileAndGetEmittedCSharp(source);

        var dropBody = ExtractMethodBody(emitted, "Drop", "Container");
        Assert.NotNull(dropBody);

        // Handle.Drop() should be auto-inserted.
        Assert.Contains("this.Handle.Drop()", dropBody);

        // Count should NOT have a Drop() call (int is not droppable).
        Assert.DoesNotContain("Count.Drop()", dropBody);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the body text of a method named <paramref name="methodName"/> inside
    /// a class named <paramref name="className"/> from emitted C# source.
    /// Returns the text between the method's opening and closing braces (inclusive).
    /// </summary>
    private static string? ExtractMethodBody(string emitted, string methodName, string className)
    {
        // Find the class
        var classIdx = emitted.IndexOf($"class {className}", StringComparison.Ordinal);
        if(classIdx < 0)
        {
            return null;
        }

        // Find the method within the class region
        var methodPattern = $"void {methodName}()";
        var searchStart = classIdx;
        var methodIdx = emitted.IndexOf(methodPattern, searchStart, StringComparison.Ordinal);
        if(methodIdx < 0)
        {
            return null;
        }

        // Find the opening brace of the method body
        var braceIdx = emitted.IndexOf('{', methodIdx);
        if(braceIdx < 0)
        {
            return null;
        }

        // Find the matching closing brace
        var depth = 1;
        var pos = braceIdx + 1;
        while(pos < emitted.Length && depth > 0)
        {
            if(emitted[pos] == '{')
            {
                depth++;
            }
            else if(emitted[pos] == '}')
            {
                depth--;
            }

            pos++;
        }

        return emitted[braceIdx..pos];
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var idx = 0;
        while((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }

        return count;
    }
}