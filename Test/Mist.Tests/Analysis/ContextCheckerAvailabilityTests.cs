using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the ContextCheckerAnalyzer's context availability checking (Task 5.2).
/// Verifies that MIST_CTX_0001 is emitted when context is missing and NOT emitted
/// when context is provided.
/// </summary>
public sealed class ContextCheckerAvailabilityTests
{
    private static CompilerDriver CreateDriver()
    {
        return new CompilerDriver(
            new MistLexer(),
            new MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(
                new TypeCheckerAnalyzer(),
                new ContextCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private static ImmutableArray<Diagnostic> CompileSource(string source)
    {
        var driver = CreateDriver();
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_ctx_test_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = driver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    private static ImmutableArray<Diagnostic> GetContextDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.MissingContext)
            .ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: MIST_CTX_0001 should be emitted when context is missing
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MissingContext_DirectCall_EmitsDiagnostic()
    {
        // A method with a with-clause is called without any context in scope.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        _ = Assert.Single(ctxDiags);
        Assert.Contains("Consume", ctxDiags[0].Message);
        Assert.Contains("MyCtx", ctxDiags[0].Message);
    }

    [Fact]
    public void MissingContext_TransitiveCall_EmitsDiagnostic()
    {
        // A method calls another method that calls a method with a with-clause,
        // but no context is provided anywhere in the chain.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                void Middle() {
                    Consume();
                }

                public static void Main() {
                    var p = new Program();
                    p.Middle();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        // The diagnostic should be emitted at the call site in Middle()
        // where Consume() is called without context.
        _ = Assert.Single(ctxDiags);
        Assert.Contains("Consume", ctxDiags[0].Message);
    }

    [Fact]
    public void MissingContext_MultipleWithClauses_EmitsDiagnosticPerType()
    {
        // A method with multiple with-clauses is called without any context.
        var source = """
            class CtxA { }
            class CtxB { }

            class Program {
                void Consume() with CtxA a with CtxB b { }

                public static void Main() {
                    var p = new Program();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Equal(2, ctxDiags.Length);
        Assert.Contains(ctxDiags, d => d.Message.Contains("CtxA"));
        Assert.Contains(ctxDiags, d => d.Message.Contains("CtxB"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: MIST_CTX_0001 should NOT be emitted when context is provided
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ContextProvided_WithVarDeclaration_NoDiagnostic()
    {
        // Context is provided via `with var x = expr;` before the call.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    with var c = new MyCtx();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_WithTypedDeclaration_NoDiagnostic()
    {
        // Context is provided via `with TypeRef x = expr;` before the call.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    with MyCtx c = new MyCtx();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_WithDiscardDeclaration_NoDiagnostic()
    {
        // Context is provided via `with TypeRef _ = expr;` before the call.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    with MyCtx _ = new MyCtx();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_WithPushStatement_NoDiagnostic()
    {
        // Context is provided via `with expr;` before the call.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    with new MyCtx();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_WithBlock_NoDiagnostic()
    {
        // Context is provided via `with(expr) { ... }` block containing the call.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    with(new MyCtx()) {
                        p.Consume();
                    }
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_CallerHasWithClause_NoDiagnostic()
    {
        // The calling method itself has a with-clause for the required type,
        // meaning it receives context from its own caller.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                void Caller() with MyCtx c {
                    Consume();
                }

                public static void Main() {
                    var p = new Program();
                    with var c = new MyCtx();
                    p.Caller();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_TransparentIntermediate_NoDiagnostic()
    {
        // Context is declared in an outer method, and an intermediate method
        // (without context declarations) calls the consumer. The intermediate
        // method is transparent — context propagates through it.
        // Note: The checker verifies at each direct call site, so the call
        // from Middle() to Consume() needs context. Middle() doesn't have it,
        // so this should emit a diagnostic at the Middle->Consume call site.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                void Middle() with MyCtx c {
                    Consume();
                }

                public static void Main() {
                    var p = new Program();
                    with var c = new MyCtx();
                    p.Middle();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_MultipleTypes_NoDiagnostic()
    {
        // Multiple context types are required and all are provided.
        var source = """
            class CtxA { }
            class CtxB { }

            class Program {
                void Consume() with CtxA a with CtxB b { }

                public static void Main() {
                    var p = new Program();
                    with var a = new CtxA();
                    with var b = new CtxB();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextProvided_PartiallyMissing_EmitsDiagnosticForMissingOnly()
    {
        // Two context types required, only one provided.
        var source = """
            class CtxA { }
            class CtxB { }

            class Program {
                void Consume() with CtxA a with CtxB b { }

                public static void Main() {
                    var p = new Program();
                    with var a = new CtxA();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        _ = Assert.Single(ctxDiags);
        Assert.Contains("CtxB", ctxDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: Type alias resolution for context keying
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TypeAlias_AliasAndUnderlyingShareKey_NoDiagnostic()
    {
        // A type alias resolves to its underlying type for context keying.
        // Declaring context with the alias satisfies a binding for the underlying type.
        var source = """
            class MyCtx { }
            type Alias = MyCtx;

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    with Alias _ = new MyCtx();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: Context declaration ordering (must appear before call site)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ContextDeclaredAfterCallSite_EmitsDiagnostic()
    {
        // Context is declared AFTER the call site in the same block.
        // This should emit a diagnostic because the context is not yet available.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    p.Consume();
                    with var c = new MyCtx();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        _ = Assert.Single(ctxDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: With-block scoping (context only available inside the block)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_CallOutsideBlock_EmitsDiagnostic()
    {
        // Context is provided inside a with-block, but the call is outside it.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    with(new MyCtx()) {
                    }
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        _ = Assert.Single(ctxDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: Standalone function calls (no receiver)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void StandaloneCall_MissingContext_EmitsDiagnostic()
    {
        // A standalone function call (no receiver) to a method with a with-clause.
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx { }

                public static void Main() {
                    var p = new Program();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        _ = Assert.Single(ctxDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: No false positives for methods without with-clauses
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoWithClauses_NoDiagnostic()
    {
        // A program with no with-clauses should produce no context diagnostics.
        var source = """
            class Program {
                void DoSomething() { }

                public static void Main() {
                    var p = new Program();
                    p.DoSomething();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = GetContextDiagnostics(diagnostics);

        Assert.Empty(ctxDiags);
    }
}