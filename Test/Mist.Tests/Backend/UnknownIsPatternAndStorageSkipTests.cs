// Feature: unknown-type (Phase E, Task 32)
// **Validates: Requirements 15.3, 15.5**
//
// Unit tests for Task 32: C# emission of `is` pattern expressions and
// best-effort storage skipping.
//
// Two sub-rules exercised here:
//
//   * Task 32.1 / Req 15.3 — Pattern emission is unchanged: Mist
//     `foo is Optional<int> oi` emits C# `foo is Optional<int> oi`
//     directly. C# preserves full generic type information at
//     runtime, so the pattern match tests the closed constructed type,
//     not an erased form. This is a regression test that locks in the
//     existing emitter behavior so future changes (for instance, to
//     the `TypePatternPrimaryPatternNode` code path) cannot silently
//     break `is`-pattern emission.
//
//   * Task 32.3 / Req 15.5 — Best-effort storage skipping: when a
//     field, property, or indexer has a declared type that satisfies
//     `TypeContainment.IsStorageBanned` (e.g., `unknown f` or
//     `List<unknown> xs`), the Semantic_Model_Builder emits the
//     appropriate `MIST_SB_005x` diagnostic and the emitter skips the
//     declaration entirely. This produces compilable C# for the
//     unaffected portions of a program even when the storage-ban
//     diagnostic has turned the compilation into an error state.
//     Per Req 15.5, the Emitter "SHALL NOT emit any declaration of an
//     `unknown` Mist field, property, indexer..."
//
// These tests follow the workspace's `test-process-spawning.md`
// policy: compilation is performed in-process via the lexer/parser/
// semantic-model-builder/emitter pipeline directly. No `dotnet`
// subprocess is spawned. The emitted C# is inspected as text. This
// mirrors the pattern used by `UnknownTypeCastEmissionTests` (Task
// 30.3) and `UnknownWideningEmissionTests` (Task 31.3).

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests covering Task 32 of the unknown-type feature:
/// <list type="bullet">
/// <item>
/// <description>
/// Task 32.1 — <c>is</c> pattern emission lowers Mist's
/// <c>foo is Optional&lt;int&gt; oi</c> to C#
/// <c>foo is Optional&lt;int&gt; oi</c>, preserving the closed
/// constructed type and the binding identifier.
/// </description>
/// </item>
/// <item>
/// <description>
/// Task 32.3 — Fields (and by extension properties / indexers) whose
/// declared type contains <c>unknown</c> at any storage position are
/// skipped by the emitter, producing compilable C# for the unaffected
/// members of a program that already carries a storage-ban diagnostic.
/// </description>
/// </item>
/// </list>
/// Each test lexes and parses a small Mist source string, builds the
/// semantic model, optionally runs the type checker, and invokes
/// <see cref="CSharpEmitter"/> directly. The emitted C# file contents
/// are inspected with substring assertions. No external process is
/// spawned.
/// </summary>
public sealed class UnknownIsPatternAndStorageSkipTests : IDisposable
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();
    private readonly string outputDir;

    public UnknownIsPatternAndStorageSkipTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistUnknownIsAndSkip_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); }
            catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Lexes and parses <paramref name="mistSource"/>, builds the
    /// semantic model, runs the type checker, and emits C#. Fails the
    /// test if any stage produces an error diagnostic. Returns the
    /// concatenated contents of the user-emitted <c>.cs</c> file(s).
    /// Used by Task 32.1 where the program is well-typed.
    /// </summary>
    private string EmitCleanAndReadCsharp(string mistSource)
    {
        var (_, csharp) = RunPipeline(mistSource, expectErrors: false);
        return csharp;
    }

    /// <summary>
    /// Same shape as <see cref="EmitCleanAndReadCsharp"/> but tolerant
    /// of semantic / type-check errors. Used by Task 32.3 to exercise
    /// the best-effort skipping path: the storage-ban diagnostic
    /// <c>MIST_SB_0050</c> is an error, yet the emitter must still run
    /// and produce compilable C# for the unaffected members. Returns a
    /// tuple of (all-diagnostics, emitted-C#).
    /// </summary>
    private (ImmutableArray<Diagnostic> Diagnostics, string Csharp)
        EmitBestEffortAndReadCsharp(string mistSource)
    {
        return RunPipeline(mistSource, expectErrors: true);
    }

    private (ImmutableArray<Diagnostic> Diagnostics, string Csharp) RunPipeline(
        string mistSource, bool expectErrors)
    {
        var sourceText = new SourceText("test.mist", mistSource);

        // Lex.
        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, ErrorReport("lex", lexBag.ToImmutableArray()));

        // Parse.
        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, ErrorReport("parse", parseBag.ToImmutableArray()));
        Assert.NotNull(parseResult.Root);

        // Build semantic model.
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var semBag = new DiagnosticBag();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);
        if(!expectErrors)
        {
            Assert.False(semBag.HasErrors, ErrorReport("semantic model", semBag.ToImmutableArray()));
        }

        // Type check. Skipped if we already observed semantic errors —
        // the type checker is not a safe pass to run when the semantic
        // model is known to be inconsistent, and task 32.3's scenario
        // only needs the storage-ban diagnostic emitted from the
        // semantic-model pass to fire.
        var tcBag = new DiagnosticBag();
        if(!semBag.HasErrors)
        {
            analyzer.Analyze(model, tcBag);
            if(!expectErrors)
            {
                Assert.False(tcBag.HasErrors, ErrorReport("type check", tcBag.ToImmutableArray()));
            }
        }

        // Emit. The driver normally short-circuits emission when there
        // are upstream errors (CompilerDriver.cs), but Task 32.2's spec
        // is explicit that invoking the emitter directly with an
        // errored-out model must still produce compilable output for
        // the unaffected portions. We invoke `CSharpEmitter.Emit`
        // directly to exercise that path.
        CleanOutputDir();
        var emitBag = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", emitBag);
        Assert.False(emitBag.HasErrors, ErrorReport("emit", emitBag.ToImmutableArray()));

        // Read user-emitted .cs files. Exclude the `MistVoid.g.cs`
        // helper that the backend generates when a compilation unit
        // uses `void` as a value.
        var csFiles = Directory.GetFiles(outputDir, "*.cs", SearchOption.AllDirectories)
            .Where(p =>
            {
                var name = Path.GetFileName(p);
                return name != "MistVoid.g.cs";
            })
            .ToArray();

        Assert.NotEmpty(csFiles);
        var csharp = string.Join("\n", csFiles.Select(File.ReadAllText));

        // Collect every diagnostic produced upstream so tests can
        // assert on specific diagnostic IDs.
        var allDiagnostics = semBag.ToImmutableArray()
            .Concat(tcBag.ToImmutableArray())
            .Concat(emitBag.ToImmutableArray())
            .ToImmutableArray();

        return (allDiagnostics, csharp);
    }

    private static string ErrorReport(string stage, ImmutableArray<Diagnostic> bag)
    {
        var errors = bag.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        if(errors.IsEmpty)
        {
            return "";
        }

        return $"Unexpected errors during {stage}:\n" + string.Join(
            "\n",
            errors.Select(d => $"  [{d.Id.Value}] {d.Message}"));
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Collapses whitespace so substring assertions below are
    /// insensitive to the emitter's spacing / indentation choices.
    /// </summary>
    private static string Collapse(string s)
    {
        return new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 32.1 / Req 15.3
    //
    // Mist `foo is Optional<int> oi` emits C# `foo is Optional<int> oi`
    // directly. The emitter's existing `EmitPattern` /
    // `EmitPrimaryPattern` pipeline already produces this shape — this
    // test locks in that behavior so the `is`-pattern lowering cannot
    // silently regress when other pattern code paths are touched.
    //
    // We use a locally-declared `Optional<T>` class rather than the
    // stdlib to keep the test self-contained (the builder is invoked
    // without loading the stdlib, mirroring the pattern used by
    // `UnknownWideningEmissionTests` and `StorageBanDeclarationTests`).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Canonical Task 32.1 bullet: a type pattern whose pattern type is
    /// <c>Optional&lt;int&gt;</c> emits a C# type pattern against the
    /// same closed constructed type. The binding identifier <c>oi</c>
    /// is preserved verbatim.
    /// </summary>
    [Fact]
    public void IsPattern_OptionalIntWithBinding_EmitsClosedGenericPattern()
    {
        const string mistSource =
            "class Optional<T> { public int has; public Optional() { this.has = 0; } }\n" +
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public void Narrow(Optional<int> foo) {\n" +
            "        if (foo is Optional<int> oi) { }\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitCleanAndReadCsharp(mistSource);

        // Assert on whitespace-sensitive shape first — this is the
        // precise guarantee called out in Req 15.3 ("C# pattern match
        // against the corresponding constructed C# type"). The
        // emitter's spacing around `is` matches the AST pretty-print
        // convention used by `PatternEmissionPropertyTests`, so a
        // non-collapsed substring check is appropriate here.
        Assert.Contains("is Optional<int> oi", csharp);

        // Negative: the binding must not be mangled, and the pattern
        // type must not be erased to `object` even though `object`
        // casts appear in other unknown-type emission paths. Req 15.3
        // is explicit that the `is` test uses the closed constructed
        // type, preserving C#'s generic metadata at runtime.
        Assert.DoesNotContain("is object oi", csharp);
        Assert.DoesNotContain("is Optional<object> oi", csharp);
    }

    /// <summary>
    /// Defensive coverage for the whitespace-collapsed form. If a
    /// future emitter refactor changes the spacing (for instance,
    /// to emit <c>is&#x00A0;Optional&lt;int&gt;&#x00A0;oi</c> using
    /// non-breaking spaces, or to insert comments), the whitespace-
    /// insensitive check still catches erasure or binding-rename
    /// regressions.
    /// </summary>
    [Fact]
    public void IsPattern_OptionalIntWithBinding_WhitespaceInsensitive()
    {
        const string mistSource =
            "class Optional<T> { public int has; public Optional() { this.has = 0; } }\n" +
            "class Foo {\n" +
            "    public static void Main() { }\n" +
            "    public void Narrow(Optional<int> foo) {\n" +
            "        if (foo is Optional<int> oi) { }\n" +
            "    }\n" +
            "}\n";

        var csharp = EmitCleanAndReadCsharp(mistSource);
        var normalized = Collapse(csharp);

        // `foo is Optional<int> oi` collapses to
        // `fooisOptional<int>oi` — the adjacency of `is` between the
        // scrutinee and the pattern, and the binding `oi` trailing
        // the pattern type, together witness the end-to-end shape.
        Assert.Contains("fooisOptional<int>oi", normalized);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 32.3 / Req 15.5
    //
    // `class Foo { unknown f; int g; }` produces:
    //   * a `MIST_SB_0050` diagnostic from the semantic-model builder,
    //     naming the declaring type `Foo` and the field `f`
    //     (Requirement 5.1, 16.1);
    //   * emitted C# that contains the `int g;` field declaration but
    //     NOT the `f` field — the emitter's
    //     `IsStorageBannedMember` check in `EmitMemberDeclaration`
    //     skips declarations whose declared type contains `unknown`
    //     at any storage position.
    //
    // Skipping the banned field is what makes the "best-effort"
    // output compilable: emitting `object f;` would be misleading
    // because the Mist program is already invalid, and emitting the
    // field with its original Mist type would produce invalid C#.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Canonical Task 32.3 scenario: a class with one banned field
    /// (<c>unknown f</c>) and one legal field (<c>int g</c>) emits a
    /// C# class that contains only the legal field, and the
    /// <c>MIST_SB_0050</c> diagnostic is present in the bag.
    /// </summary>
    [Fact]
    public void StorageBan_UnknownField_IsSkippedAndLegalFieldIsEmitted()
    {
        const string mistSource =
            "class Foo {\n" +
            "    unknown f;\n" +
            "    int g;\n" +
            "}\n";

        var (diagnostics, csharp) = EmitBestEffortAndReadCsharp(mistSource);

        // Bullet 1 — the storage-ban diagnostic is present. The
        // diagnostic ID is the canonical `MIST_SB_0050` allocated in
        // Task 11.1 (Requirements 5.1, 16.1).
        var storageBanHits = diagnostics
            .Where(d => d.Id.Value == "MIST_SB_0050")
            .ToImmutableArray();
        _ = Assert.Single(storageBanHits);
        Assert.Equal(DiagnosticSeverity.Error, storageBanHits[0].Severity);
        Assert.Contains("Foo", storageBanHits[0].Message);
        Assert.Contains("f", storageBanHits[0].Message);

        // Bullet 2 — the emitter skipped the banned field. The
        // whitespace-collapsed check catches any variant spacing
        // (`int f;`, `int  f;`, `int\tf;`). We also assert the bare
        // `object f` spelling is absent, to catch a potential bug
        // where a future refactor of the type map quietly lowered
        // `unknown` to `object` at the field declaration (which is
        // what the pre-Task-32 emitter would have done).
        var normalized = Collapse(csharp);
        Assert.DoesNotContain("objectf;", normalized);
        Assert.DoesNotContain("objectf=", normalized);
        // Belt-and-suspenders: no banned field slot under any name.
        // The field's Mist identifier `f` must not appear as a
        // declarator — this check is stricter than needed (if a local
        // or method named `f` existed it would trip), but the source
        // above declares no such identifiers, so the substring is a
        // reliable witness.
        Assert.DoesNotContain(" f;", csharp);
        Assert.DoesNotContain(" f =", csharp);

        // Bullet 3 — the legal field `g` is emitted. The emitter maps
        // Mist `int` to C# `int` (see `CSharpTypeMap`), so the C#
        // output contains a declarator matching `int g;` (or the
        // whitespace-collapsed form `intg;` when the emitter uses
        // tight spacing).
        Assert.Contains("intg;", normalized);
    }

    /// <summary>
    /// Extended coverage: the storage-ban skip also handles the
    /// cascade rule (Req 7.5, 7.6). A <c>List&lt;unknown&gt;</c>
    /// field in the same class is banned and skipped alongside a
    /// bare <c>unknown</c> field. This locks in that the emitter's
    /// <c>IsStorageBannedMember</c> helper consults the semantic
    /// type via <c>GetDeclaredSymbol</c> rather than matching only
    /// the surface-level AST <c>UnknownSingleTypeRefSingleTypeRefNode</c>.
    /// </summary>
    [Fact]
    public void StorageBan_CascadeField_IsSkippedTogetherWithBareUnknown()
    {
        // Declare a local `List<T>` so the test is self-contained —
        // same pattern as the Phase-B storage-ban declaration tests.
        const string mistSource =
            "class List<T> { }\n" +
            "class Foo {\n" +
            "    unknown f;\n" +
            "    List<unknown> xs;\n" +
            "    int g;\n" +
            "}\n";

        var (diagnostics, csharp) = EmitBestEffortAndReadCsharp(mistSource);

        // Two `MIST_SB_0050` diagnostics: one for the bare `unknown f`
        // and one for the cascade `List<unknown> xs`.
        var storageBanHits = diagnostics
            .Where(d => d.Id.Value == "MIST_SB_0050")
            .ToImmutableArray();
        Assert.Equal(2, storageBanHits.Length);

        // Both banned fields are skipped; `g` survives.
        var normalized = Collapse(csharp);
        Assert.Contains("intg;", normalized);

        // Neither banned field leaks into the C# output.
        Assert.DoesNotContain(" f;", csharp);
        Assert.DoesNotContain(" xs;", csharp);
        // And no `List<object>`-typed field leaks either.
        Assert.DoesNotContain("List<object>xs", normalized);
    }
}