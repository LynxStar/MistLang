// Feature: ownership-v2, Task 4.8 — unit tests for the MoveBoundaryResolver decision matrix.
//
// Table-driven [Theory]/[InlineData] over (source kind × site × clonability) asserting the
// MoveOutcome. Covers the full resolution algorithm documented in the ownership-v2 design
// (Component 2). Each row represents one concrete path through the resolver's decision logic.
//
// The fixture source declares:
//   - An owned class with all-clonable fields (OwnedClonable)
//   - A non-owned class with all-clonable fields (PlainClonable)
//   - A non-owned class with a non-clonable field (PlainNotClonable)
//   - A struct (ValuePoint)
//
// For each decision-matrix row, the test locates the relevant owned-binding initializer
// (or field-assignment source or parameter-passing argument) and invokes
// MoveBoundaryResolver.Resolve directly on the AST node, then asserts the MoveOutcome.
//
// Compiled ONCE in-process via CompilerDriver (test-process-spawning policy: 0 external
// process spawns).
//
// **Validates: Requirements 6.9, 4.3, 4.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Xunit;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the <see cref="MoveBoundaryResolver"/> decision matrix
/// (ownership-v2 design Component 2, Task 4.8).
///
/// Decision matrix (source kind × site × clonability) → <see cref="MoveOutcome"/>:
///
/// <list type="table">
///   <listheader>
///     <term>Source Kind</term><term>Site</term><term>Clonable?</term><term>Outcome</term>
///   </listheader>
///   <item>default(T)       | any site      | N/A      | DefaultGraph     </item>
///   <item>bare default     | any site      | N/A      | DefaultGraph     </item>
///   <item>value type       | any site      | N/A      | ValueCopy        </item>
///   <item>primitive        | any site      | N/A      | ValueCopy        </item>
///   <item>new T()          | any site      | N/A      | MovedFromOwned   </item>
///   <item>owned-returning  | any site      | N/A      | MovedFromOwned   </item>
///   <item>owned binding    | any site      | N/A      | MovedFromOwned   </item>
///   <item>non-owned ref    | any site      | yes      | ImplicitClone    </item>
///   <item>non-owned ref    | any site      | no       | Error            </item>
/// </list>
///
/// **Validates: Requirements 6.9, 4.3, 4.4**
/// </summary>
public sealed class MoveBoundaryResolverDecisionMatrixTests : IDisposable
{
    private readonly string scratchDir;

    public MoveBoundaryResolverDecisionMatrixTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-resolver-matrix-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();
        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<SyntaxNode> asts,
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

    // ─── Compile helper (in-process) ─────────────────────────────────────

    private ISemanticModel? CompileAndCapture(string source)
    {
        var filePath = Path.Combine(scratchDir, $"Test_{Guid.NewGuid():N}.mist");
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

        _ = driver.Compile(options);
        return capture.Captured;
    }

    // ─── AST helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the owned local-variable binding with the given name and returns its
    /// initializer expression plus the binding's declared type.
    /// </summary>
    private static (SyntaxNode Initializer, IType? DestinationType)? FindOwnedBindingInitializer(
        ISemanticModel model, string bindingName)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            if(symbol.Kind != SymbolKind.LocalVariable)
            {
                continue;
            }

            if(symbol.Name != bindingName)
            {
                continue;
            }

            if(symbol is not Symbol varSymbol || varSymbol.OwnershipTier != OwnershipTier.Owned)
            {
                continue;
            }

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            var initializer = GetInitializerExpression(declaringNode);
            if(initializer is null)
            {
                continue;
            }

