// Feature: unknown-type (Phase C, Task 20.3)
// **Validates: Requirement 9.1**
//
// Integration tests for Task 20: wiring ReachabilityAnalyzer.Analyze into
// SemanticModelBuilder.Build and exposing its output through the public
// ISemanticModel.ReachabilityMap property.
//
// Task 20 is plumbing — the real classification algorithm is tested by
// the Task 17, 18, and 19 unit/property tests. This fixture's job is to
// confirm that:
//
//   1. SemanticModelBuilder.Build produces a non-null ReachabilityMap on
//      the returned ISemanticModel (no direct call to ReachabilityAnalyzer
//      needed by callers).
//   2. The classifications on that map match what Tasks 17–19 produce —
//      i.e., the wiring runs the analyzer on the same symbol table and
//      expression-type map, with no loss of fidelity.
//   3. The map is reachable through the *public* ISemanticModel interface
//      (which is the surface analyzers receive), confirming Task 21 will
//      be able to consult it from TypeCheckerAnalyzer.
//
// The test uses the same in-process compile pattern as the Task 18 tests —
// no external process is spawned.

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
/// Integration tests that verify <see cref="SemanticModelBuilder.Build"/>
/// invokes <see cref="ReachabilityAnalyzer.Analyze"/> as a post-Pass-2
/// step and exposes the resulting <see cref="IReachabilityMap"/> through
/// the public <see cref="ISemanticModel.ReachabilityMap"/> property.
/// </summary>
public class ReachabilityMapIntegrationTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Compiles <paramref name="source"/> through lex → parse →
    /// <see cref="SemanticModelBuilder.Build"/> without running any
    /// downstream analyzers. Asserts that lex and parse produce no errors.
    /// Returns the semantic model and the semantic diagnostics so the
    /// test can sanity-check them before inspecting the reachability map.
    /// </summary>
    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) Compile(string source)
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

        return (model, semBag.ToImmutableArray());
    }

    /// <summary>
    /// Looks up a generic type by name and returns the symbol and its
    /// sole type parameter. Every fixture here declares a single type
    /// parameter named <c>T</c>.
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
        return (typeSymbol, typeParameters[0]);
    }

    /// <summary>
    /// Looks up a named member in the generic type's member scope — the
    /// same key the analyzer uses.
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
    // Task 20.1 + 20.2: the ReachabilityMap is produced by Build and is
    // accessible through the ISemanticModel interface.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Compiling any program (even a trivial non-generic one) must leave
    /// the resulting semantic model with a non-null
    /// <see cref="ISemanticModel.ReachabilityMap"/>. For programs with no
    /// generic types the map is empty, not null, because the analyzer
    /// iterates over an empty set of generics.
    /// </summary>
    [Fact]
    public void Build_ProducesNonNullReachabilityMap_EvenForNonGenericSource()
    {
        const string source =
            "class Foo {\n" +
            "    public int tag;\n" +
            "}";

        var (model, diagnostics) = Compile(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        // ReachabilityMap is exposed on the ISemanticModel interface. This
        // is the exact path TypeCheckerAnalyzer (Task 21) will use.
        Assert.NotNull(model.ReachabilityMap);
    }

    /// <summary>
    /// Spec's Task 20.3 fixture: a small generic type <c>List&lt;T&gt;</c>
    /// with an <c>Add</c> method, a read-only <c>Count</c> auto-property,
    /// and a T-typed <c>item</c> field. After Build, the ReachabilityMap
    /// must classify:
    /// <list type="bullet">
    /// <item><description><c>Add</c> = <see cref="MemberClassification.WritesT"/></description></item>
    /// <item><description><c>Count.Read</c> = <see cref="MemberClassification.DoesNotWriteT"/></description></item>
    /// <item><description><c>item</c> (field write) = <see cref="MemberClassification.WritesT"/></description></item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The spec bullet mentions <c>T[] items</c> as the field, but the
    /// pre-existing <c>T[]</c> resolution issue flagged in the Task 18
    /// tests (<c>NamedTypeRef</c> with <c>ArrayRank</c> resolving to a
    /// <see cref="TypeParameterType"/> drops the array rank) makes the
    /// array form unreliable from source. A bare T-typed field exercises
    /// the same storage-position containment rule (Req 9.2) and is used
    /// here, matching the approach taken in
    /// <see cref="ReachabilityAnalyzerMethodConstructorTests"/>.
    /// </remarks>
    [Fact]
    public void Build_ProducesReachabilityMap_ListExample()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public int Count { get; }\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}";

        var (model, diagnostics) = Compile(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var map = model.ReachabilityMap;
        Assert.NotNull(map);

        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "List");

        // Add stores its argument into a T-typed field — WritesT under
        // Requirements 9.5 + 9.2 (the assignment target's declared type
        // is T, which satisfies ContainsTAtStoragePosition against T).
        var add = GetMember(typeSymbol, "Add");
        Assert.Equal(SymbolKind.Method, add.Kind);
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(add, T));

        // Count is an auto-property with only a get accessor — no
        // backing-field write, and reading never writes. Both sides are
        // DoesNotWriteT (Requirement 9.3 + 9.11). The spec calls out
        // "Count read = DoesNotWriteT" explicitly.
        var count = GetMember(typeSymbol, "Count");
        Assert.Equal(SymbolKind.Property, count.Kind);
        var countClass = map.GetPropertyClassification(count, T);
        Assert.Equal(MemberClassification.DoesNotWriteT, countClass.Read);
        Assert.Equal(MemberClassification.DoesNotWriteT, countClass.Write);

        // The T-typed field itself: field-write classification is WritesT
        // (Requirement 9.2). Field *reads* are always DoesNotWriteT and
        // are not exposed on the map's interface because reading can
        // never materialize a write.
        var item = GetMember(typeSymbol, "item");
        Assert.Equal(SymbolKind.Field, item.Kind);
        Assert.Equal(MemberClassification.WritesT, map.GetFieldClassification(item, T));
    }

    /// <summary>
    /// Confirms that the map produced by <see cref="SemanticModelBuilder.Build"/>
    /// is exactly what a direct invocation of
    /// <see cref="ReachabilityAnalyzer.Analyze"/> on the same symbol table
    /// and expression-type map would produce. This is the parity check
    /// that protects against a wiring regression silently running the
    /// analyzer on a different (or stale) state than the caller sees.
    /// </summary>
    [Fact]
    public void Build_ReachabilityMap_MatchesDirectAnalyzerInvocation()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "    public void Noop() { }\n" +
            "}";

        var (model, diagnostics) = Compile(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var pipelineMap = model.ReachabilityMap;

        // Independently run the analyzer on the same inputs. The pipeline
        // map must agree with this reference result on every member.
        var concreteModel = (SemanticModel)model;
        var referenceMap = ReachabilityAnalyzer.Analyze(
            concreteModel.SymbolTable,
            concreteModel.ExpressionTypes);

        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "List");

        var add = GetMember(typeSymbol, "Add");
        Assert.Equal(
            referenceMap.GetMethodClassification(add, T),
            pipelineMap.GetMethodClassification(add, T));

        var noop = GetMember(typeSymbol, "Noop");
        Assert.Equal(
            referenceMap.GetMethodClassification(noop, T),
            pipelineMap.GetMethodClassification(noop, T));

        var item = GetMember(typeSymbol, "item");
        Assert.Equal(
            referenceMap.GetFieldClassification(item, T),
            pipelineMap.GetFieldClassification(item, T));
    }
}
