// Feature: unknown-type (Phase D, Task 27)
// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5**
//
// Unit tests for Task 27: trait satisfaction with `unknown`. Covers:
//
//   • 12.1 — a trait member whose return type is `unknown` matches any
//     non-`void` concrete return on the implementer.
//   • 12.2 — a trait member whose return type is a `Closed_Generic_With_Unknown`
//     (e.g., `List<unknown>`) matches an implementer whose return is the
//     same generic with any type argument (e.g., `List<int>`), subject
//     to the class-only restriction from Req 8.6 (struct generics are
//     rejected).
//   • 12.3 — parameter positions require exact matching; `unknown` in a
//     trait parameter requires exactly `unknown` in the implementer and
//     vice versa.
//   • 12.4 — a trait field declaration whose type is `unknown` is
//     rejected by Task 11.1's field check (MIST_SB_0050). This test
//     locks in that coverage.
//   • 12.5 — combined coverage of the above behaviors.
//
// Note on 27.3 (reachability-based member filtering on trait-typed
// receivers): traits in v1 do not carry type parameters, so the
// filtering rule of Req 10 / Req 12.5 reduces to a no-op at the trait
// level — any `unknown` appearing in a trait's signature is directly
// visible and already handled by the bare-unknown rejection
// (MIST_TC_0065 / MIST_TC_0066) from Task 25. This file does not
// add new filtering tests for 27.3; the existing
// `UnknownBareRejectionTests` cover the operative path.
//
// All tests run fully in-process via the shared `SemanticModelBuilder`
// + `TypeCheckerAnalyzer` — no `dotnet` process is spawned, per the
// workspace test-process-spawning policy.

