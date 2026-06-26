// Feature: ownership-v2, Property 5: Clone from lent tier does not consume the lender
// **Validates: Requirements 8.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based test for the move boundary resolver's handling of cloning a source held
/// in a lent tier (Ownership v2, Property 5).
///
/// Feature: ownership-v2, Property 5: Clone from lent tier does not consume the lender
/// **Validates: Requirements 8.5**
///
/// Requirement 8 AC 5: when the source of a move into an owned destination is a value held
/// in a lent tier (<c>borrowed</c>, <c>frozen</c>, or <c>readonly ref</c>) whose type
/// implements <c>IClonable&lt;T&gt;</c>, the compiler implicitly inserts a <c>.Clone()</c>
/// call to produce a fresh owned value. Cloning from a lent reference SHALL NOT consume or
/// move the underlying value — the lender retains its value after the lend returns.
///
/// <para>
/// The resolver's <see cref="MoveOutcome.ImplicitClone"/> path (Step 4) never marks any
/// binding <see cref="VariableOwnershipState.Moved"/>; it reads through the reference and
/// records a clone annotation only. This test proves that property holds for all lent
/// states: for a clonable non-owned lender placed in any lent tier
/// (<see cref="VariableOwnershipState.LentReadonly"/>, <see cref="VariableOwnershipState.LentFrozen"/>,
/// or <see cref="VariableOwnershipState.LentRef"/>), resolving a clone into an owned
/// destination yields <see cref="MoveOutcome.ImplicitClone"/> and leaves the lender's
/// flow-state entry exactly as it was before the call.
/// </para>
///
/// <para>
/// Each generated program is compiled in-process via a fresh <see cref="CompilerDriver"/>
/// (which runs <c>IClonableDerivationEngine</c> before ownership analysis, so the captured
/// model carries the derived <c>ImplementsIClonable</c> flags the resolver's default oracle
/// reads). The test locates the owned-binding initializer node and the lender symbol
/// <c>s</c>, primes a <see cref="MethodFlowState"/> with the lender in a lent state, and
/// calls <see cref="MoveBoundaryResolver.Resolve"/>. Spawns 0 external processes.
/// </para>
/// </summary>
public sealed class MoveBoundaryCloneFromLentPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public MoveBoundaryCloneFromLentPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistMoveBoundaryCloneFromLentPBT_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the test can inspect the
    /// constructed model after the driver builds it. The IClonable derivation pass runs after
    /// <c>Build</c> in the driver and mutates this same model instance, so the captured
    /// reference observes the derived <c>ImplementsIClonable</c> flags the resolver reads.
    /// </summary>
    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder _inner = new();
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

    /// <summary>
    /// Finds the owned local-variable binding with the given name and returns its declaring
    /// node's initializer expression plus the binding's declared type, or <c>null</c> if the
    /// binding (or its initializer) cannot be located.
    /// </summary>
    private static (SyntaxNode Initializer, Mist.Semantics.Contracts.IType? DestinationType)? FindOwnedBindingInitializer(
        ISemanticModel model, string bindingName)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
                continue;
            if(symbol.Kind != SymbolKind.LocalVariable)
                continue;
            if(symbol.Name != bindingName)
                continue;
            if(symbol is not Symbol varSymbol || varSymbol.OwnershipTier != OwnershipTier.Owned)
                continue;

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { return null; }

            var initializer = GetInitializerExpression(declaringNode);
            if(initializer is null)
                return null;

            return (initializer, varSymbol.DeclaredType);
        }

        return null;
    }

    /// <summary>
    /// Finds the user-code local-variable symbol with the given name (the lender), or
    /// <c>null</c> if it cannot be located.
    /// </summary>
    private static ISymbol? FindLocalVariableSymbol(ISemanticModel model, string name)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
                continue;
            if(symbol.Kind != SymbolKind.LocalVariable)
                continue;
            if(symbol.Name == name)
                return symbol;
        }

        return null;
    }

    /// <summary>
    /// Extracts the initializer expression of a variable declaration node, mirroring
    /// <c>OwnershipAnalyzer.GetInitializerExpression</c>.
    /// </summary>
    private static SyntaxNode? GetInitializerExpression(SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            TypedVariableDeclarationVariableDeclarationStatementNode typed => typed.Initializer,
            InferredVariableDeclarationVariableDeclarationStatementNode inferred => inferred.Initializer,
            _ => null,
        };
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface a scenario

    /// <summary>The clonability classification of a single generated field of the source type.</summary>
    public enum FieldKind
    {
        /// <summary>An <c>int</c> field — clonable base case.</summary>
        IntLeaf,

        /// <summary>A <c>string</c> field — clonable base case.</summary>
        StringLeaf,

        /// <summary>A field whose type is a clonable helper class (propagates clonability).</summary>
        ClonableRef,
    }

    /// <summary>
    /// A generated program plus the lent state to place the lender in, and the human-readable
    /// label.
    /// </summary>
    public sealed record CloneFromLentScenario(string Source, VariableOwnershipState LentState, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────
    //
    // Generates a single non-owned reference source type S guaranteed to implement
    // IClonable<S> (explicitly or auto-derived) and a program with a non-owned binding `s` of
    // S used as the source of an owned binding `x`. The scenario also carries one of the three
    // lent states to prime the lender with.

    private static Gen<CloneFromLentScenario> GenScenario()
    {
        return from declaresExplicit in Gen.Elements(false, false, true) // bias toward auto-derivation
               from fieldCount in Gen.Choose(0, 3)
               from fields in Gen.ArrayOf(GenField(), fieldCount).Select(a => a.ToList())
               from lentState in Gen.Elements(
                   VariableOwnershipState.LentReadonly,
                   VariableOwnershipState.LentFrozen,
                   VariableOwnershipState.LentRef)
               select Build(declaresExplicit, fields, lentState);
    }

    private static Gen<FieldKind> GenField() =>
        Gen.Elements(FieldKind.IntLeaf, FieldKind.StringLeaf, FieldKind.ClonableRef);

    private static CloneFromLentScenario Build(bool declaresExplicit, List<FieldKind> fields, VariableOwnershipState lentState)
    {
        var usesHelper = fields.Contains(FieldKind.ClonableRef);

        var sb = new System.Text.StringBuilder();
        sb.Append("using Standard;\n\nnamespace Gen;\n\n");

        // A clonable helper class (all-int fields → auto-derives IClonable<Helper>).
        if(usesHelper)
        {
            sb.Append("public class Helper\n{\n    public int hv;\n}\n\n");
        }

        // The non-owned reference source type S.
        var clonableClause = declaresExplicit ? " : IClonable<S>" : "";
        sb.Append($"public class S{clonableClause}\n{{\n");
        for(var i = 0; i < fields.Count; i++)
        {
            var typeText = fields[i] switch
            {
                FieldKind.IntLeaf => "int",
                FieldKind.StringLeaf => "string",
                FieldKind.ClonableRef => "Helper",
                _ => "int",
            };
            sb.Append($"    public {typeText} f{i};\n");
        }

        if(declaresExplicit)
        {
            // A Clone() satisfying IClonable<S>; `default` is a fresh owned graph (Req 6.10).
            sb.Append("    public owned S Clone() { return default; }\n");
        }

        sb.Append("}\n\n");

        // A non-owned binding `s` of S, used as the source of the owned binding `x`.
        sb.Append(
            "public class Program\n{\n    public static void Main()\n    {\n" +
            "        var s = new S();\n" +
            "        owned var x = s;\n" +
            "    }\n}\n");

        var fieldDesc = fields.Count == 0
            ? "()"
            : "(" + string.Join(",", fields) + ")";
        var label = $"S:{(declaresExplicit ? "decl" : "auto")}{fieldDesc} lent={lentState}";

        return new CloneFromLentScenario(sb.ToString(), lentState, label);
    }

    public static Arbitrary<CloneFromLentScenario> ArbitraryCloneFromLentScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Clone from a lent tier ⇒ Resolve == ImplicitClone, and the lender's
    //             flow-state entry is unchanged (no transition to Moved).
    //
    // **Validates: Requirements 8.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MoveBoundaryCloneFromLentPropertyTests) })]
    public Property CloneFromLentTier_DoesNotConsumeLender(CloneFromLentScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);

        if(model is null)
            return false.ToProperty().Label($"Semantic model was null (compilation failed early).\nSource:\n{scenario.Source}");

        var located = FindOwnedBindingInitializer(model, "x");
        if(located is null)
            return false.ToProperty().Label($"Could not locate owned binding 'x' or its initializer.\nSource:\n{scenario.Source}");

        var lender = FindLocalVariableSymbol(model, "s");
        if(lender is null)
            return false.ToProperty().Label($"Could not locate lender binding 's'.\nSource:\n{scenario.Source}");

        var (initializer, destinationType) = located.Value;

        // Prime the flow state: place the lender in the generated lent tier and record it.
        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        flowState.VariableStates[lender] = scenario.LentState;
        var preState = flowState.VariableStates[lender];
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            initializer,
            destinationType,
            OwnedMoveSite.OwnedBindingInit,
            flowState,
            model,
            diagnostics);

        // (a) The non-owned clonable source resolves to an implicit clone (Req 8 AC 5).
        if(resolution.Outcome != MoveOutcome.ImplicitClone)
        {
            return false.ToProperty().Label(
                $"Expected Resolve == ImplicitClone for a clonable source held in {scenario.LentState}, "
                + $"but got {resolution.Outcome}.\nScenario: {scenario.Label}\nSource:\n{scenario.Source}");
        }

        // (b) The lender is NOT consumed: its flow-state entry equals the pre-clone state.
        if(!flowState.VariableStates.TryGetValue(lender, out var postState))
        {
            return false.ToProperty().Label(
                $"Lender 's' was removed from the flow state by the clone.\nScenario: {scenario.Label}\nSource:\n{scenario.Source}");
        }

        if(postState != preState)
        {
            return false.ToProperty().Label(
                $"Clone consumed the lender: pre-state was {preState} but post-state is {postState} "
                + $"(expected unchanged, NOT Moved).\nScenario: {scenario.Label}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}