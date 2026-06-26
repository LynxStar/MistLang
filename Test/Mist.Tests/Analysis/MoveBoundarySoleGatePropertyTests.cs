// Feature: ownership-v2, Property 1: Move boundary is the sole gate
// **Validates: Requirements 6.9, 4.1, 4.2**
//
// Property 1 (design § "Property 1: Move boundary is the sole gate", Req 6 AC 9):
//
//   For all owned-move sites S (binding init, owned-field assignment, owned-param
//   pass) and all source expressions e, the accept/reject decision and the
//   clone-insertion decision for (S, e) equal MoveBoundaryResolver.Resolve(e, ...).
//   There is no site that accepts an owned move the resolver would reject, nor vice
//   versa.
//
// SCOPE OF WHAT THIS TEST CAN VERIFY TODAY
// ----------------------------------------
// The OwnershipAnalyzer is NOT yet wired to call MoveBoundaryResolver at the three
// owned-move sites — that wiring lands in task 8.1 (see the [Skip] trigger tests in
// OwnershipV2DiagnosticTests.cs, all annotated "Pending MoveBoundaryResolver wiring
// (task 8.1)"). Until 8.1 wires the analyzer, there is no per-site analyzer decision
// to compare against; the only decision function in existence is the resolver itself.
//
// Property 1 is fundamentally the claim that the resolver is the SINGLE authority and
// that the decision is the SAME at every site (the resolution algorithm is documented
// as site-independent — OwnedMoveSite is informational/for diagnostics only). This test
// therefore verifies exactly that invariant, which is the verifiable core of "the move
// boundary is the sole gate":
//
//   (a) SITE-INDEPENDENCE — for a generated source expression e, resolving e at
//       OwnedBindingInit, OwnedFieldAssignment, and OwnedParameterPass (each with a
//       fresh, identical flow state) yields the SAME MoveOutcome. No site can accept a
//       move another site rejects, nor reach a different clone decision. This is the
//       "sole gate": there is no site-specific provenance logic — every site gets the
//       same answer from the one resolver.
//
//   (b) DETERMINISM — resolving the same (e, site) twice from identical flow state
//       yields the same MoveOutcome and the same consumed binding. The gate is a pure
//       function of (source, destination, flow state).
//
// Together these pin down that field-assignment provenance (Req 4.1/4.2) and owned
// binding/param provenance (Req 6.9) are decided by ONE function with no per-site
// divergence. Once task 8.1 wires the analyzer, this property is strengthened to the
// full end-to-end "analyzer per-site decision == direct resolver call" comparison; the
// generator and the resolver call here are already shaped for that extension.
//
// Spawns 0 external processes (in-process CompilerDriver.Compile per iteration).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using StormSyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based test for Ownership v2 Property 1 — "the move boundary is the sole
/// gate" (<see cref="MoveBoundaryResolver"/>, design Component 2, Req 6 AC 9).
///
/// Feature: ownership-v2, Property 1: Move boundary is the sole gate
/// **Validates: Requirements 6.9, 4.1, 4.2**
///
/// See the file header for the precise scope of what is verifiable before the task 8.1
/// analyzer wiring lands. In short: the resolver is the single decision function, and
/// its decision is site-independent and deterministic.
/// </summary>
public sealed class MoveBoundarySoleGatePropertyTests : IDisposable
{
    private readonly string _outputDir;

    public MoveBoundarySoleGatePropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistMoveBoundaryGatePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Capturing semantic model builder ────────────────────────────────
    //
    // Mirrors the pattern in IClonableDerivationPropertyTests: wrap the real builder so
    // the test can inspect the model the driver constructed (the IClonable derivation
    // pass — which MoveBoundaryResolver consults — runs after Build mutates this model).

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();
        public ISemanticModel? Captured { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<StormSyntaxNode> asts,
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

    // ─── Compile helper ──────────────────────────────────────────────────

    private ISemanticModel? CompileAndCapture(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
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
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        driver.Compile(options);
        return capture.Captured;
    }

    // ─── AST traversal helpers ───────────────────────────────────────────

    private static IEnumerable<StormSyntaxNode> Descendants(StormSyntaxNode node)
    {
        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            yield return child;
            foreach(var d in Descendants(child))
            {
                yield return d;
            }
        }
    }

    /// <summary>
    /// Returns the initializer expression of a variable declaration node, or null if the
    /// node is not an initialized typed/inferred declaration.
    /// </summary>
    private static StormSyntaxNode? GetInitializer(StormSyntaxNode node) => node switch
    {
        Mist.Generated.TypedVariableDeclarationVariableDeclarationStatementNode typed => typed.Initializer,
        Mist.Generated.InferredVariableDeclarationVariableDeclarationStatementNode inferred => inferred.Initializer,
        _ => null,
    };