using System.Collections.Immutable;
using System.Linq;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for trait satisfaction with the `unknown` top type
/// (unknown-type feature, Task 27 / Requirement 12).
/// </summary>
public class UnknownTraitSatisfactionTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    /// <summary>
    /// MIST_SB_0050 — `unknown` used as a field type (Task 11.1, Req 5.1
    /// applied to traits per Req 12.4).
    /// </summary>
    private const string UnknownAsFieldTypeDiagnosticId = "MIST_SB_0050";

    /// <summary>
    /// MIST_TRAIT_UNSATISFIED — emitted by the type checker at an
    /// assignment site when the source type does not satisfy the target
    /// trait.
    /// </summary>
    private const string TraitUnsatisfiedDiagnosticId = "MIST_TRAIT_UNSATISFIED";

    /// <summary>
    /// Lexes, parses, builds the semantic model, then runs the type
    /// checker against <paramref name="source"/>. Returns the combined
    /// diagnostic set from the semantic model builder and the type
    /// checker, along with the built model so tests can interrogate
    /// the <see cref="TraitSatisfactionMap"/> directly.
    /// </summary>
    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) CompileAndCollect(
        string source)
    {
        var sourceText = new SourceText("test.mist", source);

        var lexBag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, lexBag);
        Assert.False(lexBag.HasErrors, "Lex should not produce errors");

        var parseBag = new DiagnosticBag();
        var parseResult = Parser.Parse(tokens, parseBag);
        Assert.False(parseBag.HasErrors, "Parse should not produce errors");
        Assert.NotNull(parseResult.Root);

        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));

        var semBag = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root!),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semBag);

        var tcBag = new DiagnosticBag();
        _analyzer.Analyze(model, tcBag);

        var all = semBag.ToImmutableArray()
            .Concat(tcBag.ToImmutableArray())
            .ToImmutableArray();

        return (model, all);
    }

    private static ImmutableArray<Diagnostic> WithId(
        ImmutableArray<Diagnostic> diagnostics, string id)
    {
        return diagnostics.Where(d => d.Id.Value == id).ToImmutableArray();
    }

    private static IType? FindType(ISemanticModel model, string name, SymbolKind kind)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == kind)
            ?.DeclaredType;
    }

    private static TraitType? FindTrait(ISemanticModel model, string name)
    {
        return FindType(model, name, SymbolKind.Trait) as TraitType;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 27.5 bullet 1 — Trait `trait T { fn get() : unknown; }` is satisfied
    // by `class C { fn get() : int { ... } }`. Validates Req 12.1: a trait
    // return of `unknown` matches any non-void concrete return on the
    // implementer.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitReturnUnknown_IsSatisfiedBy_ConcreteIntReturn()
    {
        const string source =
            "trait T {\n" +
            "    unknown Get();\n" +
            "}\n" +
            "class C {\n" +
            "    public int Get() { return 0; }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { T t = new C(); }\n" +
            "}";

        var (model, diagnostics) = CompileAndCollect(source);

        // The assignment `T t = new C()` must type-check without
        // MIST_TRAIT_UNSATISFIED — the implementer's concrete `int`
        // return satisfies the trait's `unknown` return (Req 12.1).
        Assert.Empty(WithId(diagnostics, TraitUnsatisfiedDiagnosticId));

        // And the satisfaction map must record the structural match.
        var trait = FindTrait(model, "T");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.True(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "C should structurally satisfy T under Req 12.1.");
    }

    /// <summary>
    /// A trait return of <c>unknown</c> does NOT match a <c>void</c>
    /// implementer return (Req 12.1 — "any non-void return type").
    /// </summary>
    [Fact]
    public void TraitReturnUnknown_IsNotSatisfiedBy_VoidReturn()
    {
        const string source =
            "trait T {\n" +
            "    unknown Act();\n" +
            "}\n" +
            "class C {\n" +
            "    public void Act() { }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { T t = new C(); }\n" +
            "}";

        var (model, diagnostics) = CompileAndCollect(source);

        // The implementer's `void` return does not satisfy `unknown`,
        // so the assignment must fail with MIST_TRAIT_UNSATISFIED.
        Assert.NotEmpty(WithId(diagnostics, TraitUnsatisfiedDiagnosticId));

        var trait = FindTrait(model, "T");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.False(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "C must not satisfy T when the implementer returns void.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 27.5 bullet 2 — Trait `trait T { fn get() : List<unknown>; }` is
    // satisfied by `class C { fn get() : List<int> { ... } }`. Validates
    // Req 12.2: a trait return of `G<..., unknown, ...>` matches an
    // implementer return of `G<..., T_i, ...>` via covariant unknown
    // widening (Req 8.1, 8.2) — subject to the class-only rule (Req 8.6).
    // Here `List` is a user-declared class generic, so widening fires.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitReturnListOfUnknown_IsSatisfiedBy_ListOfInt()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "trait MyTrait {\n" +
            "    List<unknown> Get();\n" +
            "}\n" +
            "class C {\n" +
            "    public List<int> Get() { return new List<int>(); }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { MyTrait t = new C(); }\n" +
            "}";

        var (model, diagnostics) = CompileAndCollect(source);

        // `List<int>` widens to `List<unknown>` at the return position
        // because `List` is a class generic (Req 12.2 + Req 8.1/8.6).
        Assert.Empty(WithId(diagnostics, TraitUnsatisfiedDiagnosticId));

        var trait = FindTrait(model, "MyTrait");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.True(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "C should satisfy MyTrait under class-generic widening (Req 12.2).");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 27.5 bullet 3 — Trait `trait T { fn get() : Optional<unknown>; }`
    // is NOT satisfied by `class C { fn get() : Optional<int> { ... } }`
    // when `Optional` is a struct. Validates the struct-generic
    // restriction from Req 8.6 applied to trait return positions.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitReturnStructOfUnknown_IsNotSatisfiedBy_StructOfInt()
    {
        // `Optional` is declared as a struct — covariant unknown widening
        // must reject the match because of Req 8.6 (struct generics are
        // not reference-covariant).
        const string source =
            "struct Optional<V> {\n" +
            "    public V value;\n" +
            "}\n" +
            "trait T {\n" +
            "    Optional<unknown> Get();\n" +
            "}\n" +
            "class C {\n" +
            "    public Optional<int> Get() { return new Optional<int>(); }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { T t = new C(); }\n" +
            "}";

        var (model, diagnostics) = CompileAndCollect(source);

        // The struct widening is rejected, so the match fails; the
        // assignment produces MIST_TRAIT_UNSATISFIED.
        Assert.NotEmpty(WithId(diagnostics, TraitUnsatisfiedDiagnosticId));

        var trait = FindTrait(model, "T");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.False(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "C must not satisfy T under struct-generic widening (Req 8.6).");
    }

    /// <summary>
    /// Counter-example for bullet 3: with the same struct definition, an
    /// implementer whose return is <c>Optional&lt;unknown&gt;</c> (exact
    /// match) DOES satisfy the trait because no widening is required.
    /// This guards against over-rejection on struct generics.
    /// </summary>
    [Fact]
    public void TraitReturnStructOfUnknown_IsSatisfiedBy_ExactStructOfUnknownReturn()
    {
        const string source =
            "struct Optional<V> {\n" +
            "    public V value;\n" +
            "}\n" +
            "trait T {\n" +
            "    Optional<unknown> Get();\n" +
            "}\n" +
            "class C {\n" +
            "    public Optional<unknown> Get() { return new Optional<unknown>(); }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { T t = new C(); }\n" +
            "}";

        // Note: `new Optional<unknown>()` fires MIST_SB_0053 per Task 12
        // because `unknown` is banned at generic construction sites. We
        // are not concerned with that diagnostic here — only that the
        // trait-satisfaction path accepts the signature match when both
        // sides use `Optional<unknown>` exactly.
        var (model, _) = CompileAndCollect(source);

        var trait = FindTrait(model, "T");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.True(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "Exact unknown-to-unknown match on a struct generic must succeed.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 27.5 bullet 4 — Trait `trait T { fn put(x: unknown); }` is NOT
    // satisfied by `class C { fn put(x: int) { ... } }`. Validates
    // Req 12.3: parameter-position matching is exact — `unknown` in a
    // trait parameter requires exactly `unknown` in the implementer.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitParameterUnknown_IsNotSatisfiedBy_ConcreteIntParameter()
    {
        const string source =
            "trait T {\n" +
            "    void Put(unknown x);\n" +
            "}\n" +
            "class C {\n" +
            "    public void Put(int x) { }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { T t = new C(); }\n" +
            "}";

        var (model, diagnostics) = CompileAndCollect(source);

        Assert.NotEmpty(WithId(diagnostics, TraitUnsatisfiedDiagnosticId));

        var trait = FindTrait(model, "T");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.False(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "C must not satisfy T when trait parameter `unknown` is "
            + "paired with a concrete `int` (Req 12.3).");
    }

    /// <summary>
    /// The symmetric direction of Req 12.3 — `unknown` in the
    /// implementer's parameter does not match a concrete parameter in
    /// the trait.
    /// </summary>
    [Fact]
    public void TraitParameterConcrete_IsNotSatisfiedBy_UnknownParameter()
    {
        const string source =
            "trait T {\n" +
            "    void Put(int x);\n" +
            "}\n" +
            "class C {\n" +
            "    public void Put(unknown x) { }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { T t = new C(); }\n" +
            "}";

        var (model, diagnostics) = CompileAndCollect(source);

        Assert.NotEmpty(WithId(diagnostics, TraitUnsatisfiedDiagnosticId));

        var trait = FindTrait(model, "T");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.False(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "C must not satisfy T when the implementer has an `unknown` "
            + "parameter paired with a concrete trait parameter (Req 12.3).");
    }

    /// <summary>
    /// Exact-match variant of Req 12.3: `unknown` in both trait and
    /// implementer parameter positions is a valid structural match.
    /// </summary>
    [Fact]
    public void TraitParameterUnknown_IsSatisfiedBy_ExactUnknownParameter()
    {
        const string source =
            "trait T {\n" +
            "    void Put(unknown x);\n" +
            "}\n" +
            "class C {\n" +
            "    public void Put(unknown x) { }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use() { T t = new C(); }\n" +
            "}";

        var (model, diagnostics) = CompileAndCollect(source);

        Assert.Empty(WithId(diagnostics, TraitUnsatisfiedDiagnosticId));

        var trait = FindTrait(model, "T");
        var classType = FindType(model, "C", SymbolKind.Class);
        Assert.NotNull(trait);
        Assert.NotNull(classType);
        Assert.True(
            model.TraitSatisfactionMap.Satisfies(classType!, trait!),
            "Exact unknown-to-unknown match on a trait parameter must succeed.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 27.5 bullet 5 — Trait with `unknown` return, invoked on a
    // trait-typed receiver, filters `WritesT` members correctly. For v1,
    // traits do not carry type parameters (see the file-level comment on
    // 27.3), so this bullet reduces to verifying that a trait-typed
    // receiver with an `unknown`-return member works end-to-end without
    // spurious filtering and that the bare-unknown rejection fires when
    // the returned value is used in a member-access position.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitTypedReceiver_UnknownReturn_MemberAccessOnResult_IsRejected()
    {
        // The trait method returns `unknown`; the resulting value is
        // bare-`unknown`-typed, so calling a member on it must fire
        // MIST_TC_0065 (Task 25.1 / Req 13.1). This indirectly
        // validates that 27.3's filtering is unnecessary at the trait
        // level — the bare-unknown rejection covers the relevant path
        // for v1.
        const string source =
            "trait T {\n" +
            "    unknown Get();\n" +
            "}\n" +
            "class C {\n" +
            "    public int Get() { return 0; }\n" +
            "}\n" +
            "class Driver {\n" +
            "    public void Use(T t) { t.Get().ToString(); }\n" +
            "}";

        var (_, diagnostics) = CompileAndCollect(source);

        // The bare-unknown member access on the result must be flagged.
        Assert.NotEmpty(WithId(diagnostics, "MIST_TC_0065"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 27.4 — Trait field declarations with `unknown` are rejected by
    // Task 11.1's field check (MIST_SB_0050). This lock-in test ensures
    // that the existing storage ban applies to trait fields too, per
    // Req 12.4.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TraitFieldOfUnknown_EmitsStorageBanDiagnostic()
    {
        const string source =
            "trait T {\n" +
            "    unknown f;\n" +
            "}";

        var (_, diagnostics) = CompileAndCollect(source);

        var hits = WithId(diagnostics, UnknownAsFieldTypeDiagnosticId);
        Assert.NotEmpty(hits);
    }

    /// <summary>
    /// A trait field whose declared type transitively contains
    /// <c>unknown</c> (e.g., <c>List&lt;unknown&gt;</c>) must also be
    /// rejected by the same storage-ban cascade (Req 5.1 + Req 7.5,
    /// applied to traits per Req 12.4).
    /// </summary>
    [Fact]
    public void TraitFieldOfListUnknown_EmitsStorageBanDiagnostic()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "}\n" +
            "trait T {\n" +
            "    List<unknown> xs;\n" +
            "}";

        var (_, diagnostics) = CompileAndCollect(source);

        var hits = WithId(diagnostics, UnknownAsFieldTypeDiagnosticId);
        Assert.NotEmpty(hits);
    }
}
