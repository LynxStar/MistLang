// Unit tests for synthetic drop inference exclusion of IDisposable fields.
// **Validates: Requirements 5.1, 5.2, 5.3**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="SyntheticDropInferenceEngine"/> exclusion of IDisposable fields.
///
/// Validates that the synthetic drop inference engine does NOT consider fields whose type
/// has <c>IsDisposableDrop = true</c> as droppable, and therefore does not synthesize a
/// <c>Drop()</c> method based on IDisposable fields alone.
///
/// **Validates: Requirements 5.1, 5.2, 5.3**
/// </summary>
public sealed class SyntheticDropInferenceExclusionTests : IDisposable
{
    private readonly string outputDir;

    public SyntheticDropInferenceExclusionTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistSyntheticDropExclusion_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
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

    // ─── Compile helper ──────────────────────────────────────────────────

    private ISemanticModel? CompileAndCapture(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
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
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        _ = driver.Compile(options);
        return capture.Captured;
    }

    private static ClassType? FindClassType(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .Where(s => s.Origin == SymbolOrigin.UserCode
                && s.Kind == SymbolKind.Class
                && s.Name == name)
            .Select(s => s.DeclaredType)
            .OfType<ClassType>()
            .FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Mist type with only an IDisposable CLR field → no synthetic Drop
    // Validates: Requirement 5.1, 5.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MistType_WithOnlyDisposableField_NoSyntheticDrop()
    {
        // A Mist type with a single field whose type is an IDisposable CLR type.
        // The inference engine must NOT synthesize a Drop() for the container
        // because IsDisposableDrop fields are excluded from inference.
        var source = """
            using System.IO;

            class Container
            {
                MemoryStream stream;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var container = FindClassType(model!, "Container");
        Assert.NotNull(container);

        // No synthetic drop should be synthesized — the IDisposable field is excluded.
        Assert.False(container!.HasDrop, "Container with only IDisposable CLR fields must NOT have a synthesized Drop().");
        Assert.Null(container.SyntheticDropFields);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Mist type with IDisposable CLR field + explicit Mist Drop field
    //       → only explicit field appears in synthetic drop list
    // Validates: Requirement 5.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MistType_WithDisposableAndExplicitDropField_OnlyExplicitInList()
    {
        // A Mist type with both:
        // - An IDisposable CLR field (MemoryStream) — excluded from inference
        // - A field with an explicit Mist Drop() method (Leaf) — included
        // The inference engine must synthesize a Drop() listing only the explicit field.
        var source = """
            using System.IO;

            linear class Leaf
            {
                public void Drop()
                {
                }
            }
            linear class Container
            {
                MemoryStream stream;
                Leaf leaf;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var container = FindClassType(model!, "Container");
        Assert.NotNull(container);

        // Synthetic drop should be synthesized for only the explicitly-droppable field.
        Assert.True(container!.HasDrop, "Container with an explicitly-droppable field must have a synthesized Drop().");
        Assert.NotNull(container.SyntheticDropFields);

        var fieldNames = container.SyntheticDropFields!.Select(f => f.Name).ToList();
        Assert.Equal(new[] { "leaf" }, fieldNames);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: Mist type with multiple fields, mix of IDisposable and explicit
    //       drops → correct field list (only explicit drops appear)
    // Validates: Requirements 5.1, 5.2, 5.3
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MistType_MixedFields_OnlyExplicitDropsInSyntheticList()
    {
        // A Mist type with a mix of:
        // - IDisposable CLR fields (excluded from inference)
        // - Explicitly droppable Mist fields (included)
        // - Non-droppable fields (excluded)
        // The inference engine must include only the explicitly-droppable fields
        // in the SyntheticDropFields list, in declaration order.
        var source = """
            using System.IO;

            linear class Resource
            {
                public void Drop()
                {
                }
            }
            linear class Handler
            {
                public void Drop()
                {
                }
            }
            linear class Container
            {
                MemoryStream stream1;
                Resource res;
                int count;
                MemoryStream stream2;
                Handler handler;
            }
            class Program
            {
                public static void Main()
                {
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var container = FindClassType(model!, "Container");
        Assert.NotNull(container);

        // Synthetic drop should include only the explicitly-droppable fields in declaration order.
        Assert.True(container!.HasDrop, "Container with explicitly-droppable fields must have a synthesized Drop().");
        Assert.NotNull(container.SyntheticDropFields);

        var fieldNames = container.SyntheticDropFields!.Select(f => f.Name).ToList();
        Assert.Equal(new[] { "res", "handler" }, fieldNames);
    }
}