// Feature: ownership-v2, Property 4: Non-owned non-clonable source implies error
// **Validates: Requirements 8.4, 13.7**

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
/// Property-based test for <see cref="MoveBoundaryResolver"/> Property 4
/// (Ownership v2 design, Component 2).
///
/// Feature: ownership-v2, Property 4: Non-owned non-clonable source implies error
/// **Validates: Requirements 8.4, 13.7**
///
/// For all non-owned reference sources <c>e</c> whose type does NOT implement
/// <c>IClonable&lt;T&gt;</c>, resolving a move of <c>e</c> into an owned destination
/// yields <see cref="MoveOutcome.Error"/> AND emits <c>MIST_OWN_NOT_CLONABLE</c>.
///
/// <para>
/// The generator builds programs that declare a reference-typed class with at least
/// one non-clonable field (an array field, or an abstract-typed field — neither
/// auto-derives <c>IClonable&lt;T&gt;</c>), bind a NON-owned local to a fresh instance
/// of it, and then move that local into an <c>owned var</c>. Each program is compiled
/// in-process via a fresh <see cref="CompilerDriver"/> per iteration (the driver runs
/// the IClonable derivation pass that caches <c>ImplementsIClonable</c> on the type
/// model). The test then locates the owned-binding initializer node — exactly as the
/// analyzer's owned-binding validation does — calls
/// <see cref="MoveBoundaryResolver.Resolve"/> directly with a fresh
/// <see cref="DiagnosticBag"/>, and asserts the outcome is <see cref="MoveOutcome.Error"/>
/// and the bag contains <c>MIST_OWN_NOT_CLONABLE</c>. Spawns 0 external processes.
/// </para>
/// </summary>
public sealed class MoveBoundaryNotClonablePropertyTests : IDisposable
{
    private const string NotClonableId = "MIST_OWN_NOT_CLONABLE";

    private readonly string outputDir;

    public MoveBoundaryNotClonablePropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistMoveBoundaryNotClonablePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
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
    /// captured reference observes the derived <c>ImplementsIClonable</c> flags that
    /// <see cref="MoveBoundaryResolver"/>'s default clonability oracle reads.
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

        driver.Compile(options);
        return capture.Captured;
    }

    // ─── AST helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the owned local-variable binding with the given name and returns its
    /// declaring node's initializer expression plus the binding's declared type, or
    /// <c>null</c> if the binding (or its initializer) cannot be located. Mirrors the
    /// analyzer's owned-binding initializer lookup.
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

    /// <summary>The kind of non-clonable field that makes the source type non-derivable.</summary>
    public enum NonClonableKind
    {
        /// <summary>An <c>int[]</c> field — arrays are not auto-derivable.</summary>
        ArrayField,

        /// <summary>An abstract-typed field — abstract types do not implement <c>IClonable&lt;T&gt;</c>.</summary>
        AbstractField,
    }

    /// <summary>
    /// A generated program plus the name of the owned binding whose initializer is the
    /// non-owned, non-clonable source under test.
    /// </summary>
    public sealed record NotClonableScenario(string Source, string OwnedVarName, string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    private static readonly string[] TypeNames =
        ["Gadget", "SharedThing", "Resource", "Connection", "Payload", "Handle"];

    private static readonly string[] SourceVarNames =
        ["shared", "src", "thing", "input", "other"];

    private static readonly string[] OwnedVarNames =
        ["c", "owned1", "dest", "target", "result"];

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<NotClonableScenario> GenScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from srcVar in Gen.Elements(SourceVarNames)
               from ownedVar in Gen.Elements(OwnedVarNames)
               from kind in Gen.Elements(NonClonableKind.ArrayField, NonClonableKind.AbstractField)
               select Build(typeName, srcVar, ownedVar, kind);
    }

    private static NotClonableScenario Build(string typeName, string srcVar, string ownedVar, NonClonableKind kind)
    {
        // A reference-typed class with a non-clonable field cannot auto-derive
        // IClonable<T>. The source binding 'srcVar' is a plain (non-owned) local.
        string source = kind switch
        {
            NonClonableKind.ArrayField => $$"""
                namespace Gen;

                public class {{typeName}}
                {
                    public int[] data;
                }

                public class Program
                {
                    public static void Main()
                    {
                        var {{srcVar}} = new {{typeName}}();
                        owned var {{ownedVar}} = {{srcVar}};
                    }
                }
                """,
            NonClonableKind.AbstractField => $$"""
                namespace Gen;

                public abstract ISignal
                {
                }

                public class {{typeName}}
                {
                    public ISignal signal;
                }

                public class Program
                {
                    public static void Main()
                    {
                        var {{srcVar}} = new {{typeName}}();
                        owned var {{ownedVar}} = {{srcVar}};
                    }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return new NotClonableScenario(source, ownedVar, $"{typeName}/{kind}: owned {ownedVar} = {srcVar}");
    }

    public static Arbitrary<NotClonableScenario> ArbitraryNotClonableScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: non-owned non-clonable source ⇒ Error + MIST_OWN_NOT_CLONABLE
    //
    // **Validates: Requirements 8.4, 13.7**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(MoveBoundaryNotClonablePropertyTests) })]
    public Property NonOwnedNonClonableSource_ResolvesToError_AndEmitsNotClonable(NotClonableScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);

        if(model is null)
        {
            return false.ToProperty().Label($"Semantic model was null (compilation failed early).\nSource:\n{scenario.Source}");
        }

        var located = FindOwnedBindingInitializer(model, scenario.OwnedVarName);
        if(located is null)
        {
            return false.ToProperty().Label(
                $"Could not locate owned binding '{scenario.OwnedVarName}' or its initializer.\nSource:\n{scenario.Source}");
        }

        var (initializer, destinationType) = located.Value;

        var resolver = new MoveBoundaryResolver();
        var flowState = new MethodFlowState();
        var diagnostics = new DiagnosticBag();

        var resolution = resolver.Resolve(
            initializer,
            destinationType,
            OwnedMoveSite.OwnedBindingInit,
            flowState,
            model,
            diagnostics);

        if(resolution.Outcome != MoveOutcome.Error)
        {
            return false.ToProperty().Label(
                $"Expected Outcome=Error for a non-owned non-clonable source, but got {resolution.Outcome}.\n"
                + $"Scenario: {scenario.Label}\nSource:\n{scenario.Source}");
        }

        var emitted = diagnostics.ToImmutableArray();
        if(!emitted.Any(d => d.Id.Value == NotClonableId))
        {
            var ids = string.Join(", ", emitted.Select(d => d.Id.Value));
            return false.ToProperty().Label(
                $"Expected diagnostic '{NotClonableId}' to be emitted, but got: [{ids}].\n"
                + $"Scenario: {scenario.Label}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}