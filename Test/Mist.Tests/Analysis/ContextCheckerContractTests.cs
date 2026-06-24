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
/// Unit tests for the ContextCheckerAnalyzer's abstract type contract enforcement (Task 5.3).
/// Verifies that MIST_CTX_0006 is emitted for contract violations and NOT emitted
/// when contracts are satisfied.
/// </summary>
public sealed class ContextCheckerContractTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_ctx_contract_{Guid.NewGuid():N}");
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

    private static ImmutableArray<Diagnostic> GetContractDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.WithClauseContractViolation)
            .ToImmutableArray();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: MIST_CTX_0006 should be emitted for abstract type contract violations
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractMethod_ImplementorOmitsWithClause_EmitsDiagnostic()
    {
        // An abstract type method declares a with-clause, but the implementing class
        // method does not declare it.
        var source = """
            class MyCtx { }

            abstract IService {
                void DoWork() with MyCtx ctx;
            }

            class ServiceImpl : IService {
                public void DoWork() { }
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var contractDiags = GetContractDiagnostics(diagnostics);

        Assert.Single(contractDiags);
        Assert.Contains("DoWork", contractDiags[0].Message);
        Assert.Contains("ServiceImpl", contractDiags[0].Message);
        Assert.Contains("MyCtx", contractDiags[0].Message);
        Assert.Contains("IService", contractDiags[0].Message);
    }

    [Fact]
    public void AbstractMethod_ImplementorMatchesWithClause_NoDiagnostic()
    {
        // An abstract type method declares a with-clause, and the implementing class
        // method declares the same with-clause.
        var source = """
            class MyCtx { }

            abstract IService {
                void DoWork() with MyCtx ctx;
            }

            class ServiceImpl : IService {
                public void DoWork() with MyCtx ctx { }
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var contractDiags = GetContractDiagnostics(diagnostics);

        Assert.Empty(contractDiags);
    }

    [Fact]
    public void AbstractMethod_MultipleWithClauses_AllOmitted_EmitsMultipleDiagnostics()
    {
        // An abstract type method declares multiple with-clauses, and the implementing
        // class method omits all of them.
        var source = """
            class CtxA { }
            class CtxB { }

            abstract IService {
                void DoWork() with CtxA a with CtxB b;
            }

            class ServiceImpl : IService {
                public void DoWork() { }
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var contractDiags = GetContractDiagnostics(diagnostics);

        Assert.Equal(2, contractDiags.Length);
        Assert.Contains(contractDiags, d => d.Message.Contains("CtxA"));
        Assert.Contains(contractDiags, d => d.Message.Contains("CtxB"));
    }

    [Fact]
    public void AbstractMethod_MultipleWithClauses_PartiallyOmitted_EmitsDiagnosticForMissing()
    {
        // An abstract type method declares two with-clauses, and the implementing
        // class method only declares one of them.
        var source = """
            class CtxA { }
            class CtxB { }

            abstract IService {
                void DoWork() with CtxA a with CtxB b;
            }

            class ServiceImpl : IService {
                public void DoWork() with CtxA a { }
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var contractDiags = GetContractDiagnostics(diagnostics);

        Assert.Single(contractDiags);
        Assert.Contains("CtxB", contractDiags[0].Message);
    }

    [Fact]
    public void AbstractMethod_StructImplementor_OmitsWithClause_EmitsDiagnostic()
    {
        // A struct implementing an abstract type must also match with-clauses.
        var source = """
            class MyCtx { }

            abstract IService {
                void DoWork() with MyCtx ctx;
            }

            struct ServiceImpl : IService {
                public void DoWork() { }
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var contractDiags = GetContractDiagnostics(diagnostics);

        Assert.Single(contractDiags);
        Assert.Contains("DoWork", contractDiags[0].Message);
        Assert.Contains("ServiceImpl", contractDiags[0].Message);
        Assert.Contains("MyCtx", contractDiags[0].Message);
    }

    [Fact]
    public void AbstractMethod_NoWithClauses_NoDiagnostic()
    {
        // An abstract type method without with-clauses should not trigger any
        // contract diagnostics.
        var source = """
            abstract IService {
                void DoWork();
            }

            class ServiceImpl : IService {
                public void DoWork() { }
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        var contractDiags = GetContractDiagnostics(diagnostics);

        Assert.Empty(contractDiags);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: Abstract type dispatch uses abstract type method's with-clauses
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractDispatch_UsesAbstractWithClauses_MissingContext_EmitsDiagnostic()
    {
        // When calling through an abstract type, the context checker should use
        // the abstract type method's with-clauses to determine context requirements.
        var source = """
            class MyCtx { }

            abstract IService {
                void DoWork() with MyCtx ctx;
            }

            class ServiceImpl : IService {
                public void DoWork() with MyCtx ctx { }
            }

            class Program {
                public static void Main() {
                    IService svc = new ServiceImpl();
                    svc.DoWork();
                }
            }
            """;

        var diagnostics = CompileSource(source);
        var ctxDiags = diagnostics
            .Where(d => d.Id == ContextCheckerAnalyzer.MissingContext)
            .ToImmutableArray();

        Assert.Single(ctxDiags);
        Assert.Contains("DoWork", ctxDiags[0].Message);
        Assert.Contains("MyCtx", ctxDiags[0].Message);
    }

    [Fact]
    public void AbstractDispatch_UsesAbstractWithClauses_ContextProvided_NoDiagnostic()
    {
        // When calling through an abstract type with context provided, no diagnostic.
        var source = """
            class MyCtx { }

            abstract IService {
                void DoWork() with MyCtx ctx;
            }

            class ServiceImpl : IService {
                public void DoWork() with MyCtx ctx { }
            }

            class Program {
                public static void Main() {
                    IService svc = new ServiceImpl();
                    with var c = new MyCtx();
                    svc.DoWork();
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
