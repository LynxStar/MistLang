// Task 19.3 — Unit tests for this-tier call-site checking.
//
// Verifies that the this-tier call-site check correctly emits
// MIST_OWN_THIS_TIER_MISMATCH when the receiver binding's tier is
// insufficient for the method being called. Also tests that 'with owned'
// methods consume the receiver (subsequent access → MIST_OWN_USE_AFTER_MOVE).
//
// All compilation is in-process via a single shared CompilerDriver.
// 0 external process spawns (test-process-spawning policy).
//
// Validates: Requirements 17.13, 17.14, 17.15, 17.16

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Xunit;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for this-tier call-site checking (Task 19.3).
/// Verifies that the receiver binding's ownership tier is validated against the
/// method's inferred/declared this-tier requirement at each call site.
///
/// <b>Validates: Requirements 17.13, 17.14, 17.15, 17.16</b>
/// </summary>
public sealed class ThisTierCallSiteCheckingTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_tier_cs_" + Guid.NewGuid().ToString("N"));
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
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Diagnostic? FindById(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.FirstOrDefault(d => d.Id.Value == id);

    private static bool HasErrors(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Owned binding calls method requiring Borrowed → succeeds
    //    (Req 17.15: owned satisfies all tiers)
    //
    // An owned binding provides Owned tier, which satisfies Borrowed.
    // The method writes to an owned field → inferred this-tier = Borrowed.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OwnedBinding_CallsMethodRequiringBorrowed_Succeeds()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }

                public void Mutate(owned string newData)
                {
                    this.Data = newData;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new Container("hello");
                    c.Mutate("updated");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Owned tier satisfies Borrowed — no mismatch expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_THIS_TIER_MISMATCH"));
        Assert.False(HasErrors(diagnostics),
            $"Expected clean compilation but got errors: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Borrowed binding calls method requiring Borrowed → succeeds
    //    (Req 17.15: borrowed satisfies borrowed/frozen/readonly ref)
    //
    // A borrowed binding provides Borrowed tier, which satisfies Borrowed.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BorrowedBinding_CallsMethodRequiringBorrowed_Succeeds()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }

                public void Mutate(owned string newData)
                {
                    this.Data = newData;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new Container("hello");
                    borrowed Container b = c;
                    b.Mutate("updated");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Borrowed tier satisfies Borrowed — no mismatch expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_THIS_TIER_MISMATCH"));
        Assert.False(HasErrors(diagnostics),
            $"Expected clean compilation but got errors: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Frozen binding calls method requiring Borrowed
    //    → MIST_OWN_THIS_TIER_MISMATCH (Req 17.15, 17.16)
    //
    // A frozen binding provides Frozen tier, which does NOT satisfy Borrowed.
    // The method writes to an owned field → requires Borrowed.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FrozenBinding_CallsMethodRequiringBorrowed_EmitsThisTierMismatch()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }

                public void Mutate(owned string newData)
                {
                    this.Data = newData;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var c = new Container("hello");
                    frozen Container f = c;
                    f.Mutate("updated");
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Frozen tier does NOT satisfy Borrowed — mismatch expected.
        var diag = FindById(diagnostics, "MIST_OWN_THIS_TIER_MISMATCH");
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("Mutate", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Shared binding calls method requiring ReadonlyRef
    //    → MIST_OWN_THIS_TIER_MISMATCH (Req 17.15, 17.16)
    //
    // A shared (unrestricted) binding provides None tier, which does NOT
    // satisfy ReadonlyRef. The method only reads owned fields → inferred
    // this-tier = ReadonlyRef.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedBinding_CallsMethodRequiringReadonlyRef_EmitsThisTierMismatch()
    {
        const string source = """
            class Container
            {
                public owned string Data;

                public Container(owned string data)
                {
                    this.Data = data;
                }

                public string GetData()
                {
                    return this.Data;
                }
            }

            class Program
            {
                public static void Use(Container c)
                {
                    c.GetData();
                }

                public static void Main()
                {
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Shared binding provides None tier which does NOT satisfy ReadonlyRef.
        var diag = FindById(diagnostics, "MIST_OWN_THIS_TIER_MISMATCH");
        Assert.NotNull(diag);
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("GetData", diag.Message, StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Shared binding calls method with no tier requirement → succeeds
    //    (Req 17.15: shared satisfies methods with no tier requirement)
    //
    // A method that does not access any owned fields has inferred this-tier
    // = None. A shared binding can call it freely.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SharedBinding_CallsMethodWithNoTierRequirement_Succeeds()
    {
        const string source = """
            class Container
            {
                public owned string Data;
                public int Version;

                public Container(owned string data, int version)
                {
                    this.Data = data;
                    this.Version = version;
                }

                public int GetVersion()
                {
                    return this.Version;
                }
            }

            class Program
            {
                public static void Main()
                {
                    var c = new Container("hello", 1);
                    c.GetVersion();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // GetVersion() does not access owned fields → this-tier = None.
        // Shared binding satisfies None — no mismatch expected.
        Assert.Null(FindById(diagnostics, "MIST_OWN_THIS_TIER_MISMATCH"));
        Assert.False(HasErrors(diagnostics),
            $"Expected clean compilation but got errors: {string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 6. `with owned` method consumes receiver, subsequent access
    //    → MIST_OWN_USE_AFTER_MOVE (Req 17.13, 17.14)
    //
    // A method declared 'with owned' consumes the receiver — the caller's
    // binding is moved into the method and becomes dead after the call.
    // Subsequent access produces MIST_OWN_USE_AFTER_MOVE.
    //
    // NOTE: The `with {tier}` grammar is not yet connected to the full
    // CompilerDriver pipeline (parser produces the annotation but the
    // SemanticModelBuilder does not yet propagate it). This test builds
    // the semantic model directly, sets DeclaredThisTier = Owned on the
    // target method, and runs the full analysis (ThisTierInferencePass +
    // OwnershipAnalyzer flow-sensitive pass) to exercise the call-site
    // consumption behavior end-to-end.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithOwned_ConsumesReceiver_SubsequentAccess_EmitsUseAfterMove()
    {
        // Source: an owned binding calls Close() (to be marked 'with owned'),
        // then accesses the receiver again via GetName().
        const string source = """
            class Account
            {
                public owned string Name;

                public Account(owned string name)
                {
                    this.Name = name;
                }

                public string Close()
                {
                    return this.Name;
                }

                public string GetName()
                {
                    return this.Name;
                }
            }

            class Program
            {
                public static void Main()
                {
                    owned var acct = new Account("Alice");
                    acct.Close();
                    acct.GetName();
                }
            }
            """;

        // Build semantic model and manually set DeclaredThisTier on Close().
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var lexBag = new Mist.Common.Diagnostics.DiagnosticBag();
        var tokens = new Mist.Lexing.MistLexer().Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors);

        var parseBag = new Mist.Common.Diagnostics.DiagnosticBag();
        var parseResult = new Mist.Parsing.MistParser().Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors,
            $"Parse errors: {string.Join(", ", parseBag.ToImmutableArray().Select(d => d.Message))}");
        Assert.NotNull(parseResult.Root);

        var compilation = new Mist.Driver.Contracts.MistCompilation(
            ImmutableArray.Create(new Mist.Driver.Contracts.CompilationUnit(sourceText)));
        var semBag = new Mist.Common.Diagnostics.DiagnosticBag();
        var model = new SemanticModelBuilder().Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProgram",
            projectFolder: "/test",
            semBag);

        // Set DeclaredThisTier = Owned on the Close() method to simulate
        // the parsed `with owned` annotation.
        var closeMethod = (Mist.Semantics.Model.Symbol)model.AllSymbols
            .First(s => s.Name == "Close" && s.Kind == Mist.Semantics.Contracts.SymbolKind.Method);
        closeMethod.DeclaredThisTier = ThisTier.Owned;

        // Run the full analysis pipeline on the model.
        var analysisBag = new Mist.Common.Diagnostics.DiagnosticBag();
        var analyzer = new OwnershipAnalyzer();
        analyzer.Analyze(model, analysisBag);

        var diagnostics = analysisBag.ToImmutableArray();

        // After Close() (with owned), the receiver 'acct' should be consumed.
        // Subsequent access (GetName()) should produce MIST_OWN_USE_AFTER_MOVE.
        var diag = diagnostics.FirstOrDefault(d => d.Id.Value == "MIST_OWN_USE_AFTER_MOVE");
        Assert.True(diag is not null,
            $"Expected MIST_OWN_USE_AFTER_MOVE but got: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        Assert.Equal(DiagnosticSeverity.Error, diag!.Severity);
        Assert.Contains("acct", diag.Message, StringComparison.Ordinal);
    }
}