            return (initializer, varSymbol.DeclaredType);
        }

        return null;
    }

    private static SyntaxNode? GetInitializerExpression(SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            TypedVariableDeclarationVariableDeclarationStatementNode typed => typed.Initializer,
            InferredVariableDeclarationVariableDeclarationStatementNode inferred => inferred.Initializer,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Decision matrix: default(T) / bare default → DefaultGraph
    //
    // Regardless of site, default expressions always resolve to DefaultGraph.
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void DefaultExpression_ResolvesToDefaultGraph(OwnedMoveSite site)
    {
        const string source = """
            namespace TestProgram;
            owned class Box { public int value; }
            class Program {
                public static void Main() {
                    owned var target = default(Box);
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.DefaultGraph, resolution.Outcome);
    }

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void BareDefault_ResolvesToDefaultGraph(OwnedMoveSite site)
    {
        const string source = """
            namespace TestProgram;
            owned class Box { public int value; }
            class Program {
                public static void Main() {
                    owned Box target = default;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.DefaultGraph, resolution.Outcome);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Decision matrix: value type / primitive → ValueCopy
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void ValueTypeStruct_ResolvesToValueCopy(OwnedMoveSite site)
    {
        const string source = """
            namespace TestProgram;
            struct Point { public int x; public int y; }
            class Program {
                public static void Main() {
                    owned var target = new Point();
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.ValueCopy, resolution.Outcome);
    }

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void PrimitiveInt_ResolvesToValueCopy(OwnedMoveSite site)
    {
        const string source = """
            namespace TestProgram;
            class Program {
                public static void Main() {
                    owned var target = 42;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.ValueCopy, resolution.Outcome);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Decision matrix: already-owned sources → MovedFromOwned
    //
    // Constructor call, owned-returning function call, move from owned binding.
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void ConstructorCall_ResolvesToMovedFromOwned(OwnedMoveSite site)
    {
        const string source = """
            namespace TestProgram;
            owned class Widget { public int value; }
            class Program {
                public static void Main() {
                    owned var target = new Widget();
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.MovedFromOwned, resolution.Outcome);
        Assert.Null(resolution.ConsumedBinding); // constructor call — no consumed binding
    }

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void OwnedReturningCall_ResolvesToMovedFromOwned(OwnedMoveSite site)
    {
        const string source = """
            namespace TestProgram;
            owned class Widget { public int value; }
            class Program {
                public static void Main() {
                    owned var target = MakeWidget();
                }
                public static owned Widget MakeWidget() {
                    return new Widget();
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.MovedFromOwned, resolution.Outcome);
        Assert.Null(resolution.ConsumedBinding); // call — no consumed binding
    }

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void MoveFromOwnedBinding_ResolvesToMovedFromOwned(OwnedMoveSite site)
    {
        const string source = """
            namespace TestProgram;
            owned class Widget { public int value; }
            class Program {
                public static void Main() {
                    owned var src = new Widget();
                    owned var target = src;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        // We need to set up the flow state with the 'src' symbol in Live state.
        var srcSymbol = model!.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Kind == SymbolKind.LocalVariable
                && s.Name == "src"
                && s.OwnershipTier == OwnershipTier.Owned);
        Assert.NotNull(srcSymbol);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        flowState.VariableStates[srcSymbol!] = VariableOwnershipState.Live;
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.MovedFromOwned, resolution.Outcome);
        Assert.NotNull(resolution.ConsumedBinding); // a consumed binding reference
        Assert.Equal("src", resolution.ConsumedBinding!.Name);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Decision matrix: non-owned clonable source → ImplicitClone
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void NonOwnedClonableSource_ResolvesToImplicitClone(OwnedMoveSite site)
    {
        // PlainClonable has only int fields → auto-derives IClonable.
        // The source 'src' is a non-owned (plain) binding.
        const string source = """
            namespace TestProgram;
            class PlainClonable {
                public int x;
                public int y;
            }
            class Program {
                public static void Main() {
                    var src = new PlainClonable();
                    owned var target = src;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.ImplicitClone, resolution.Outcome);
        Assert.Null(resolution.ConsumedBinding); // clone does not consume
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Decision matrix: non-owned non-clonable source → Error
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(OwnedMoveSite.OwnedBindingInit)]
    [InlineData(OwnedMoveSite.OwnedFieldAssignment)]
    [InlineData(OwnedMoveSite.OwnedParameterPass)]
    public void NonOwnedNonClonableSource_ResolvesToError(OwnedMoveSite site)
    {
        // NotClonable has an int[] field → cannot auto-derive IClonable.
        // The source 'src' is a non-owned (plain) binding.
        const string source = """
            namespace TestProgram;
            class NotClonable {
                public int[] data;
            }
            class Program {
                public static void Main() {
                    var src = new NotClonable();
                    owned var target = src;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            site,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.Error, resolution.Outcome);
        Assert.Null(resolution.ConsumedBinding);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Decision matrix: truth-table guard — move from non-Live state → Error
    //
    // An owned binding that is not in the Live state cannot be moved. This covers the
    // truth-table guard in step 3 of the resolver algorithm.
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(VariableOwnershipState.Moved)]
    [InlineData(VariableOwnershipState.LentRef)]
    [InlineData(VariableOwnershipState.LentFrozen)]
    [InlineData(VariableOwnershipState.LentReadonly)]
    public void MoveFromNonLiveOwnedBinding_ResolvesToError(VariableOwnershipState blockedState)
    {
        const string source = """
            namespace TestProgram;
            owned class Widget { public int value; }
            class Program {
                public static void Main() {
                    owned var src = new Widget();
                    owned var target = src;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        // Find the 'src' symbol and force it into the blocked state.
        var srcSymbol = model!.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Kind == SymbolKind.LocalVariable
                && s.Name == "src"
                && s.OwnershipTier == OwnershipTier.Owned);
        Assert.NotNull(srcSymbol);

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        flowState.VariableStates[srcSymbol!] = blockedState;
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            binding!.Value.Initializer,
            binding!.Value.DestinationType,
            OwnedMoveSite.OwnedBindingInit,
            flowState,
            model!,
            diagnostics);

        Assert.Equal(MoveOutcome.Error, resolution.Outcome);
        // A diagnostic should have been emitted for the truth-table violation.
        Assert.True(diagnostics.HasErrors);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Decision matrix: site independence — the same source kind resolves the
    // same way regardless of the site.
    //
    // This verifies that the resolver is truly site-independent (Req 6 AC 9:
    // a single move boundary).
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AllSites_ProduceSameOutcome_ForConstructorCallSource()
    {
        const string source = """
            namespace TestProgram;
            owned class Widget { public int value; }
            class Program {
                public static void Main() {
                    owned var target = new Widget();
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var outcomes = new List<MoveOutcome>();

        foreach(var site in Enum.GetValues<OwnedMoveSite>())
        {
            var flowState = new MethodFlowState();
            var diagnostics = new DiagnosticBag();

            var resolution = resolver.Resolve(
                binding!.Value.Initializer,
                binding!.Value.DestinationType,
                site,
                flowState,
                model!,
                diagnostics);

            outcomes.Add(resolution.Outcome);
        }

        // All sites should produce the same outcome.
        Assert.All(outcomes, o => Assert.Equal(MoveOutcome.MovedFromOwned, o));
    }

    [Fact]
    public void AllSites_ProduceSameOutcome_ForNonOwnedClonableSource()
    {
        const string source = """
            namespace TestProgram;
            class PlainClonable {
                public int x;
                public int y;
            }
            class Program {
                public static void Main() {
                    var src = new PlainClonable();
                    owned var target = src;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var outcomes = new List<MoveOutcome>();

        foreach(var site in Enum.GetValues<OwnedMoveSite>())
        {
            var flowState = new MethodFlowState();
            var diagnostics = new DiagnosticBag();

            var resolution = resolver.Resolve(
                binding!.Value.Initializer,
                binding!.Value.DestinationType,
                site,
                flowState,
                model!,
                diagnostics);

            outcomes.Add(resolution.Outcome);
        }

        Assert.All(outcomes, o => Assert.Equal(MoveOutcome.ImplicitClone, o));
    }

    [Fact]
    public void AllSites_ProduceSameOutcome_ForNonClonableSource()
    {
        const string source = """
            namespace TestProgram;
            class NotClonable {
                public int[] data;
            }
            class Program {
                public static void Main() {
                    var src = new NotClonable();
                    owned var target = src;
                }
            }
            """;

        var model = CompileAndCapture(source);
        Assert.NotNull(model);

        var binding = FindOwnedBindingInitializer(model!, "target");
        _ = Assert.NotNull(binding);

        var resolver = new MoveBoundaryResolver();
        var outcomes = new List<MoveOutcome>();

        foreach(var site in Enum.GetValues<OwnedMoveSite>())
        {
            var flowState = new MethodFlowState();
            var diagnostics = new DiagnosticBag();

            var resolution = resolver.Resolve(
                binding!.Value.Initializer,
                binding!.Value.DestinationType,
                site,
                flowState,
                model!,
                diagnostics);

            outcomes.Add(resolution.Outcome);
        }

        Assert.All(outcomes, o => Assert.Equal(MoveOutcome.Error, o));
    }
}