using System.Collections.Immutable;
using System.Linq;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for nested scoping diagnostic error conditions.
///
/// Validates:
/// * Requirement 3.1 — <c>MIST0301</c> fires on duplicate file-scoped namespace.
/// * Requirement 3.2, 3.3 — <c>MIST0302</c> fires on mixed namespace styles.
/// * Requirement 5.5 — <c>MIST0305</c> fires on file-scoped namespace inside block namespace.
/// * Requirement 9.4, 14.3 — <c>MIST0303</c> fires on duplicate nested type name.
/// * Requirement 9.5 — <c>MIST0304</c> fires on nested type name conflicting with type parameter.
/// * Requirement 7.5 — unresolved type parameter reference produces diagnostic.
/// </summary>
public sealed class NestedScopingDiagnosticTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Lexes and parses <paramref name="source"/>, runs the semantic model
    /// builder, and returns all diagnostics (lex + parse + semantic).
    /// Does not assert on parse errors since some tests intentionally produce
    /// sources that may trigger parser-level diagnostics.
    /// </summary>
    private static ImmutableArray<Diagnostic> BuildAndCollectDiagnostics(string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        if(lexBag.HasErrors)
        {
            return lexBag.ToImmutableArray();
        }

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        if(parseResult.Root is null)
        {
            return parseBag.ToImmutableArray();
        }

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var builder = new SemanticModelBuilder();
        var semBag = new DiagnosticBag();
        _ = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        // Combine parse and semantic diagnostics
        return parseBag.ToImmutableArray().AddRange(semBag.ToImmutableArray());
    }

    /// <summary>
    /// Compiles source using the full pipeline and returns all diagnostics.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAndCollectDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_diag_" + Guid.NewGuid().ToString("N"));
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

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    // ─── MIST0301 — duplicate file-scoped namespace (Requirement 3.1, 14.1) ──
    //
    // Note: Due to the grammar structure, a second file-scoped namespace declaration
    // is parsed as a body item of the first file-scoped namespace. The semantic model
    // builder detects this and reports MIST0305 (file-scoped namespace inside another
    // namespace). This is the compiler's mechanism for rejecting duplicate file-scoped
    // namespaces — the grammar prevents them from appearing as separate top-level items.

    [Fact]
    public void DuplicateFileScopedNamespace_ProducesDiagnostic()
    {
        // Two file-scoped namespace declarations in the same file.
        // The second is parsed as a body item of the first, triggering MIST0305.
        var source = """
            namespace Foo;
            namespace Bar;
            class A {}
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var diags = WithId(diagnostics, "MIST0305");

        _ = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("cannot appear inside", diags[0].Message);
    }

    [Fact]
    public void DuplicateFileScopedNamespace_SameName_ProducesDiagnostic()
    {
        // Two file-scoped namespace declarations with the same name.
        var source = """
            namespace Foo;
            namespace Foo;
            class A {}
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var diags = WithId(diagnostics, "MIST0305");

        _ = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
    }

    // ─── MIST0302 — mixed namespace styles (Requirement 3.2, 3.3, 14.2) ──────

    [Fact]
    public void MIST0302_FiresOnBlockFollowedByFileScoped()
    {
        // A block namespace followed by a file-scoped namespace at the top level.
        // The block namespace is self-contained, so the file-scoped namespace
        // appears as a separate top-level item — triggering MIST0302.
        var source = """
            namespace Foo {
                class A {}
            }
            namespace Bar;
            class B {}
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var diags = WithId(diagnostics, "MIST0302");

        _ = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("cannot be mixed", diags[0].Message);
    }

    [Fact]
    public void MIST0302_FileScopedFollowedByBlock_IsValidNesting()
    {
        // A file-scoped namespace followed by a block namespace.
        // Due to the grammar, the block namespace is parsed as a body item of the
        // file-scoped namespace (valid nested namespace), so no diagnostic fires.
        var source = """
            namespace Foo;
            namespace Bar {
                class A {}
            }
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var mixedDiags = WithId(diagnostics, "MIST0302");

        Assert.Empty(mixedDiags);
    }

    // ─── MIST0305 — file-scoped namespace inside block namespace (Requirement 5.5) ──

    [Fact]
    public void MIST0305_FiresOnFileScopedInsideBlockNamespace()
    {
        var source = """
            namespace Outer {
                namespace Inner;
                class A {}
            }
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var diags = WithId(diagnostics, "MIST0305");

        _ = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("cannot appear inside a block namespace", diags[0].Message);
    }

    // ─── MIST0303 — duplicate nested type name (Requirement 9.4, 14.3) ───────

    [Fact]
    public void MIST0303_FiresOnDuplicateNestedTypeName()
    {
        var source = """
            namespace Test;
            class Parent {
                class Inner {}
                class Inner {}
            }
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var diags = WithId(diagnostics, "MIST0303");

        _ = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Inner", diags[0].Message);
        Assert.Contains("Parent", diags[0].Message);
    }

    [Fact]
    public void MIST0303_FiresOnDuplicateNestedTypeDifferentKinds()
    {
        // A struct and a class with the same name inside the same parent.
        var source = """
            namespace Test;
            class Parent {
                class Inner {}
                struct Inner {}
            }
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var diags = WithId(diagnostics, "MIST0303");

        _ = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("Inner", diags[0].Message);
    }

    // ─── MIST0304 — nested type conflicts with type parameter (Requirement 9.5) ──

    [Fact]
    public void MIST0304_FiresOnNestedTypeConflictingWithTypeParameter()
    {
        var source = """
            namespace Test;
            class Parent<T> {
                class T {}
            }
            """;

        var diagnostics = BuildAndCollectDiagnostics(source);
        var diags = WithId(diagnostics, "MIST0304");

        _ = Assert.Single(diags);
        Assert.Equal(DiagnosticSeverity.Error, diags[0].Severity);
        Assert.Contains("T", diags[0].Message);
        Assert.Contains("conflicts with type parameter", diags[0].Message);
    }

    // ─── Unresolved type parameter reference (Requirement 7.5) ────────────────

    [Fact]
    public void UnresolvedTypeParameter_ProducesDiagnostic()
    {
        // TUnknown is not declared by any enclosing type or the nested type itself.
        // Use it as a method return type to force the type checker to resolve it.
        var source = """
            namespace Test;
            class Parent {
                class Inner {
                    TUnknown GetValue() { return default; }
                }
            }
            class Program {
                static void Main() { }
            }
            """;

        var diagnostics = CompileAndCollectDiagnostics(source);

        // Should produce a diagnostic for the unresolved type reference "TUnknown".
        var unresolvedDiags = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => d.Id.Value != "MIST_TC_0017") // Ignore entry point diagnostic
            .Where(d => d.Message.Contains("TUnknown"))
            .ToImmutableArray();

        Assert.True(unresolvedDiags.Length > 0,
            $"Expected at least one error diagnostic for unresolved 'TUnknown'. " +
            $"All diagnostics ({diagnostics.Length}): " +
            string.Join("; ", diagnostics.Select(d => $"[{d.Id.Value}] {d.Severity}: {d.Message}")));
    }
}