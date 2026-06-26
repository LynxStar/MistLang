// Feature: ownership-v2, Task 5.3 — unit tests for the MethodTierVisibilityFilter
// decision table (design Component 4).
//
// Decision table (Req 2 AC 1-4, Req 3 AC 1):
//
//   | Receiver binding | Receiver type   | Method kind | Result        |
//   |------------------|-----------------|-------------|---------------|
//   | owned            | owned type      | readonly    | Allowed       |
//   | owned            | owned type      | mutable     | Allowed       |
//   | owned            | non-owned type  | readonly    | Allowed       |
//   | owned            | non-owned type  | mutable     | HiddenMutable |
//   | owned            | value type      | any         | Allowed       |
//   | unrestricted     | any             | any         | Allowed       |
//
// Each row is exercised with an explicit [Theory]/[InlineData] case. A single Mist
// program declaring an owned class, a non-owned class, and a (non-owned) struct —
// each with one readonly and one mutable method — is compiled ONCE in-process via a
// shared CompilerDriver (test-process-spawning policy: 0 external process spawns).
// The semantic model is captured so each case can locate its method symbol and the
// receiver type and invoke MethodTierVisibilityFilter.Resolve directly.
//
// The ReadonlyAnalyzer is wired into the pipeline so each method's
// Symbol.ReadonlyStatus is populated (the filter classifies readonly vs mutable from
// that status).

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
/// Unit tests for the <see cref="MethodTierVisibilityFilter"/> decision table
/// (ownership-v2 design Component 4, Task 5.3).
///
/// **Validates: Requirements 2.1, 2.2, 2.4, 3.1**
/// </summary>
public sealed class MethodTierVisibilityDecisionTableTests : IDisposable
{
    private readonly string _scratchDir;

    public MethodTierVisibilityDecisionTableTests()
    {
        _scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-method-tier-table-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_scratchDir))
        {
            try { Directory.Delete(_scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── In-process compile + capture infrastructure ─────────────────────

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the test can inspect the
    /// constructed model (method symbols with ReadonlyStatus, type models) after the
    /// driver builds it.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();
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
            var model = _inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            Captured = model;
            return model;
        }
    }

    // A single program covering all three receiver-type categories. Each type has
    // one explicitly-readonly method and one mutable method (mutates a field).
    private const string FixtureSource = """
        namespace TestProgram;

        // Owned type (cooperation contract) — full method access in owned bindings.
        owned class OwnedBox
        {
            public int Value;
            public OwnedBox(int value)
            {
                this.Value = value;
            }
            public readonly int OwnedReadonly()
            {
                return Value;
            }
            public void OwnedMutable(int v)
            {
                this.Value = v;
            }
        }

        // Non-owned reference type — readonly methods visible, mutable hidden.
        class PlainBox
        {
            public int Value;
            public PlainBox(int value)
            {
                this.Value = value;
            }
            public readonly int PlainReadonly()
            {
                return Value;
            }
            public void PlainMutable(int v)
            {
                this.Value = v;
            }
        }

        // Non-owned value type — all methods visible (copied on access).
        struct ValueBox
        {
            public int Value;
            public ValueBox(int value)
            {
                this.Value = value;
            }
            public readonly int ValueReadonly()
            {
                return Value;
            }
            public void ValueMutable(int v)
            {
                this.Value = v;
            }
        }

        class Program
        {
            public static void Main()
            {
            }
        }
        """;

    private ISemanticModel? CompileFixtureAndCapture()
    {
        var sourcePath = Path.Combine(_scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, FixtureSource);

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
            ProjectFolder: _scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _scratchDir,
            BinDirectory: Path.Combine(_scratchDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        driver.Compile(options);
        return capture.Captured;
    }

    private static Symbol? FindMethod(ISemanticModel model, string methodName) =>
        model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == methodName);

    private static IType? FindType(ISemanticModel model, string typeName) =>
        model.AllSymbols
            .Where(s => (s.Kind == SymbolKind.Class || s.Kind == SymbolKind.Struct)
                && s.Name == typeName)
            .Select(s => s.DeclaredType)
            .OfType<IType>()
            .FirstOrDefault(t =>
                (t is ClassType ct && ct.ContainingNamespace == "TestProgram")
                || (t is StructType st && st.ContainingNamespace == "TestProgram"));

    // ═══════════════════════════════════════════════════════════════════════
    // Decision table — one [InlineData] per row (with both method kinds for the
    // value-type row, and two distinct types for the unrestricted-binding row).
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    // Row 1: owned binding, owned type, readonly method -> Allowed (Req 3 AC 1)
    [InlineData("OwnedBox", "OwnedReadonly", true, MethodVisibility.Allowed)]
    // Row 2: owned binding, owned type, mutable method -> Allowed (Req 3 AC 1)
    [InlineData("OwnedBox", "OwnedMutable", true, MethodVisibility.Allowed)]
    // Row 3: owned binding, non-owned type, readonly method -> Allowed (Req 2 AC 1)
    [InlineData("PlainBox", "PlainReadonly", true, MethodVisibility.Allowed)]
    // Row 4: owned binding, non-owned type, mutable method -> HiddenMutable (Req 2 AC 2/4)
    [InlineData("PlainBox", "PlainMutable", true, MethodVisibility.HiddenMutable)]
    // Row 5: owned binding, value type, readonly method -> Allowed (value copy)
    [InlineData("ValueBox", "ValueReadonly", true, MethodVisibility.Allowed)]
    // Row 5: owned binding, value type, mutable method -> Allowed (value copy)
    [InlineData("ValueBox", "ValueMutable", true, MethodVisibility.Allowed)]
    // Row 6: unrestricted binding, non-owned type, mutable method -> Allowed
    [InlineData("PlainBox", "PlainMutable", false, MethodVisibility.Allowed)]
    // Row 6: unrestricted binding, owned type, mutable method -> Allowed
    [InlineData("OwnedBox", "OwnedMutable", false, MethodVisibility.Allowed)]
    public void DecisionTableRow_ResolvesToExpectedVisibility(
        string typeName,
        string methodName,
        bool receiverIsOwnedBinding,
        MethodVisibility expected)
    {
        var model = CompileFixtureAndCapture();
        Assert.NotNull(model);

        var method = FindMethod(model!, methodName);
        Assert.True(method is not null, $"Method '{methodName}' not found in the semantic model.");

        var receiverType = FindType(model!, typeName);
        Assert.True(receiverType is not null, $"Type '{typeName}' not found in the semantic model.");

        var verdict = MethodTierVisibilityFilter.Resolve(
            method!,
            receiverType!,
            receiverIsOwnedBinding,
            model!);

        Assert.Equal(expected, verdict);
    }
}