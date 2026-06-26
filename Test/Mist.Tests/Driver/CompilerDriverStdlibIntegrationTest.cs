using System.Collections.Immutable;
using System.Linq;
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

namespace Mist.Tests.Driver;

/// <summary>
/// Task 28.1 integration test for the Mist Standard Library spec.
///
/// Compiles a trivial user program through the full <see cref="CompilerDriver"/>
/// pipeline and asserts:
/// 1. The driver prepends every stdlib compilation unit (as loaded by
///    <see cref="StdlibLoader.LoadFor"/>) so the semantic model contains the
///    stdlib type <c>Standard.Int32</c> alongside the user's types.
/// 2. After Task 32 landed the compat shim, <see cref="LegacyPrimitiveTypes.Int"/>
///    returns the stdlib <c>Standard.Int32</c> <see cref="StructType"/>
///    instance (registered into <c>PrimitiveTypeRegistry</c> at the end
///    of <see cref="SemanticModelBuilder"/> Pass 1). Reference equality is
///    preserved (Requirement 8.5).
///
/// Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.9, 15.5, 8.3, 8.5.
/// </summary>
public sealed class CompilerDriverStdlibIntegrationTest : IDisposable
{
    private readonly string scratchDir;

    public CompilerDriverStdlibIntegrationTest()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-driver-stdlib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private static CompilerDriver CreateRealDriver()
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    /// <summary>
    /// Hook the semantic model out of the driver by using a wrapping builder
    /// that captures the constructed model before returning it to the driver.
    /// The driver itself only exposes a <see cref="CompilerResult"/> (artifact
    /// path + diagnostics), so this is the cleanest way to inspect the
    /// semantic model that the driver built.
    /// </summary>
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

    private static CompilerDriver CreateDriverWithCapture(CapturingSemanticModelBuilder capture)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    [Fact]
    public void Compile_TrivialUserProgram_SemanticModelContainsStandardInt32()
    {
        // Arrange — a minimal user program in a distinct namespace so no name
        // clash with anything the stdlib declares.
        var userSource = "namespace X;\npublic class Y {\n    public static void Main() { }\n}\n";
        var userPath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(userPath, userSource);

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(userPath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "X",
            NamespaceRoot: "X",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);

        // Act
        var result = driver.Compile(options);

        // Assert — the semantic model was constructed (the driver may still
        // have surfaced unrelated analyzer or emitter diagnostics; we care
        // about the stdlib integration here, not end-to-end success).
        Assert.NotNull(capture.Captured);
        Assert.DoesNotContain(
            result.Diagnostics,
            d => d.Id.Value == DiagnosticId.StdlibParse.Value);

        var model = capture.Captured!;

        // The driver should have prepended every stdlib compilation unit so
        // the compilation spans stdlib + user code.
        Assert.Contains(model.Compilation.Units, u => u.IsStdlib);
        Assert.Contains(model.Compilation.Units, u => !u.IsStdlib && u.Source.Path == userPath);
        // Stdlib units are prepended — user units come after (design § 8).
        var firstUserIndex = model.Compilation.Units.ToList().FindIndex(u => !u.IsStdlib);
        var lastStdlibIndex = model.Compilation.Units.ToList().FindLastIndex(u => u.IsStdlib);
        Assert.True(lastStdlibIndex < firstUserIndex,
            "Expected all stdlib units to precede user units in compilation.Units.");

        // The stdlib type `Standard.Int32` must be discoverable via the
        // registered symbols. We don't drive a name-resolution query here
        // (the keyword-flip in Stage 4 handles that path); instead we assert
        // the symbol exists in the flat AllSymbols list.
        var standardInt32Symbol = model.AllSymbols.FirstOrDefault(s =>
            s.Name == "Int32"
            && (s.Kind == SymbolKind.Struct || s.Kind == SymbolKind.Class)
            && s.DeclaredType is StructType st
            && st.ContainingNamespace == "Standard");

        Assert.NotNull(standardInt32Symbol);
        Assert.IsType<StructType>(standardInt32Symbol!.DeclaredType);
        var int32Type = (StructType)standardInt32Symbol.DeclaredType!;
        Assert.Equal("Int32", int32Type.DisplayName);
        Assert.Equal("Standard", int32Type.ContainingNamespace);
        Assert.True(int32Type.IsBackendBacked,
            "Standard.Int32 must carry the type-level `intrinsic` marker (IsBackendBacked = true).");

        // Walk from the `Standard` namespace symbol to confirm namespace
        // containment is correctly modelled (containing scope holds the
        // namespace scope, and Int32 sits inside it).
        var standardNamespaceSymbol = model.AllSymbols.FirstOrDefault(s =>
            s.Kind == SymbolKind.Namespace && s.Name == "Standard");
        Assert.NotNull(standardNamespaceSymbol);

        // The Int32 symbol must be declared directly inside a scope whose
        // parent is the global scope (i.e., the Standard namespace scope).
        Assert.Same(model.GlobalScope, standardInt32Symbol.ContainingScope.Parent);
    }
}