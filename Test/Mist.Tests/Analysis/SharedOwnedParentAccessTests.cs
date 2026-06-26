// Task 13.4 — Unit tests for shared/owned parent access rules.
//
// Tests the enforcement of Requirement 2 (owned field access through shared vs. owned
// parent bindings) and related per-field state behaviors:
//   - Shared parent: write with provenance succeeds (Req 2 AC 2)
//   - Shared parent: clone-read of IClonable field succeeds (Req 2 AC 3)
//   - Shared parent: move/borrow → MIST_OWN_SHARED_EXTRACT (Req 2 AC 4–5)
//   - Shared parent: read non-IClonable → MIST_OWN_SHARED_READ_NOT_CLONABLE (Req 2 AC 6)
//   - Owned parent: move out succeeds, field enters Moved (Req 2 AC 1, Req 16 AC 2)
//   - Owned parent: write with non-owned non-clonable source → provenance error (Req 8 AC 4)
//   - Method call on instance with partial field state (Moved) → rejected (Req 16 AC 23)
//
// All compilation is in-process via a single shared CompilerDriver.
// 0 external process spawns (test-process-spawning policy).
//
// Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 8.1, 8.4, 11.1, 11.7, 16.23

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for shared/owned parent access rules (Task 13.4).
/// Verifies that owned fields enforce extraction blocking through shared parents
/// and permit full access through owned parents with per-field state tracking.
///
/// <b>Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 8.1, 8.4, 11.1, 11.7, 16.23</b>
/// </summary>
public sealed class SharedOwnedParentAccessTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_own_access_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Diagnostic? FindById(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.FirstOrDefault(d => d.Id.Value == id);

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Shared parent → write with owned source succeeds (Req 2 AC 2)
    //
    // A shared (non-owned) parent can write to an owned field as long as the
    // source satisfies provenance (yields an owned value).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_WriteWithOwnedSource_Succeeds()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new Container("initial");
                    c.Data = new string("updated");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No shared-extract or provenance errors expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_REQUIRES_OWNED"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Shared parent → clone-read of IClonable field succeeds (Req 2 AC 3)
    //
    // Reading an owned field through a shared parent is permitted when the
    // field's type implements IClonable — the read produces a clone.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_CloneReadOfIClonableField_Succeeds()
    {
        // string implements IClonable, so reading through shared parent succeeds.
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new Container("hello");
                    var copy = c.Data;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No shared-extract or shared-read-not-clonable errors expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_READ_NOT_CLONABLE"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Shared parent → move out of owned field → MIST_OWN_SHARED_EXTRACT
    //    (Req 2 AC 4–5, Req 11 AC 1)
    //
    // Moving data out of an owned field through a shared parent is blocked
    // because the shared binding cannot guarantee exclusive access.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_MoveOutOfOwnedField_EmitsSharedExtract()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new Container("hello");
                    owned var extracted = c.Data;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT");

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("Data", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Shared parent → borrow owned field → MIST_OWN_SHARED_EXTRACT
    //    (Req 2 AC 4–5, Req 11 AC 1)
    //
    // Borrowing an owned field through a shared parent is blocked —
    // exclusive access requires an owned parent.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_BorrowOwnedField_EmitsSharedExtract()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new Container("hello");
                    borrowed var b = c.Data;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT");

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("Data", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Shared parent → read non-IClonable field → MIST_OWN_SHARED_READ_NOT_CLONABLE
    //    (Req 2 AC 6, Req 11 AC 7)
    //
    // Reading an owned field through a shared parent when the field's type
    // does NOT implement IClonable is blocked — the field cannot be cloned
    // and cannot be moved out.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_ReadNonClonableField_EmitsSharedReadNotClonable()
    {
        // Widget has an array field which blocks IClonable auto-derivation.
        const string source = """
            class Widget
            {
                public int[] parts;
            }

            class Container
            {
                public owned Widget item;

                public Container(owned Widget w)
                {
                    this.item = w;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new Container(new Widget());
                    var w = c.item;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_SHARED_READ_NOT_CLONABLE");

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("item", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Owned parent → move out succeeds, field enters Moved
    //    (Req 2 AC 1, Req 16 AC 2)
    //
    // Through an owned parent, all ownership operations are permitted
    // including moving out of a field. The field enters Moved state.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedParent_MoveOutOfField_Succeeds()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new Container("hello");
                    owned var extracted = c.Data;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No extraction error — owned parent permits move-out.
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_REQUIRES_OWNED"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Owned parent → write with non-owned non-clonable source → provenance error
    //    (Req 8 AC 4, Req 11 AC 3)
    //
    // Even through an owned parent, writing to an owned field requires
    // provenance — a non-owned, non-clonable source is rejected.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedParent_WriteNonOwnedNonClonableSource_EmitsProvenanceError()
    {
        // Widget has an array field which blocks IClonable auto-derivation.
        const string source = """
            class Widget
            {
                public int[] parts;
            }

            class Container
            {
                public owned Widget item;

                public Container(owned Widget w)
                {
                    this.item = w;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new Container(new Widget());
                    var shared = new Widget();
                    c.item = shared;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // The source 'shared' is not owned and Widget is not IClonable → provenance error.
        var reqOwned = FindById(diagnostics, "MIST_OWN_REQUIRES_OWNED");
        var notClonable = FindById(diagnostics, "MIST_OWN_NOT_CLONABLE");

        // One of these two provenance diagnostics should fire.
        Assert.True(reqOwned is not null || notClonable is not null,
            "Expected MIST_OWN_REQUIRES_OWNED or MIST_OWN_NOT_CLONABLE for non-owned, non-clonable source assignment to owned field");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 8. Method call on instance with partial field state (accessed field is Moved)
    //    → rejected (Req 16 AC 23)
    //
    // When an owned field has been moved out, calling a method that accesses
    // that field is rejected because the field is in Moved state.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MethodCall_AccessedFieldIsMoved_Rejected()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }

                public string GetData()
                {
                    return this.Data;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new Container("hello");
                    owned var extracted = c.Data;
                    c.GetData();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // After moving c.Data out, calling GetData() (which reads Data) should be rejected.
        var useAfterMove = FindById(diagnostics, "MIST_OWN_USE_AFTER_MOVE");

        Assert.NotNull(useAfterMove);
        Assert.Equal(DiagnosticSeverity.Error, useAfterMove!.Severity);
        Assert.Contains("Data", useAfterMove.Message, StringComparison.Ordinal);
    }
}