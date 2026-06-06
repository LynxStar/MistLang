// Feature: drop-semantics, Property 4: Drop does not consume relevant types
// **Validates: Requirements 3.3**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Semantics;

/// <summary>
/// Example-based tests for Property 4: Drop does not consume relevant types.
///
/// Feature: drop-semantics, Property 4: Drop does not consume relevant types
/// **Validates: Requirements 3.3**
///
/// For any variable <c>v</c> of a relevant type, calling <c>v.Drop()</c> SHALL NOT make
/// <c>v</c> inaccessible — subsequent accesses to <c>v</c> SHALL compile without a
/// use-after-move (<c>MIST_DROP_USE_AFTER_MOVE</c>) or use-after-drop
/// (<c>MIST_DROP_USE_AFTER_DROP</c>) error, because relevant types are aliasable and
/// <c>Drop()</c> is idempotent rather than consuming.
///
/// Substructural discipline lives on the TYPE: each scenario declares a
/// <c>relevant class SharedResource</c> with a <c>Drop()</c> and an accessor, declares a
/// local of that type in <c>Main</c>, calls <c>Drop()</c>, and then accesses the variable.
/// Compilation runs fully in-process via a per-instance <see cref="CompilerDriver"/> whose
/// analyzer array includes the <see cref="DropAnalyzer"/>, and the resulting
/// <see cref="CompilerResult.Diagnostics"/> are inspected directly.
/// Spawns 0 external processes.
/// </summary>
public sealed class RelevantDropNotConsumedTests : IDisposable
{
    private const string UseAfterMoveId = "MIST_DROP_USE_AFTER_MOVE";
    private const string UseAfterDropId = "MIST_DROP_USE_AFTER_DROP";

    private readonly string _outputDir;

    public RelevantDropNotConsumedTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistRelevantDropPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Compile helper (driver includes DropAnalyzer) ───────────────────

    private ImmutableArray<Mist.Common.Diagnostics.Diagnostic> CompileAndGetDiagnostics(string source)
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
        return result.Diagnostics;
    }

    // The relevant type under test: aliasable, Drop() is idempotent and does NOT consume.
    private const string SharedResourceType = """
        relevant class SharedResource
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

    private static string Program(string body) => $$"""
        {{SharedResourceType}}
        class Program
        {
            public static void Main()
            {
        {{body}}
            }
        }
        """;

    // ─── Property 4: Drop does not consume relevant types ────────────────
    //
    // Each scenario calls Drop() on a relevant local then accesses it. None of
    // them should produce a use-after-move/use-after-drop diagnostic.

    public static IEnumerable<object[]> NonConsumingScenarios()
    {
        // Access via method call after a single Drop().
        yield return new object[]
        {
            "single drop then method-call access",
            """
                    SharedResource r = new SharedResource();
                    r.Drop();
                    int x = r.GetId();
            """,
        };

        // Multiple drops then access (Drop() is idempotent for relevant types).
        yield return new object[]
        {
            "multiple drops then access",
            """
                    SharedResource r = new SharedResource();
                    r.Drop();
                    r.Drop();
                    r.Drop();
                    int x = r.GetId();
            """,
        };

        // Drop() then access again via a second Drop() call (still accessible).
        yield return new object[]
        {
            "drop then drop again as access",
            """
                    SharedResource r = new SharedResource();
                    r.Drop();
                    r.Drop();
            """,
        };

        // Alias the relevant local, drop the original, then access via the alias
        // (relevant types are aliasable — no single-holder consume on Drop).
        yield return new object[]
        {
            "alias then drop original then access alias",
            """
                    SharedResource r = new SharedResource();
                    SharedResource s = r;
                    r.Drop();
                    int x = s.GetId();
            """,
        };
    }

    [Theory]
    [MemberData(nameof(NonConsumingScenarios))]
    public void RelevantDrop_DoesNotConsume_NoUseAfterDiagnostic(string label, string body)
    {
        var source = Program(body);

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            d => d.Id.Value == UseAfterMoveId || d.Id.Value == UseAfterDropId);

        // Label is included for failure readability via the xUnit data theory name.
        _ = label;
    }
}
