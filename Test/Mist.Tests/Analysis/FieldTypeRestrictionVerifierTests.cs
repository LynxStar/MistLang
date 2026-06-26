// Task 4.2 — Unit tests for `FieldTypeRestrictionVerifier`.
//
// Tests the standalone verifier by compiling Mist programs through the full driver
// (which runs IClonable derivation), capturing the semantic model, finding the owned
// type under test, and calling FieldTypeRestrictionVerifier.Verify() directly.
//
// This approach ensures IClonable flags are correctly set (Req 4 AC 5) before the
// field-type restriction is evaluated, while isolating the verifier from orchestrator
// wiring (which is a later task).
//
// All compilation is in-process via a single shared CompilerDriver.
// 0 external process spawns (test-process-spawning policy).
//
// Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.6, 5.2, 5.3, 11.2

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for <see cref="FieldTypeRestrictionVerifier"/> (Task 4.2).
/// Verifies that owned types with valid field types pass, while owned types with
/// non-owned reference type fields or unconstrained generic type parameter fields
/// produce <c>MIST_OWN_FIELD_TYPE_VIOLATION</c> diagnostics.
///
/// <b>Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.6, 5.2, 5.3, 11.2</b>
/// </summary>
public sealed class FieldTypeRestrictionVerifierTests : IDisposable
{
    private const string FieldTypeViolationId = "MIST_OWN_FIELD_TYPE_VIOLATION";

    private readonly string scratchDir;

    public FieldTypeRestrictionVerifierTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist_field_restrict_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();

        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Compiles the given source through the full driver, captures the semantic model,
    /// and returns it. IClonable derivation runs as part of the driver pipeline.
    /// </summary>
    private ISemanticModel CompileAndCapture(string source)
    {
        var filePath = Path.Combine(scratchDir, $"Program_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(
                new TypeCheckerAnalyzer(),
                new ReadonlyAnalyzer(),
                new OwnershipAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        driver.Compile(options);

        Assert.NotNull(capture.Captured);
        return capture.Captured!;
    }

    /// <summary>
    /// Finds the first owned type with the given name in the semantic model.
    /// </summary>
    private static IType? FindOwnedType(ISemanticModel model, string typeName)
    {
        return model.AllSymbols
            .Where(s => (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct)
                && s.Name == typeName)
            .Select(s => s.DeclaredType)
            .OfType<IType>()
            .FirstOrDefault(t => t switch
            {
                ClassType ct => ct.IsOwnedType,
                StructType st => st.IsOwnedType,
                _ => false,
            });
    }

    /// <summary>
    /// Runs <see cref="FieldTypeRestrictionVerifier.Verify"/> on the named owned type
    /// and returns the diagnostics produced.
    /// </summary>
    private ImmutableArray<Diagnostic> VerifyType(string source, string typeName)
    {
        var model = CompileAndCapture(source);
        var ownedType = FindOwnedType(model, typeName);
        Assert.NotNull(ownedType);

        var diagnostics = new DiagnosticBag();
        FieldTypeRestrictionVerifier.Verify(ownedType!, model, diagnostics);
        return diagnostics.ToImmutableArray();
    }

    private static Diagnostic? FindFieldTypeViolation(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.FirstOrDefault(d => d.Id.Value == FieldTypeViolationId);

    private static IEnumerable<Diagnostic> FindAllFieldTypeViolations(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Where(d => d.Id.Value == FieldTypeViolationId);

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Owned type with all-owned-type fields → passes (Req 4 AC 1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllOwnedTypeFields_NoDiagnostic()
    {
        const string source = """
            owned class Inner
            {
                public int Value;
            }

            owned class Outer
            {
                public Inner child;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = VerifyType(source, "Outer");

        Assert.Empty(FindAllFieldTypeViolations(diagnostics));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Owned type with all-IClonable fields → passes (Req 4 AC 2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllIClonableFields_NoDiagnostic()
    {
        // 'Config' has only primitive fields, so it auto-derives IClonable.
        const string source = """
            class Config
            {
                public int Timeout;
                public Config(int timeout)
                {
                    this.Timeout = timeout;
                }
            }

            owned class Vault
            {
                public Config setting;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = VerifyType(source, "Vault");

        Assert.Empty(FindAllFieldTypeViolations(diagnostics));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Owned type with value-type fields → passes (Req 4 AC 3)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValueTypeFields_NoDiagnostic()
    {
        const string source = """
            owned class Counter
            {
                public int Value;
                public int Total;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = VerifyType(source, "Counter");

        Assert.Empty(FindAllFieldTypeViolations(diagnostics));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Owned type with mixed valid fields → passes
    //    (Req 4 AC 1 + AC 2 + AC 3 combined)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MixedValidFields_NoDiagnostic()
    {
        // Mix: owned-type field + IClonable field + value-type field
        const string source = """
            owned class OwnedChild
            {
                public int X;
            }

            class ClonableData
            {
                public int Value;
                public ClonableData(int v)
                {
                    this.Value = v;
                }
            }

            owned class Mixed
            {
                public OwnedChild child;
                public ClonableData data;
                public int count;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = VerifyType(source, "Mixed");

        Assert.Empty(FindAllFieldTypeViolations(diagnostics));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Owned type with non-owned reference type field (not IClonable)
    //    → MIST_OWN_FIELD_TYPE_VIOLATION (Req 4 AC 4, Req 11 AC 2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NonOwnedNonClonableField_EmitsViolation()
    {
        // 'Gadget' has an array field, which blocks IClonable auto-derivation.
        // It is not an owned type. So it violates the field-type restriction.
        const string source = """
            class Gadget
            {
                public int[] data;
            }

            owned class Holder
            {
                public Gadget item;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = VerifyType(source, "Holder");
        var diag = FindFieldTypeViolation(diagnostics);

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("item", diag.Message, StringComparison.Ordinal);
        Assert.Contains("Gadget", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. Owned type with unconstrained generic type parameter field → error
    //    (Req 4 AC 6)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnconstrainedGenericTypeParamField_EmitsViolation()
    {
        const string source = """
            owned class Box<T>
            {
                public T content;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = VerifyType(source, "Box");
        var diag = FindFieldTypeViolation(diagnostics);

        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("content", diag.Message, StringComparison.Ordinal);
        Assert.Contains("T", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Multiple violations in same type → each reported independently
    //    (Req 5 AC 3, Req 11 AC 6)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MultipleViolations_EachReportedIndependently()
    {
        // 'Gadget' and 'Widget' both have array fields → not clonable, not owned.
        const string source = """
            class Gadget
            {
                public int[] data;
            }

            class Widget
            {
                public int[] parts;
            }

            owned class Container
            {
                public Gadget first;
                public Widget second;
                public int validValue;
            }

            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var diagnostics = VerifyType(source, "Container");
        var violations = FindAllFieldTypeViolations(diagnostics).ToList();

        // Both 'first' and 'second' should produce violations; 'validValue' should not.
        Assert.Equal(2, violations.Count);

        // Verify each violation identifies its specific field.
        var messages = violations.Select(d => d.Message).ToList();
        Assert.Contains(messages, m => m.Contains("first", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("second", StringComparison.Ordinal));

        // 'validValue' (int, a value type) should not appear in any violation.
        Assert.DoesNotContain(messages, m => m.Contains("validValue", StringComparison.Ordinal));
    }
}