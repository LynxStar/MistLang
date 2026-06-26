// Feature: owned-fields, Property 4: Shared parent blocks extraction (Req 2 AC 4–5)
//
// **Validates: Requirements 2.2, 2.3, 2.4, 2.5, 2.6**
//
// For all shared parent bindings and owned fields:
//   - Extract (move/borrow/freeze) → MIST_OWN_SHARED_EXTRACT
//   - Write with provenance → succeeds (no error)
//   - Clone-read (field type is IClonable) → succeeds (no error)
//   - Read (field type NOT IClonable) → MIST_OWN_SHARED_READ_NOT_CLONABLE
//
// Since generating valid Mist source requires careful construction, this uses a
// Theory/InlineData approach that enumerates the operation types exhaustively.
// All compilation is in-process via a single shared CompilerDriver.
// 0 external process spawns (test-process-spawning policy).

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
/// Property 4: Shared parent blocks extraction (Req 2 AC 4–5).
///
/// For all shared parent bindings and owned fields:
///   extract → MIST_OWN_SHARED_EXTRACT,
///   write with provenance → succeeds,
///   clone-read → succeeds iff IClonable,
///   read non-IClonable → MIST_OWN_SHARED_READ_NOT_CLONABLE.
///
/// Since the input space is finite (6 operation types), this uses deterministic
/// [Theory] tests covering all operation categories exhaustively.
///
/// <b>Validates: Requirements 2.2, 2.3, 2.4, 2.5, 2.6</b>
/// </summary>
public sealed class SharedParentExtractionBlockingPropertyTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_shared_extract_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Shared type declarations ────────────────────────────────────────

    /// <summary>
    /// Container with an IClonable owned field (string is IClonable).
    /// Used for scenarios 1–5 (move, borrow, freeze, write, clone-read).
    /// </summary>
    private const string ClonableFieldContainer = """
        class Container
        {
            public owned string Data;

            public Container(owned string data)
            {
                this.Data = data;
            }
        }
        """;

    /// <summary>
    /// Widget type that is NOT IClonable (has an array field which blocks auto-derivation).
    /// Used for scenario 6 (read non-IClonable).
    /// </summary>
    private const string NonClonableWidget = """
        class Widget
        {
            public int[] parts;
        }

        class WidgetContainer
        {
            public owned Widget item;

            public WidgetContainer(owned Widget w)
            {
                this.item = w;
            }
        }
        """;

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4a: Shared parent + move → MIST_OWN_SHARED_EXTRACT
    //
    // Moving out of an owned field through a shared parent binding is blocked.
    // The shared binding cannot guarantee exclusive access required for moves.
    //
    // **Validates: Requirements 2.4, 2.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_MoveOwnedField_EmitsSharedExtract()
    {
        var source = ClonableFieldContainer + """

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
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4b: Shared parent + borrow → MIST_OWN_SHARED_EXTRACT
    //
    // Borrowing an owned field through a shared parent is blocked — exclusive
    // access requires an owned parent.
    //
    // **Validates: Requirements 2.4, 2.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_BorrowOwnedField_EmitsSharedExtract()
    {
        var source = ClonableFieldContainer + """

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
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4c: Shared parent + freeze → MIST_OWN_SHARED_EXTRACT
    //
    // Freezing an owned field through a shared parent is blocked — taking a
    // frozen reference to an owned field requires exclusive access from an
    // owned parent.
    //
    // **Validates: Requirements 2.4, 2.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_FreezeOwnedField_EmitsSharedExtract()
    {
        var source = ClonableFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    var c = new Container("hello");
                    frozen var f = c.Data;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT");

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4d: Shared parent + write with provenance → succeeds
    //
    // Writing to an owned field through a shared parent is permitted as long
    // as the source satisfies provenance (yields an owned value). Multiple
    // aliases can independently write owned values into the field.
    //
    // **Validates: Requirement 2.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_WriteWithProvenance_NoError()
    {
        var source = ClonableFieldContainer + """

            class Program
            {
                public static void Main()
                {
                    var c = new Container("hello");
                    c.Data = new string("updated");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // No extraction or provenance errors expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_REQUIRES_OWNED"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_READ_NOT_CLONABLE"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4e: Shared parent + clone-read (field type is IClonable) → succeeds
    //
    // Reading an owned field through a shared parent is permitted when the
    // field's type implements IClonable — the read produces a clone.
    //
    // **Validates: Requirement 2.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_CloneReadIClonableField_NoError()
    {
        // string implements IClonable, so clone-read through shared parent succeeds.
        var source = ClonableFieldContainer + """

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

        // No extraction or read-not-clonable errors expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_EXTRACT"));
        Assert.Null(FindById(diagnostics, "MIST_OWN_SHARED_READ_NOT_CLONABLE"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4f: Shared parent + read (field type NOT IClonable) →
    //              MIST_OWN_SHARED_READ_NOT_CLONABLE
    //
    // Reading an owned field through a shared parent when the field's type
    // does NOT implement IClonable is blocked — the field cannot be cloned
    // and cannot be moved out without an owned parent.
    //
    // **Validates: Requirement 2.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedParent_ReadNonClonableField_EmitsSharedReadNotClonable()
    {
        // Widget has an array field which blocks IClonable auto-derivation.
        var source = NonClonableWidget + """

            class Program
            {
                public static void Main()
                {
                    var c = new WidgetContainer(new Widget());
                    var w = c.item;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var diag = FindById(diagnostics, "MIST_OWN_SHARED_READ_NOT_CLONABLE");

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
    }
}