// Feature: ownership-v2, Task 4.3 — Property 2: Owned source implies no clone.
//
// For all sources e that are already owned — a constructor call (new T()), a call to a
// function returning an owned value, a move from a live owned binding, a default(T) /
// bare default expression, or a value-type/primitive source — MoveBoundaryResolver.Resolve(e)
// returns an outcome in {MovedFromOwned, ValueCopy, DefaultGraph} and NEVER ImplicitClone
// (and never Error).
//
// **Validates: Requirements 6.1, 6.2, 6.3**
//
// The model is built in-process via a per-call CompilerDriver (the IClonable derivation pass
// runs before analysis, so the captured model carries the ImplementsIClonable flag the
// resolver's default oracle reads). Spawns 0 external processes.

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
/// Property-based tests for <see cref="MoveBoundaryResolver"/> Property 2 (ownership-v2):
/// "Owned source implies no clone".
///
/// For all already-owned sources <c>e</c>, <c>Resolve(e)</c> ∈
/// {<see cref="MoveOutcome.MovedFromOwned"/>, <see cref="MoveOutcome.ValueCopy"/>,
/// <see cref="MoveOutcome.DefaultGraph"/>} and never <see cref="MoveOutcome.ImplicitClone"/>.
///
/// **Validates: Requirements 6.1, 6.2, 6.3**
/// </summary>
public sealed class MoveBoundaryOwnedSourcePropertyTests : IDisposable
{
    private readonly string outputDir;

    public MoveBoundaryOwnedSourcePropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistMoveBoundaryOwnedPBT_" + Guid.NewGuid().ToString("N"));
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
    /// constructed model after the driver's analysis pipeline (including the IClonable
    /// derivation pass) has mutated it.
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
    /// <c>owned</c> binding specifier, paired with the declared destination symbol (whose
    /// <see cref="ISymbol.DeclaredType"/> is passed as the move destination type).
    /// </summary>
    private static List<(SyntaxNode Initializer, IType? DestinationType)> FindOwnedBindingInitializers(
        ISemanticModel model)
    {
        var result = new List<(SyntaxNode, IType?)>();
        var userAst = model.Asts[^1];
        walk(userAst);
        return result;

        void walk(SyntaxNode node)
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
                    walk(child);
                }
            }
        }
    }

    // ─── Scenario ────────────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>The category of already-owned source under test.</summary>
    public enum OwnedSourceKind
    {
        /// <summary><c>owned var x = new Foo()</c> — constructor call (Req 6 AC 1, AC 5).</summary>
        ConstructorCall,

        /// <summary><c>owned var x = MakeFoo()</c> — call returning <c>owned Foo</c> (Req 6 AC 7).</summary>
        OwnedReturningCall,

        /// <summary><c>owned var s = new Foo(); owned var x = s;</c> — move from a live owned binding (Req 6 AC 1).</summary>
        MoveFromOwnedBinding,

        /// <summary><c>owned var x = default(Foo)</c> — default(T) expression (Req 6 AC 10).</summary>
        DefaultExpression,

        /// <summary><c>owned Foo x = default</c> — bare default, typed LHS (Req 6 AC 10).</summary>
        BareDefault,

        /// <summary><c>owned var x = new Point()</c> — value-type (struct) source (Req 4 AC 3).</summary>
        ValueTypeStruct,

        /// <summary><c>owned var x = 7</c> — primitive source (Req 4 AC 4).</summary>
        Primitive,
    }

    /// <summary>An already-owned source scenario: a full program plus a human-readable label.</summary>
    public sealed record OwnedSourceScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    private static readonly string[] ClassNames =
        ["Container", "Buffer", "Holder", "Wrapper", "Storage", "Vault"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "instance", "item"];

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildSource(OwnedSourceKind kind, string typeName, string varName)
    {
        return kind switch
        {
            OwnedSourceKind.ConstructorCall =>
                $$"""
                owned class {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned var {{varName}} = new {{typeName}}();
                    }
                }
                """,

            OwnedSourceKind.OwnedReturningCall =>
                $$"""
                owned class {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned var {{varName}} = Make{{typeName}}();
                    }
                    public static owned {{typeName}} Make{{typeName}}() {
                        return new {{typeName}}();
                    }
                }
                """,

            OwnedSourceKind.MoveFromOwnedBinding =>
                $$"""
                owned class {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned var source = new {{typeName}}();
                        owned var {{varName}} = source;
                    }
                }
                """,

            OwnedSourceKind.DefaultExpression =>
                $$"""
                owned class {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned var {{varName}} = default({{typeName}});
                    }
                }
                """,

            OwnedSourceKind.BareDefault =>
                $$"""
                owned class {{typeName}} {
                    int value;
                }
                class Program {
                    public static void Main() {
                        owned {{typeName}} {{varName}} = default;
                    }
                }
                """,

            OwnedSourceKind.ValueTypeStruct =>
                $$"""
                struct {{typeName}}Point {
                    int x;
                }
                class Program {
                    public static void Main() {
                        owned var {{varName}} = new {{typeName}}Point();
                    }
                }
                """,

            OwnedSourceKind.Primitive =>
                $$"""
                class Program {
                    public static void Main() {
                        owned var {{varName}} = 7;
                    }
                }
                """,

            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<OwnedSourceScenario> GenOwnedSourceScenario()
    {
        return from kind in Gen.Elements(
                   OwnedSourceKind.ConstructorCall,
                   OwnedSourceKind.OwnedReturningCall,
                   OwnedSourceKind.MoveFromOwnedBinding,
                   OwnedSourceKind.DefaultExpression,
                   OwnedSourceKind.BareDefault,
                   OwnedSourceKind.ValueTypeStruct,
                   OwnedSourceKind.Primitive)
               from typeName in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               let source = BuildSource(kind, typeName, varName)
               let label = $"{kind}: owned {varName} : {typeName}"
               select new OwnedSourceScenario(source, label);
    }

    public static Arbitrary<OwnedSourceScenario> ArbitraryOwnedSourceScenario()
    {
        return GenOwnedSourceScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Owned source implies no clone.
    //
    // For every owned-binding initializer in a program whose source is already owned,
    // MoveBoundaryResolver.Resolve returns MovedFromOwned, ValueCopy, or DefaultGraph —
    // never ImplicitClone (and never Error).
    //
    // **Validates: Requirements 6.1, 6.2, 6.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(MoveBoundaryOwnedSourcePropertyTests) })]
    public bool OwnedSource_NeverResolvesToImplicitClone(OwnedSourceScenario scenario)
    {
        var model = CompileAndCapture(scenario.Source);
        if(model is null)
        {
            return false;
        }

        var ownedInits = FindOwnedBindingInitializers(model);

        // The generated program always contains at least one owned binding; if discovery
        // found none, the scenario or harness is broken.
        if(ownedInits.Count == 0)
        {
            return false;
        }

        var resolver = new MoveBoundaryResolver();

        foreach(var (initializer, destinationType) in ownedInits)
        {
            // A fresh flow state per resolution keeps each move independent (the binding
            // being referenced is not yet tracked, so the truth-table guard permits the move).
            var flowState = new MethodFlowState();
            var diagnostics = new DiagnosticBag();

            var resolution = resolver.Resolve(
                initializer,
                destinationType,
                OwnedMoveSite.OwnedBindingInit,
                flowState,
                model,
                diagnostics);

            var ok = resolution.Outcome is
                MoveOutcome.MovedFromOwned or
                MoveOutcome.ValueCopy or
                MoveOutcome.DefaultGraph;

            if(!ok)
            {
                return false;
            }
        }

        return true;
    }
}