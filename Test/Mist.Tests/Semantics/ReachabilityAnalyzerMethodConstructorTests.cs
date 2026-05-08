// Feature: unknown-type (Phase C, Task 18.5)
// **Validates: Requirements 9.5, 9.6, 9.7, 9.8, 9.9**
//
// Unit tests for ReachabilityAnalyzer's method and constructor
// classification — the body-walk + parameter-type + conservative-dispatch
// rules added in Task 18.
//
// These tests compile a small Mist source fragment in-process through
// SemanticModelBuilder.Build (no dotnet process is spawned), extract the
// symbol table and expression-type map via internal accessors, and then
// invoke ReachabilityAnalyzer.Analyze directly on those inputs. Driving the
// analyzer from the real pipeline (rather than a hand-built symbol table,
// as Task 17 does) lets the tests exercise the actual AST shapes the
// parser produces for assignments, calls, and constructor chains.

using System.Collections.Immutable;
using System.Linq;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tests for <see cref="ReachabilityAnalyzer.Analyze"/>'s method and
/// constructor classification (Task 18.5). Each test compiles a small
/// Mist program, runs the analyzer, and asserts the classification of a
/// named member against the enclosing generic's type parameter <c>T</c>.
/// </summary>
public class ReachabilityAnalyzerMethodConstructorTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Compiles <paramref name="source"/> through lex → parse →
    /// <see cref="SemanticModelBuilder.Build"/>, asserts no lex/parse
    /// errors, then runs <see cref="ReachabilityAnalyzer.Analyze"/> on the
    /// resulting symbol table and expression-type map. Returns the map, the
    /// semantic model, and the diagnostics so callers can introspect type
    /// and symbol information.
    /// </summary>
    private (IReachabilityMap Map, ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) Analyze(string source)
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

        // Access the internal symbol table and expression-type map via the
        // InternalsVisibleTo("Mist.Tests") shared with the main assembly.
        var semanticModel = (SemanticModel)model;
        var map = ReachabilityAnalyzer.Analyze(semanticModel.SymbolTable, semanticModel.ExpressionTypes);

        return (map, model, semBag.ToImmutableArray());
    }

    /// <summary>
    /// Finds the generic type symbol named <paramref name="typeName"/> in the
    /// semantic model's symbol table, and returns its sole type parameter.
    /// Every test fixture here uses a single type parameter named <c>T</c>.
    /// </summary>
    private static (ISymbol TypeSymbol, TypeParameterType T) GetGenericTypeAndTypeParam(
        ISemanticModel model,
        string typeName)
    {
        var typeSymbol = model.AllSymbols
            .First(s => s.Kind is SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface
                && s.Name == typeName);

        var typeParameters = typeSymbol.DeclaredType switch
        {
            ClassType c => c.TypeParameters,
            StructType s => s.TypeParameters,
            InterfaceType i => i.TypeParameters,
            _ => throw new InvalidOperationException($"Type '{typeName}' has no type parameters container."),
        };
        Assert.NotEmpty(typeParameters);
        return (typeSymbol, (TypeParameterType)typeParameters[0]);
    }

    /// <summary>
    /// Finds a named member of the given generic type. Uses the declared
    /// member scope (the scope registered in Pass 1), which is exactly what
    /// the analyzer uses as the key into its classification tables.
    /// </summary>
    private static ISymbol GetMember(ISymbol typeSymbol, string memberName)
    {
        var scope = typeSymbol.DeclaredType switch
        {
            ClassType c => c.MemberScope,
            StructType s => s.MemberScope,
            InterfaceType i => i.MemberScope,
            _ => throw new InvalidOperationException(
                $"Type '{typeSymbol.Name}' does not have a member scope."),
        };
        return scope.Members.First(m => m.Name == memberName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 9.5 — Method body write-site rule.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>void Set(T x) { this.value = x; }</c> — the canonical Task 18.1
    /// write-site case. The assignment target <c>this.value</c> has
    /// expression type <c>T</c> (the T-typed field), so the walker records
    /// it as a write-site against T. The method therefore classifies
    /// <see cref="MemberClassification.WritesT"/> per Requirement 9.5.
    /// </summary>
    /// <remarks>
    /// The Task 18.5 bullet "Method that assigns to a <c>T[]</c> element →
    /// WritesT" is exercised by
    /// <see cref="MethodAssigningToTField_IsClassifiedWritesT"/> in this
    /// simpler form. A proper <c>T[]</c> fixture is blocked by a pre-existing
    /// compiler issue: <c>T[]</c> as a type reference (NamedTypeRef with
    /// ArrayRank) is resolved to the bare <c>T</c> TypeParameterType rather
    /// than <c>ArrayType(T)</c> when the name resolves to a type parameter
    /// (<see cref="SemanticModelBuilder"/> Pass 2 returns the TPT directly
    /// without honoring ArrayRank). This is not a Task 18 regression — the
    /// same bug affects the Task 17 field tests when expressed through
    /// source rather than in-memory AST. A T-typed field covers the same
    /// storage-position-containment rule and is unaffected.
    /// </remarks>
    [Fact]
    public void MethodAssigningToTField_IsClassifiedWritesT()
    {
        const string source =
            "class Cell<T> {\n" +
            "    public T value;\n" +
            "    public void Set(T x) { this.value = x; }\n" +
            "}";

        var (map, model, diagnostics) = Analyze(source);
        // Sanity: the source must lex/parse/semantically resolve cleanly,
        // otherwise the expressionTypes map will be missing entries the
        // analyzer depends on.
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Cell");
        var set = GetMember(typeSymbol, "Set");

        Assert.Equal(SymbolKind.Method, set.Kind);
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(set, T));
    }

    /// <summary>
    /// <c>int Count() { return 0; }</c> — a method whose body contains no
    /// assignment and no call with a T-containing argument. The intrinsic
    /// classification is <see cref="MemberClassification.DoesNotWriteT"/>
    /// (the lattice bottom). Task 19 will only ever promote from here;
    /// Task 18 must return the bottom for read-only methods.
    /// </summary>
    [Fact]
    public void ReadOnlyMethod_IsClassifiedDoesNotWriteT()
    {
        const string source =
            "class Box<T> {\n" +
            "    public int Count() { return 0; }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Box");
        var count = GetMember(typeSymbol, "Count");

        Assert.Equal(SymbolKind.Method, count.Kind);
        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(count, T));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 9.7 — Constructor classification.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>Cell(T v)</c> — constructor whose parameter type is T. Under the
    /// parameter-type containment rule (Req 9.7), the constructor is
    /// classified <see cref="MemberClassification.WritesT"/> regardless of
    /// whether the body writes T-typed storage. Covers the test case
    /// "Constructor taking a T parameter stored into a field → WritesT".
    /// </summary>
    [Fact]
    public void ConstructorWithTParameter_IsClassifiedWritesT()
    {
        const string source =
            "class Cell<T> {\n" +
            "    public T value;\n" +
            "    public Cell(T v) { this.value = v; }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Cell");
        var ctor = GetMember(typeSymbol, "Cell");

        Assert.Equal(SymbolKind.Constructor, ctor.Kind);
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(ctor, T));
    }

    /// <summary>
    /// <c>Box(int tag)</c> — constructor whose parameter type does not
    /// contain T, whose body writes only to a non-T field. Parameter rule
    /// does not fire, body writes do not hit T-typed storage, and no call
    /// site has a T-containing argument — classification is
    /// <see cref="MemberClassification.DoesNotWriteT"/>. Covers the test
    /// case "Constructor taking no T parameters and not writing →
    /// DoesNotWriteT".
    /// </summary>
    [Fact]
    public void ConstructorWithoutTParameter_AndNoTWrite_IsClassifiedDoesNotWriteT()
    {
        const string source =
            "class Box<T> {\n" +
            "    public int tag;\n" +
            "    public Box(int t) { this.tag = t; }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Box");
        var ctor = GetMember(typeSymbol, "Box");

        Assert.Equal(SymbolKind.Constructor, ctor.Kind);
        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(ctor, T));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Req 9.8 — Virtual / trait / dynamic-dispatch conservative rule.
    // Req 9.9 — External (non-generic-internal) callee conservative rule.
    //
    // Task 18 treats every call site that isn't a resolved same-generic
    // internal member as dynamic or external: in both cases the rule is
    // "WritesT iff any argument contains T at a storage position".
    //
    // Task 19 refines this rule for two common call shapes: `this.M(...)`
    // and unqualified `M(...)` now resolve to a sibling member when one
    // exists, so they are classified as <c>IsInternal=true</c> and
    // participate in fixed-point propagation instead of triggering the
    // conservative argument-based rule. The Req 9.8 / 9.9 coverage below
    // exercises the external receiver path (call through a non-same-type
    // receiver) that remains dynamic.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>void Forward(T x) { this.Sink(x); }</c> — after Task 19's
    /// walker improvements, <c>this.Sink</c> resolves to the sibling
    /// method <c>Sink</c>. <c>Sink</c>'s body is empty, so its intrinsic
    /// classification is <see cref="MemberClassification.DoesNotWriteT"/>,
    /// and fixed-point propagation leaves <c>Forward</c> at
    /// <see cref="MemberClassification.DoesNotWriteT"/> too. This is a
    /// strictly-better classification than Task 18's conservative rule
    /// (which marked every unresolved same-type call as external/dynamic
    /// and therefore WritesT whenever any argument contained T). The
    /// Req 9.8 external/dynamic rule is still exercised by
    /// <see cref="ExternalCalleeWithTContainingArgument_IsClassifiedWritesT"/>
    /// below.
    /// </summary>
    [Fact]
    public void InternalSameTypeDispatchToEmptyCallee_IsClassifiedDoesNotWriteT()
    {
        const string source =
            "class Holder<T> {\n" +
            "    public void Sink(T x) { }\n" +
            "    public void Forward(T x) { this.Sink(x); }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Holder");
        var forward = GetMember(typeSymbol, "Forward");

        Assert.Equal(SymbolKind.Method, forward.Kind);
        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(forward, T));
    }

    /// <summary>
    /// <c>void Forward(T x) { this.Sink(x); }</c> where <c>Sink</c>
    /// writes to a T-typed field. Task 19's fixed-point propagation
    /// promotes <c>Forward</c> to
    /// <see cref="MemberClassification.WritesT"/> via the internal
    /// call edge. This is the "WritesT propagates through a resolved
    /// internal callee" scenario and the direct counterpart to the
    /// mutual-recursion test in
    /// <see cref="ReachabilityAnalyzerFixedPointTests"/>.
    /// </summary>
    [Fact]
    public void InternalSameTypeDispatchToWritingCallee_PropagatesWritesT()
    {
        const string source =
            "class Holder<T> {\n" +
            "    public T slot;\n" +
            "    public void Sink(T x) { this.slot = x; }\n" +
            "    public void Forward(T x) { this.Sink(x); }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Holder");
        var forward = GetMember(typeSymbol, "Forward");

        Assert.Equal(SymbolKind.Method, forward.Kind);
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(forward, T));
    }

    /// <summary>
    /// <c>void Print(int n) { this.Emit(n); }</c> invoking a sibling
    /// method whose arguments' types do not contain T. After Task 19's
    /// walker improvements, <c>this.Emit</c> resolves as an internal
    /// callee and the fixed-point rule applies: <c>Emit</c>'s empty body
    /// is <see cref="MemberClassification.DoesNotWriteT"/>, so
    /// <c>Print</c> stays at the lattice bottom. Before Task 19 this was
    /// a conservative external-dispatch case that nonetheless concluded
    /// <see cref="MemberClassification.DoesNotWriteT"/> because
    /// <c>int</c> does not mention T; the expected value is the same.
    /// </summary>
    [Fact]
    public void DispatchWithoutTArgument_IsClassifiedDoesNotWriteT()
    {
        const string source =
            "class Holder<T> {\n" +
            "    public void Emit(int n) { }\n" +
            "    public void Print(int n) { this.Emit(n); }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Holder");
        var print = GetMember(typeSymbol, "Print");

        Assert.Equal(SymbolKind.Method, print.Kind);
        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(print, T));
    }

    /// <summary>
    /// Mist analogue of <c>Console.WriteLine(T)</c> — a call to an external
    /// callee with a T-typed argument. Under Req 9.9, external callees are
    /// classified as writing T iff any argument contains T at a storage
    /// position. Because Task 18's walker uniformly marks call sites as
    /// external/dynamic, the analysis agrees: a T-containing argument fires
    /// <see cref="MemberClassification.WritesT"/>.
    /// </summary>
    [Fact]
    public void ExternalCalleeWithTContainingArgument_IsClassifiedWritesT()
    {
        // `Logger.Log(x)` stands in for a Console.WriteLine-style external
        // callee. The analyzer does not resolve `Logger.Log` (it lives
        // outside the `Repo<T>` member scope), so the conservative rule
        // fires when the argument type contains T.
        const string source =
            "class Logger { public static void Log(int n) { } }\n" +
            "class Repo<T> {\n" +
            "    public void Store(T item) { Logger.Log(item); }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Repo");
        var store = GetMember(typeSymbol, "Store");

        Assert.Equal(SymbolKind.Method, store.Kind);
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(store, T));
    }

    /// <summary>
    /// External callee with a non-T-containing argument — int flows to
    /// <c>Logger.Log</c>, which does not contain T at any storage position.
    /// Task 18 concludes <see cref="MemberClassification.DoesNotWriteT"/>.
    /// Negative-space confirmation of Req 9.9, and the second half of the
    /// "External Console.WriteLine(T) invocation" test case.
    /// </summary>
    [Fact]
    public void ExternalCalleeWithoutTContainingArgument_IsClassifiedDoesNotWriteT()
    {
        const string source =
            "class Logger { public static void Log(int n) { } }\n" +
            "class Repo<T> {\n" +
            "    public void Announce(int n) { Logger.Log(n); }\n" +
            "}";

        var (map, model, _) = Analyze(source);
        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "Repo");
        var announce = GetMember(typeSymbol, "Announce");

        Assert.Equal(SymbolKind.Method, announce.Kind);
        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(announce, T));
    }
}
