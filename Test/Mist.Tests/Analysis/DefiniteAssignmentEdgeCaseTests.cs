// Feature: definite-assignment — Unit tests for definite-assignment edge cases
// **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 1.7, 2.2, 2.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for definite-assignment edge cases covering:
/// - Field: all-branch assign (ok), one-branch assign (error), read-after-assign (ok),
///   this-escape (error), initializer/synthetic-default pre-assigned (ok),
///   end-of-ctor not-assigned still fires.
/// - Local: initializer (ok), conditional merge with re-assignment (ok),
///   compound after init (ok).
///
/// **Validates: Requirements 1.2, 1.3, 1.5, 1.6, 1.7, 2.2, 2.4**
/// </summary>
public sealed class DefiniteAssignmentEdgeCaseTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "mist_da_edge_" + Guid.NewGuid().ToString("N"));
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
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.Any(d => d.Id.Value == id);

    private static bool HasDiagnosticForField(
        ImmutableArray<Diagnostic> diagnostics, string diagnosticId, string fieldName) =>
        diagnostics.Any(d =>
            d.Id.Value == diagnosticId &&
            d.Message.Contains($"'{fieldName}'"));

    private static bool HasNoErrors(ImmutableArray<Diagnostic> diagnostics) =>
        !diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics) =>
        string.Join("; ", diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id.Value}: {d.Message}"));

    // ═══════════════════════════════════════════════════════════════════════
    // FIELD TESTS — Constructor definite-assignment
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A field assigned on every branch of an if/else is definitely assigned
    /// at the merge point — no read-before-assignment diagnostic.
    /// Validates: Requirement 1.5
    /// </summary>
    [Fact]
    public void Field_AssignedOnAllBranches_NoDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            public abstract IService
            {
            }

            public class Widget
            {
                public IService svc;

                public Widget(IService s, bool flag)
                {
                    if (flag)
                    {
                        this.svc = s;
                    }
                    else
                    {
                        this.svc = s;
                    }
                    var sink = this.svc;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(
            HasDiagnosticForField(diagnostics, "MIST_FIELD_READ_BEFORE_ASSIGNMENT", "svc"),
            $"Expected no MIST_FIELD_READ_BEFORE_ASSIGNMENT for 'svc' but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// A field assigned on only one branch of an if/else is NOT definitely
    /// assigned at the merge point — read-before-assignment diagnostic fires.
    /// Validates: Requirement 1.5
    /// </summary>
    [Fact]
    public void Field_AssignedOnOneBranch_EmitsReadBeforeAssignment()
    {
        const string source = """
            namespace TestProgram;

            public abstract IService
            {
            }

            public class Widget
            {
                public IService svc;

                public Widget(IService s, bool flag)
                {
                    if (flag)
                    {
                        this.svc = s;
                    }
                    else
                    {
                        // not assigned on this path
                    }
                    var sink = this.svc;
                    this.svc = s;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(
            HasDiagnosticForField(diagnostics, "MIST_FIELD_READ_BEFORE_ASSIGNMENT", "svc"),
            $"Expected MIST_FIELD_READ_BEFORE_ASSIGNMENT for 'svc' but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// A field read after it has been definitively assigned in the same
    /// constructor — no diagnostic.
    /// Validates: Requirement 1.6
    /// </summary>
    [Fact]
    public void Field_ReadAfterAssign_NoDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            public abstract IService
            {
            }

            public class Widget
            {
                public IService svc;

                public Widget(IService s)
                {
                    this.svc = s;
                    var sink = this.svc;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(
            HasDiagnosticForField(diagnostics, "MIST_FIELD_READ_BEFORE_ASSIGNMENT", "svc"),
            $"Expected no MIST_FIELD_READ_BEFORE_ASSIGNMENT for 'svc' but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// Passing <c>this</c> as an argument before all non-defaultable fields are
    /// assigned is a this-escape — emits read-before-assignment for unassigned fields.
    /// Validates: Requirement 1.8 (this-escape)
    /// </summary>
    [Fact]
    public void Field_ThisEscape_EmitsReadBeforeAssignment()
    {
        const string source = """
            namespace TestProgram;

            public abstract IService
            {
            }

            public class Registry
            {
                public static void Register(Widget w)
                {
                }
            }

            public class Widget
            {
                public IService svc;

                public Widget(IService s)
                {
                    Registry.Register(this);
                    this.svc = s;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(
            HasDiagnosticForField(diagnostics, "MIST_FIELD_READ_BEFORE_ASSIGNMENT", "svc"),
            $"Expected MIST_FIELD_READ_BEFORE_ASSIGNMENT for 'svc' (this-escape) but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// A field with an explicit initializer is treated as pre-assigned at the
    /// start of the constructor — reading it produces no diagnostic.
    /// Validates: Requirement 1.2
    /// </summary>
    [Fact]
    public void Field_WithInitializer_PreAssigned_NoDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            public class Widget
            {
                public int value = 42;

                public Widget()
                {
                    var sink = this.value;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(
            HasDiagnosticForField(diagnostics, "MIST_FIELD_READ_BEFORE_ASSIGNMENT", "value"),
            $"Expected no MIST_FIELD_READ_BEFORE_ASSIGNMENT for 'value' but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// A field whose type is defaultable and has no explicit initializer is
    /// treated as pre-assigned (synthetic default injection) — no diagnostic.
    /// Validates: Requirement 1.3
    /// </summary>
    [Fact]
    public void Field_SyntheticDefault_PreAssigned_NoDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            public class Widget
            {
                public int count;

                public Widget()
                {
                    var sink = this.count;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(
            HasDiagnosticForField(diagnostics, "MIST_FIELD_READ_BEFORE_ASSIGNMENT", "count"),
            $"Expected no MIST_FIELD_READ_BEFORE_ASSIGNMENT for 'count' (defaultable) but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// The end-of-constructor MIST_FIELD_NOT_DEFINITELY_ASSIGNED check still fires
    /// for a non-defaultable field not assigned on all paths, independently of the
    /// read-before-assignment check.
    /// Validates: Requirement 1.7
    /// </summary>
    [Fact]
    public void Field_EndOfConstructor_NotAssigned_EmitsNotDefinitelyAssigned()
    {
        const string source = """
            namespace TestProgram;

            public abstract IService
            {
            }

            public class Widget
            {
                public IService svc;

                public Widget(IService s, bool flag)
                {
                    if (flag)
                    {
                        this.svc = s;
                    }
                    else
                    {
                        // not assigned
                    }
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.True(
            HasDiagnosticForField(diagnostics, "MIST_FIELD_NOT_DEFINITELY_ASSIGNED", "svc"),
            $"Expected MIST_FIELD_NOT_DEFINITELY_ASSIGNED for 'svc' but got: {FormatDiagnostics(diagnostics)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // LOCAL TESTS — No false positives (grammar requires initializers)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A local declared with an initializer is definitely assigned immediately —
    /// reading it after declaration produces no diagnostic.
    /// Validates: Requirement 2.2
    /// </summary>
    [Fact]
    public void Local_WithInitializer_NoDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                }

                public static void Process()
                {
                    var value = 42;
                    var sink = value;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(
            HasDiagnostic(diagnostics, "MIST_LOCAL_READ_BEFORE_ASSIGNMENT"),
            $"Expected no MIST_LOCAL_READ_BEFORE_ASSIGNMENT but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// A local re-assigned on every branch of a conditional is definitely assigned
    /// at the merge point — no diagnostic when read after the merge.
    /// Validates: Requirement 2.4
    /// </summary>
    [Fact]
    public void Local_ConditionalMerge_AllBranchesAssigned_NoDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                }

                public static void Compute(bool flag)
                {
                    var result = 0;
                    if (flag)
                    {
                        result = 1;
                    }
                    else
                    {
                        result = 2;
                    }
                    var sink = result;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(
            HasDiagnostic(diagnostics, "MIST_LOCAL_READ_BEFORE_ASSIGNMENT"),
            $"Expected no MIST_LOCAL_READ_BEFORE_ASSIGNMENT but got: {FormatDiagnostics(diagnostics)}");
    }

    /// <summary>
    /// A compound assignment (read-then-write) on an already-initialized local
    /// produces no diagnostic.
    /// Validates: Requirement 2.2 (local with initializer is pre-assigned)
    /// </summary>
    [Fact]
    public void Local_CompoundAssignAfterInit_NoDiagnostic()
    {
        const string source = """
            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                }

                public static void Accumulate()
                {
                    var total = 0;
                    total = total + 5;
                    var sink = total;
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        Assert.False(
            HasDiagnostic(diagnostics, "MIST_LOCAL_READ_BEFORE_ASSIGNMENT"),
            $"Expected no MIST_LOCAL_READ_BEFORE_ASSIGNMENT but got: {FormatDiagnostics(diagnostics)}");
    }
}
