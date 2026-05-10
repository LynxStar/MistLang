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
/// Unit tests for the ContextCheckerAnalyzer's interface/trait contract enforcement (Task 5.3).
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
    // Tests: MIST_CTX_0006 should be emitted for interface contract violations
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InterfaceMethod_ImplementorOmitsWithClause_EmitsDiagnostic()
    {
        // An interface method declares a with-clause, but the implementing class
        // method does not declare it.
        var source = """
            class MyCtx { }

            interface IService {
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
    public void InterfaceMethod_ImplementorMatchesWithClause_NoDiagnostic()
    {
        // An interface method declares a with-clause, and the implementing class
        // method declares the same with-clause.
        var source = """
            class MyCtx { }

            interface IService {
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
    public void InterfaceMethod_MultipleWithClauses_AllOmitted_EmitsMultipleDiagnostics()
    {
        // An interface method declares multiple with-clauses, and the implementing
        // class method omits all of them.
        var source = """
            class CtxA { }
            class CtxB { }

            interface IService {
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
    public void InterfaceMethod_MultipleWithClauses_PartiallyOmitted_EmitsDiagnosticForMissing()
    {
        // An interface method declares two with-clauses, and the implementing
        // class method only declares one of them.
        var source = """
            class CtxA { }
            class CtxB { }

            interface IService {
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
    public void InterfaceMethod_StructImplementor_OmitsWithClause_EmitsDiagnostic()
    {
        // A struct implementing an interface must also match with-clauses.
        var source = """
            class MyCtx { }

            interface IService {
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
    public void InterfaceMethod_NoWithClauses_NoDiagnostic()
    {
        // An interface method without with-clauses should not trigger any
        // contract diagnostics.
        var source = """
            interface IService {
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
    // Tests: Trait structural matching with with-clauses
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitMethod_StructuralMatch_WithClausesMatch_Satisfies()
    {
        // A trait method declares a with-clause, and the type's method declares
        // the same with-clause. The type should satisfy the trait.
        var source = """
            class MyCtx { }

            trait Workable {
                void DoWork() with MyCtx ctx;
            }

            class Worker {
                public void DoWork() with MyCtx ctx { }
            }

            with Worker : Workable {
                DoWork as DoWork;
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        // No trait unsatisfied diagnostic should be emitted.
        var traitUnsatisfied = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_WITH_UNSATISFIED")
            .ToImmutableArray();

        Assert.Empty(traitUnsatisfied);
    }

    [Fact]
    public void TraitMethod_StructuralMismatch_WithClausesDiffer_CoveredByWithBlock()
    {
        // A trait method declares a with-clause, but the type's method does NOT
        // declare the same with-clause. The structural match should fail, but
        // the explicit with-block mapping covers it.
        var source = """
            class MyCtx { }

            trait Workable {
                void DoWork() with MyCtx ctx;
            }

            class Worker {
                public void DoWork() { }
            }

            with Worker : Workable {
                DoWork as DoWork;
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        // With explicit mapping, it should still satisfy (with-block mappings
        // bypass structural matching).
        var traitUnsatisfied = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_WITH_UNSATISFIED")
            .ToImmutableArray();

        Assert.Empty(traitUnsatisfied);
    }

    [Fact]
    public void TraitMethod_PureStructural_WithClausesDiffer_FailsSatisfaction()
    {
        // A trait has two methods: one with a with-clause and one without.
        // The type has both methods but the one that should have the with-clause
        // does NOT have it. The with-block only maps the second method explicitly.
        // The first method should fail structural matching due to with-clause mismatch.
        var source = """
            class MyCtx { }

            trait Workable {
                void DoWork() with MyCtx ctx;
                void Helper();
            }

            class Worker {
                public void DoWork() { }
                public void Helper() { }
                public void Src() { }
            }

            with Worker : Workable {
                Src as Helper;
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        // Worker.DoWork() doesn't have the with-clause, so it should NOT
        // structurally match trait Workable.DoWork(). The with-block only
        // maps Src -> Helper, so DoWork is unsatisfied.
        var traitUnsatisfied = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_WITH_UNSATISFIED")
            .ToImmutableArray();

        Assert.Single(traitUnsatisfied);
        Assert.Contains("DoWork", traitUnsatisfied[0].Message);
    }

    [Fact]
    public void TraitMethod_WithClausesMismatch_CoveredByExplicitMapping()
    {
        // A trait method declares a with-clause. The type has a method with the
        // same name but WITHOUT the with-clause. The structural match fails, but
        // the with-block maps a different source method to cover the trait member.
        var source = """
            class MyCtx { }

            trait Workable {
                void DoWork() with MyCtx ctx;
            }

            class Worker {
                public void DoWork() { }
                public void OtherMethod() { }
            }

            with Worker : Workable {
                OtherMethod as DoWork;
            }

            class Program {
                public static void Main() { }
            }
            """;

        var diagnostics = CompileSource(source);
        // The with-block maps OtherMethod -> DoWork, covering the trait member.
        var traitUnsatisfied = diagnostics
            .Where(d => d.Id.Value == "MIST_TRAIT_WITH_UNSATISFIED")
            .ToImmutableArray();

        Assert.Empty(traitUnsatisfied);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tests: Interface dispatch uses interface method's with-clauses
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InterfaceDispatch_UsesInterfaceWithClauses_MissingContext_EmitsDiagnostic()
    {
        // When calling through an interface type, the context checker should use
        // the interface method's with-clauses to determine context requirements.
        var source = """
            class MyCtx { }

            interface IService {
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
    public void InterfaceDispatch_UsesInterfaceWithClauses_ContextProvided_NoDiagnostic()
    {
        // When calling through an interface type with context provided, no diagnostic.
        var source = """
            class MyCtx { }

            interface IService {
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
