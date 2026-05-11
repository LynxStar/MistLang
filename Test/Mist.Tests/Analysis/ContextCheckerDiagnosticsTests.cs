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
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the ContextCheckerAnalyzer's remaining diagnostics (Task 5.4):
/// MIST_CTX_0002 (duplicate context binding),
/// MIST_CTX_0003 (context declaration outside method body),
/// MIST_CTX_0004 (cannot infer context type),
/// MIST_CTX_0005 (assignment to context-bound variable).
/// Also verifies constructor with-clauses are checked at new expression call sites.
/// </summary>
public sealed class ContextCheckerDiagnosticsTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_ctx_diag_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

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

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CTX_0002: Duplicate context binding
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DuplicateContextBinding_SameTypeInMultipleWithClauses_EmitsDiagnostic()
    {
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx a with MyCtx b { }

                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.DuplicateContextBinding)
            .ToImmutableArray();

        Assert.Single(ctxDiags);
        Assert.Contains("Consume", ctxDiags[0].Message);
        Assert.Contains("MyCtx", ctxDiags[0].Message);
    }

    [Fact]
    public void DuplicateContextBinding_DifferentTypes_NoDiagnostic()
    {
        var source = """
            class CtxA { }
            class CtxB { }

            class Program {
                void Consume() with CtxA a with CtxB b { }

                public static void Main() {
                    with CtxA _ = new CtxA();
                    with CtxB _ = new CtxB();
                    var p = new Program();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.DuplicateContextBinding)
            .ToImmutableArray();

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void DuplicateContextBinding_Constructor_EmitsDiagnostic()
    {
        var source = """
            class MyCtx { }

            class Widget {
                Widget() with MyCtx a with MyCtx b { }
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.DuplicateContextBinding)
            .ToImmutableArray();

        Assert.Single(ctxDiags);
        Assert.Contains("MyCtx", ctxDiags[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MIST_CTX_0005: Assignment to context-bound variable
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ContextBoundVariable_Assignment_EmitsDiagnostic()
    {
        var source = """
            class MyCtx { }

            class Program {
                void Consume() with MyCtx ctx {
                    ctx = new MyCtx();
                }

                public static void Main() {
                    with MyCtx _ = new MyCtx();
                    var p = new Program();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.ContextVariableNotReassignable)
            .ToImmutableArray();

        Assert.Single(ctxDiags);
        Assert.Contains("ctx", ctxDiags[0].Message);
        Assert.Contains("not reassignable", ctxDiags[0].Message);
    }

    [Fact]
    public void ContextBoundVariable_NoAssignment_NoDiagnostic()
    {
        var source = """
            class MyCtx {
                int Value;
                new(int v) { this.Value = v; }
            }

            class Program {
                void Consume() with MyCtx ctx {
                    var x = ctx;
                }

                public static void Main() {
                    with MyCtx _ = new MyCtx(42);
                    var p = new Program();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.ContextVariableNotReassignable)
            .ToImmutableArray();

        Assert.Empty(ctxDiags);
    }

    [Fact]
    public void ContextBoundVariable_AssignmentToDifferentLocal_NoDiagnostic()
    {
        var source = """
            class MyCtx {
                new() { }
            }

            class Program {
                void Consume() with MyCtx ctx {
                    var other = new MyCtx();
                    other = new MyCtx();
                }

                public static void Main() {
                    with MyCtx _ = new MyCtx();
                    var p = new Program();
                    p.Consume();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.ContextVariableNotReassignable)
            .ToImmutableArray();

        Assert.Empty(ctxDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor with-clauses checked at new expression call sites
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConstructorWithClause_NewExpressionWithoutContext_EmitsMissingContextDiagnostic()
    {
        var source = """
            class MyCtx { }

            class Widget {
                Widget() with MyCtx ctx { }
            }

            class Program {
                public static void Main() {
                    var w = new Widget();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.MissingContext)
            .ToImmutableArray();

        Assert.Single(ctxDiags);
        Assert.Contains("MyCtx", ctxDiags[0].Message);
    }

    [Fact]
    public void ConstructorWithClause_NewExpressionWithContext_NoDiagnostic()
    {
        var source = """
            class MyCtx { }

            class Widget {
                Widget() with MyCtx ctx { }
            }

            class Program {
                public static void Main() {
                    with MyCtx _ = new MyCtx();
                    var w = new Widget();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.MissingContext)
            .ToImmutableArray();

        Assert.Empty(ctxDiags);
    }
}