    /// <summary>
    /// Collects every owned-binding initializer expression in the program along with the
    /// owned binding's declared destination type. These are exactly the source expressions
    /// that flow through the owned-move boundary at an <see cref="OwnedMoveSite"/>.
    /// </summary>
    private static List<(StormSyntaxNode Source, IType? DestinationType)> CollectOwnedBindingInitializers(ISemanticModel model)
    {
        var result = new List<(StormSyntaxNode, IType?)>();

        foreach(var ast in model.Asts)
        {
            foreach(var node in Descendants(ast))
            {
                var initializer = GetInitializer(node);
                if(initializer is null)
                {
                    continue;
                }

                if(model.GetDeclaredSymbol(node) is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                {
                    result.Add((initializer, sym.DeclaredType));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Collects every owned local-variable / parameter symbol. Used to seed a flow state
    /// where all owned bindings are <see cref="VariableOwnershipState.Live"/>, so that a
    /// "move from owned binding" source resolves uniformly across sites.
    /// </summary>
    private static List<ISymbol> CollectOwnedLocals(ISemanticModel model)
    {
        return model.AllSymbols
            .Where(s => s.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
            .Where(s => s is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
            .ToList();
    }

    /// <summary>
    /// Builds a fresh flow state in which every owned binding is Live. Each resolver call
    /// gets its own copy so the move-consumption side effect (marking a binding Moved) of
    /// one call never leaks into another — keeping the per-site comparison apples-to-apples.
    /// </summary>
    private static MethodFlowState FreshFlowState(IEnumerable<ISymbol> ownedSymbols)
    {
        var state = new MethodFlowState();
        foreach(var sym in ownedSymbols)
        {
            state.VariableStates[sym] = VariableOwnershipState.Live;
        }

        return state;
    }

    // ─── Scenario model + generator ──────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface a scenario

    /// <summary>The kind of source expression placed at an owned-move boundary.</summary>
    public enum SourceKind
    {
        /// <summary><c>new Widget()</c> — constructor call (already owned).</summary>
        ConstructorNonOwnedType,

        /// <summary><c>new Vault()</c> — constructor call of an owned type.</summary>
        ConstructorOwnedType,

        /// <summary><c>default(Widget)</c> — typed default expression.</summary>
        DefaultTyped,

        /// <summary>bare <c>default</c> with an explicitly-typed owned LHS.</summary>
        DefaultBare,

        /// <summary>An <c>int</c> literal — value/primitive copy.</summary>
        ValueCopy,

        /// <summary>A reference to an earlier owned binding — a move.</summary>
        MoveFromOwned,

        /// <summary>A reference to a non-owned binding of a clonable type — implicit clone.</summary>
        CloneFromNonOwned,

        /// <summary>A reference to a non-owned binding of a non-clonable type — error.</summary>
        NonClonable,
    }

    /// <summary>A generated program plus a human-readable label of the source kinds it contains.</summary>
    public sealed record GateScenario(string Source, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    private static Gen<SourceKind> GenSourceKind() =>
        Gen.Elements(
            SourceKind.ConstructorNonOwnedType,
            SourceKind.ConstructorOwnedType,
            SourceKind.DefaultTyped,
            SourceKind.DefaultBare,
            SourceKind.ValueCopy,
            SourceKind.MoveFromOwned,
            SourceKind.CloneFromNonOwned,
            SourceKind.NonClonable);

    private static Gen<GateScenario> GenScenario() =>
        from count in Gen.Choose(1, 5)
        from kinds in Gen.ArrayOf(GenSourceKind(), count)
        select Build(kinds);

    /// <summary>
    /// Emits the statements for a single owned-move scenario. Each scenario produces at
    /// least one owned binding whose initializer is the source-of-interest; some kinds emit
    /// a prerequisite binding first (a prior owned binding to move from, or a non-owned
    /// binding to clone/fail-to-clone from). Every emitted owned binding is itself a valid
    /// owned-move site that the test will exercise.
    /// </summary>
    private static void EmitScenario(System.Text.StringBuilder sb, SourceKind kind, int i)
    {
        switch(kind)
        {
            case SourceKind.ConstructorNonOwnedType:
                sb.Append($"        owned var v{i} = new Widget();\n");
                break;

            case SourceKind.ConstructorOwnedType:
                sb.Append($"        owned var v{i} = new Vault();\n");
                break;

            case SourceKind.DefaultTyped:
                sb.Append($"        owned Widget v{i} = default(Widget);\n");
                break;

            case SourceKind.DefaultBare:
                sb.Append($"        owned Widget v{i} = default;\n");
                break;

            case SourceKind.ValueCopy:
                sb.Append($"        owned int v{i} = 5;\n");
                break;

            case SourceKind.MoveFromOwned:
                sb.Append($"        owned var src{i} = new Widget();\n");
                sb.Append($"        owned var v{i} = src{i};\n");
                break;

            case SourceKind.CloneFromNonOwned:
                sb.Append($"        var shared{i} = new Widget();\n");
                sb.Append($"        owned var v{i} = shared{i};\n");
                break;

            case SourceKind.NonClonable:
                sb.Append($"        var g{i} = new Gadget();\n");
                sb.Append($"        owned var v{i} = g{i};\n");
                break;
        }
    }

    private static GateScenario Build(SourceKind[] kinds)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("using Standard;\n\n");
        sb.Append("namespace Gen;\n\n");

        // Fixed type fixtures spanning the clonability / ownership axes the resolver
        // classifies on:
        //   Widget — non-owned, auto-derives IClonable<T> (int + string fields).
        //   Gadget — non-owned, NOT clonable (abstract-typed field blocks derivation).
        //   Vault  — an owned type.
        sb.Append("public class Widget\n{\n    public int Value;\n    public string Label;\n}\n\n");
        sb.Append("public abstract ISignal\n{\n}\n\n");
        sb.Append("public class Gadget\n{\n    public ISignal signal;\n}\n\n");
        sb.Append("owned class Vault\n{\n    public int Code;\n}\n\n");

        sb.Append("public class Program\n{\n    public static void Main()\n    {\n");

        for(var i = 0; i < kinds.Length; i++)
        {
            EmitScenario(sb, kinds[i], i);
        }

        sb.Append("    }\n}\n");

        var label = string.Join(", ", kinds.Select((k, i) => $"v{i}:{k}"));
        return new GateScenario(sb.ToString(), label);
    }

    public static Arbitrary<GateScenario> ArbitraryGateScenario() => GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: the move boundary is the sole gate.
    //
    // For every owned-binding initializer e in a generated program, resolving e at all
    // three OwnedMoveSites (from identical fresh flow states) yields the SAME outcome, and
    // resolving the same (e, site) twice is deterministic. There is no site-specific gate.
    //
    // **Validates: Requirements 6.9, 4.1, 4.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MoveBoundarySoleGatePropertyTests) })]
    public Property MoveBoundary_IsTheSoleGate(GateScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);

        if(model is null)
        {
            return false.ToProperty().Label($"Semantic model was null (compilation failed early).\nSource:\n{scenario.Source}");
        }

        var ownedInitializers = CollectOwnedBindingInitializers(model);

        // No owned bindings to exercise (should not happen given the generator, but keep the
        // property well-defined): vacuously true.
        if(ownedInitializers.Count == 0)
        {
            return true.ToProperty();
        }

        var ownedSymbols = CollectOwnedLocals(model);
        var resolver = new MoveBoundaryResolver();

        var sites = new[]
        {
            OwnedMoveSite.OwnedBindingInit,
            OwnedMoveSite.OwnedFieldAssignment,
            OwnedMoveSite.OwnedParameterPass,
        };

        foreach(var (source, destinationType) in ownedInitializers)
        {
            var perSite = new MoveResolution[sites.Length];

            for(var s = 0; s < sites.Length; s++)
            {
                var flowState = FreshFlowState(ownedSymbols);
                var diagnostics = new DiagnosticBag();
                perSite[s] = resolver.Resolve(source, destinationType, sites[s], flowState, model, diagnostics);
            }

            // (a) SITE-INDEPENDENCE: every site must reach the same accept/reject + clone
            //     decision. A divergence here would mean a site has its own provenance logic
            //     — exactly what "sole gate" forbids.
            for(var s = 1; s < sites.Length; s++)
            {
                if(perSite[s].Outcome != perSite[0].Outcome)
                {
                    return false.ToProperty().Label(
                        $"Site-dependence detected: {sites[0]} => {perSite[0].Outcome} but "
                        + $"{sites[s]} => {perSite[s].Outcome} for the same source.\n"
                        + $"Scenario: {scenario.Label}\nSource:\n{scenario.Source}");
                }
            }

            // (b) DETERMINISM: resolving the same (source, site) again from an identical fresh
            //     flow state yields the same outcome and consumes the same binding.
            var repeatFlowState = FreshFlowState(ownedSymbols);
            var repeat = resolver.Resolve(source, destinationType, sites[0], repeatFlowState, model, new DiagnosticBag());

            if(repeat.Outcome != perSite[0].Outcome ||
                !ReferenceEquals(repeat.ConsumedBinding, perSite[0].ConsumedBinding))
            {
                return false.ToProperty().Label(
                    $"Non-deterministic resolution: first => ({perSite[0].Outcome}, "
                    + $"consumed={perSite[0].ConsumedBinding?.Name ?? "<none>"}), "
                    + $"repeat => ({repeat.Outcome}, consumed={repeat.ConsumedBinding?.Name ?? "<none>"}).\n"
                    + $"Scenario: {scenario.Label}\nSource:\n{scenario.Source}");
            }
        }

        return true.ToProperty();
    }
}