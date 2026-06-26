// Feature: ownership-v2, Task 4.7 — Property 10: `default` yields ownership.
//
// For all owned destinations of type T, both `default(T)` and bare `default` (with an
// explicitly-typed owned LHS) resolve via MoveBoundaryResolver.Resolve as
// MoveOutcome.DefaultGraph and are accepted (no error diagnostic emitted).
//
// **Validates: Requirements 6.10**
//
// The model is built in-process via a per-call CompilerDriver. Spawns 0 external processes.

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
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for <see cref="MoveBoundaryResolver"/> Property 10 (ownership-v2):
/// "<c>default</c> yields ownership".
///
/// For all owned destinations of type <c>T</c>, both <c>default(T)</c> and bare
/// <c>default</c> (typed LHS) resolve as <see cref="MoveOutcome.DefaultGraph"/> and are
/// accepted as owned sources — no error diagnostic is emitted.
///
/// **Validates: Requirements 6.10**
/// </summary>
public sealed class MoveBoundaryDefaultGraphPropertyTests : IDisposable
{
    private readonly string outputDir;

    public MoveBoundaryDefaultGraphPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistMoveBoundaryDefaultPBT_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Wraps the real <see cref="SemanticModelBuilder"/> so the test can inspect the
    /// constructed model after the driver's analysis pipeline has run.
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

    // ─── Compile helper (in-process, captures the semantic model) ─────────

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

    // ─── Owned-binding initializer discovery ─────────────────────────────

    /// <summary>
    /// Collects the initializer expression of every variable declaration that carries the
    /// <c>owned</c> binding specifier, paired with the declared destination symbol's type.
    /// Covers both typed declarations (<c>owned T y = default</c>) and inferred declarations
    /// (<c>owned var x = default(T)</c>).
    /// </summary>
    private static List<(SyntaxNode Initializer, IType? DestinationType)> FindOwnedBindingInitializers(
        ISemanticModel model)
    {
        var result = new List<(SyntaxNode, IType?)>();
        var userAst = model.Asts[^1];
        Walk(userAst);
        return result;

        void Walk(SyntaxNode node)
        {
            switch(node)
            {
                case TypedVariableDeclarationVariableDeclarationStatementNode typed
                    when typed.OwnershipSpecifier is OwnedSpecifierOwnershipSpecifierNode:
                    result.Add((typed.Initializer, (model.GetDeclaredSymbol(typed) as Symbol)?.DeclaredType));
                    break;

                case InferredVariableDeclarationVariableDeclarationStatementNode inferred
                    when inferred.OwnershipSpecifier is OwnedSpecifierOwnershipSpecifierNode:
                    result.Add((inferred.Initializer, (model.GetDeclaredSymbol(inferred) as Symbol)?.DeclaredType));
                    break;
            }

            foreach(var child in node.Children())
            {
                if(child is not null)
                {
                    Walk(child);
                }
            }
        }
    }

    // ─── Scenario ────────────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>The category of owned destination type T under test (Req 6 AC 10).</summary>
    public enum DestinationKind
    {
        /// <summary>A value-type struct destination.</summary>
        ValueTypeStruct,

        /// <summary>A plain (non-owned) class destination.</summary>
        PlainClass,

        /// <summary>An <c>owned</c> class destination.</summary>
        OwnedClass,

        /// <summary>A constructed generic class destination (e.g. <c>Box&lt;int&gt;</c>).</summary>
        Generic,
    }

    /// <summary>
    /// A <c>default</c>-source scenario: a full program exercising BOTH <c>default(T)</c> and
    /// bare <c>default</c> into owned destinations of type T, plus a human-readable label.
    /// </summary>
    public sealed record DefaultScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    private static readonly string[] TypeNames =
        ["Container", "Buffer", "Holder", "Wrapper", "Storage", "Vault"];

    // ─── Source builder ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a program declaring a destination type T of the given <paramref name="kind"/>
    /// and a Main that initializes two owned bindings: one via <c>default(T)</c> (inferred
    /// LHS) and one via bare <c>default</c> (explicitly-typed owned LHS).
    /// </summary>
    private static string BuildSource(DestinationKind kind, string typeName)
    {
        return kind switch
        {
            DestinationKind.ValueTypeStruct =>
                $$"""
                struct {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned var x = default({{typeName}});
                        owned {{typeName}} y = default;
                    }
                }
                """,

            DestinationKind.PlainClass =>
                $$"""
                class {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned var x = default({{typeName}});
                        owned {{typeName}} y = default;
                    }
                }
                """,

            DestinationKind.OwnedClass =>
                $$"""
                owned class {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned var x = default({{typeName}});
                        owned {{typeName}} y = default;
                    }
                }
                """,

            DestinationKind.Generic =>
                $$"""
                class {{typeName}}<T> {
                    T item;
                }
                class Program {
                    public static void Main() {
                        owned var x = default({{typeName}}<int>);
                        owned {{typeName}}<int> y = default;
                    }
                }
                """,

            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<DefaultScenario> GenDefaultScenario()
    {
        return from kind in Gen.Elements(
                   DestinationKind.ValueTypeStruct,
                   DestinationKind.PlainClass,
                   DestinationKind.OwnedClass,
                   DestinationKind.Generic)
               from typeName in Gen.Elements(TypeNames)
               let source = BuildSource(kind, typeName)
               let label = $"{kind}: default into owned {typeName}"
               select new DefaultScenario(source, label);
    }

    public static Arbitrary<DefaultScenario> ArbitraryDefaultScenario()
    {
        return GenDefaultScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: `default` yields ownership.
    //
    // For every owned-binding initializer whose source is default(T) or bare default,
    // MoveBoundaryResolver.Resolve returns DefaultGraph and emits no error diagnostic.
    //
    // **Validates: Requirements 6.10**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(MoveBoundaryDefaultGraphPropertyTests) })]
    public bool Default_ResolvesToDefaultGraph_AndIsAccepted(DefaultScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);
        if(model is null)
        {
            return false;
        }

        var ownedInits = FindOwnedBindingInitializers(model);

        // The generated program always contains exactly two owned default bindings
        // (default(T) and bare default); if discovery found fewer, the harness is broken.
        if(ownedInits.Count < 2)
        {
            return false;
        }

        var resolver = new MoveBoundaryResolver();

        foreach(var (initializer, destinationType) in ownedInits)
        {
            var flowState = new MethodFlowState();
            var diagnostics = new DiagnosticBag();

            var resolution = resolver.Resolve(
                initializer,
                destinationType,
                OwnedMoveSite.OwnedBindingInit,
                flowState,
                model,
                diagnostics);

            // Both default(T) and bare default must classify as a fresh owned graph (Req 6 AC 10).
            if(resolution.Outcome != MoveOutcome.DefaultGraph)
            {
                return false;
            }

            // The move must be accepted — no error diagnostic emitted by the resolver.
            if(diagnostics.HasErrors)
            {
                return false;
            }
        }

        return true;
    }
}