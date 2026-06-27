// Feature: ownership-v2, Property 3: Non-owned clonable source implies exactly one clone
// **Validates: Requirements 6.9, 8.2, 8.3**

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
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based test for the move boundary resolver's handling of non-owned
/// clonable sources (Ownership v2, Property 3).
///
/// Feature: ownership-v2, Property 3: Non-owned clonable source implies exactly one clone
/// **Validates: Requirements 6.9, 8.2, 8.3**
///
/// For all non-owned <em>reference-type</em> sources <c>e</c> whose type implements
/// <c>IClonable&lt;T&gt;</c> (either by explicit declaration or by auto-derivation
/// because every field is clonable), resolving a move of <c>e</c> into an owned
/// destination yields <see cref="MoveOutcome.ImplicitClone"/>, and the resolver
/// records exactly one implicit-clone annotation for that source
/// (<see cref="ISemanticModel.GetImplicitCloneDestination"/> is non-null).
///
/// <para>
/// The resolver is not yet wired into the <c>OwnershipAnalyzer</c> (task 8.1), so the
/// test drives it directly: each generated program is compiled in-process via a fresh
/// <see cref="CompilerDriver"/> (which runs <c>IClonableDerivationEngine</c> before
/// ownership analysis, so the captured model carries the derived
/// <c>ImplementsIClonable</c> flags the resolver's default oracle reads). The test then
/// locates the owned-binding initializer node — exactly as the analyzer's
/// <c>ValidateOwnedBindingTypes</c> does — and calls
/// <see cref="MoveBoundaryResolver.Resolve"/> on it with a fresh
/// <see cref="MethodFlowState"/>. Spawns 0 external processes.
/// </para>
/// </summary>
public sealed class MoveBoundaryClonableSourcePropertyTests : IDisposable
{
    private readonly string outputDir;

    public MoveBoundaryClonableSourcePropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistMoveBoundaryClonablePBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Capturing semantic model builder ────────────────────────────────

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the test can inspect the
    /// constructed model after the driver builds it. The IClonable derivation pass runs
    /// after <c>Build</c> in the driver and mutates this same model instance, so the
    /// captured reference observes the derived <c>ImplementsIClonable</c> flags the
    /// resolver's default clonability oracle reads.
    /// </summary>
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

    /// <summary>
    /// Finds the owned local-variable binding with the given name and returns its
    /// declaring node's initializer expression plus the binding's declared type, or
    /// <c>null</c> if the binding (or its initializer) cannot be located.
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
            catch { return null; }

            var initializer = GetInitializerExpression(declaringNode);
            if(initializer is null)
            {
                return null;
            }

            return (initializer, varSymbol.DeclaredType);
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
    /// A generated program: a non-owned clonable reference source type, an owned
    /// binding initialized from a non-owned binding of that type, and the human-readable
    /// label.
    /// </summary>
    public sealed record ClonableSourceScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────
    //
    // Generates a single non-owned reference source type S that is guaranteed to
    // implement IClonable<S>:
    //   • explicit  : S declares `: IClonable<S>` with a Clone() body, OR
    //   • auto-derive: S has only clonable fields (int / string / a clonable helper),
    //                  so the derivation pass derives IClonable<S>.
    // The owned binding is `owned var x = s;` where `s` is a non-owned (unrestricted)
    // binding of S — a non-owned reference source whose move must resolve to ImplicitClone.

    private static Gen<ClonableSourceScenario> GenScenario()
    {
        return from declaresExplicit in Gen.Elements(false, false, true) // bias toward auto-derivation
               from fieldCount in Gen.Choose(0, 3)
               from fields in Gen.ArrayOf(GenField(), fieldCount).Select(a => a.ToList())
               select Build(declaresExplicit, fields);
    }

    private static Gen<FieldKind> GenField()
    {
        return Gen.Elements(FieldKind.IntLeaf, FieldKind.StringLeaf, FieldKind.ClonableRef);
    }

    private static ClonableSourceScenario Build(bool declaresExplicit, List<FieldKind> fields)
    {
        var usesHelper = fields.Contains(FieldKind.ClonableRef);

        var sb = new System.Text.StringBuilder();
        _ = sb.Append("using Standard;\n\nnamespace Gen;\n\n");

        // A clonable helper class (all-int fields → auto-derives IClonable<Helper>).
        if(usesHelper)
        {
            _ = sb.Append("public class Helper\n{\n    public int hv;\n}\n\n");
        }

        // The non-owned reference source type S.
        var clonableClause = declaresExplicit ? " : IClonable<S>" : "";
        _ = sb.Append($"public class S{clonableClause}\n{{\n");
        for(var i = 0; i < fields.Count; i++)
        {
            var typeText = fields[i] switch
            {
                FieldKind.IntLeaf => "int",
                FieldKind.StringLeaf => "string",
                FieldKind.ClonableRef => "Helper",
                _ => "int",
            };
            _ = sb.Append($"    public {typeText} f{i};\n");
        }

        if(declaresExplicit)
        {
            // A Clone() satisfying IClonable<S>; `default` is a fresh owned graph (Req 6.10).
            _ = sb.Append("    public owned S Clone() { return default; }\n");
        }

        _ = sb.Append("}\n\n");

        // The owned binding initialized from a non-owned binding of S.
        _ = sb.Append(
            "public class Program\n{\n    public static void Main()\n    {\n" +
            "        var s = new S();\n" +
            "        owned var x = s;\n" +
            "    }\n}\n");

        var fieldDesc = fields.Count == 0
            ? "()"
            : "(" + string.Join(",", fields) + ")";
        var label = $"S:{(declaresExplicit ? "decl" : "auto")}{fieldDesc}";

        return new ClonableSourceScenario(sb.ToString(), label);
    }

    public static Arbitrary<ClonableSourceScenario> ArbitraryClonableSourceScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Non-owned clonable reference source ⇒ Resolve == ImplicitClone,
    //             with exactly one implicit-clone annotation recorded.
    //
    // **Validates: Requirements 6.9, 8.2, 8.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MoveBoundaryClonableSourcePropertyTests) })]
    public Property ClonableSource_ResolvesToExactlyOneImplicitClone(ClonableSourceScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);

        if(model is null)
        {
            return false.ToProperty().Label($"Semantic model was null (compilation failed early).\nSource:\n{scenario.Source}");
        }

        var located = FindOwnedBindingInitializer(model, "x");
        if(located is null)
        {
            return false.ToProperty().Label($"Could not locate owned binding 'x' or its initializer.\nSource:\n{scenario.Source}");
        }

        var (initializer, destinationType) = located.Value;

        // The default resolver reads the derived ImplementsIClonable flag from the model.
        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            initializer,
            destinationType,

            flowState,
            model,
            diagnostics);

        if(resolution.Outcome != MoveOutcome.ImplicitClone)
        {
            return false.ToProperty().Label(
                $"Expected Resolve == ImplicitClone for a non-owned clonable reference source, "
                + $"but got {resolution.Outcome}.\nScenario: {scenario.Label}\nSource:\n{scenario.Source}");
        }

        // "Exactly one clone": the resolver records a single implicit-clone annotation
        // for the source, so GetImplicitCloneDestination must be non-null.
        if(model.GetImplicitCloneDestination(initializer) is null)
        {
            return false.ToProperty().Label(
                $"Resolve returned ImplicitClone but no implicit-clone annotation was recorded "
                + $"for the source.\nScenario: {scenario.Label}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}