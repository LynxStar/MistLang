// Feature: unknown-type (Phase C, Task 19.3)
// **Validates: Requirements 9.6, 9.10**
//
// Unit test for ReachabilityAnalyzer's Task 19 fixed-point propagation:
// mutually recursive methods where one of the members writes to T-typed
// storage must promote every member in the cycle to `WritesT`.
//
// These tests compile a small Mist source fragment in-process through
// SemanticModelBuilder.Build (no dotnet process is spawned), extract the
// symbol table and expression-type map, run ReachabilityAnalyzer.Analyze
// (which now includes the fixed-point step), and assert the final
// classifications.

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
/// Tests for <see cref="ReachabilityAnalyzer.Analyze"/>'s Task 19
/// fixed-point propagation. The fixture compiles a small Mist fragment,
/// invokes the analyzer, and asserts the classifications of members that
/// are only <see cref="MemberClassification.WritesT"/> after propagation
/// (the Task-18 intrinsic pass would leave them at
/// <see cref="MemberClassification.DoesNotWriteT"/>).
/// </summary>
public class ReachabilityAnalyzerFixedPointTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Compile → build semantic model → run analyzer. Returns the map,
    /// the semantic model, and the full diagnostic list so callers can
    /// check for error-free input.
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

        var semanticModel = (SemanticModel)model;
        var map = ReachabilityAnalyzer.Analyze(semanticModel.SymbolTable, semanticModel.ExpressionTypes);

        return (map, model, semBag.ToImmutableArray());
    }

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
    // Req 9.6 + 9.10 — mutual recursion through the fixed-point iteration.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>class G&lt;T&gt; { T value; void A() { this.B(); this.value = this.value; }  void B() { this.A(); } }</c>.
    /// Task 18 classifies <c>A</c> as <see cref="MemberClassification.WritesT"/>
    /// because of its direct write to <c>this.value</c> (T-typed), and
    /// <c>B</c> as <see cref="MemberClassification.DoesNotWriteT"/> (no
    /// direct write and its only internal callee is still being analyzed).
    /// After Task 19's fixed-point iteration, <c>B</c> observes that its
    /// internal callee <c>A</c> is <see cref="MemberClassification.WritesT"/>
    /// and is promoted accordingly, so both end the iteration at the top
    /// of the lattice.
    /// </summary>
    [Fact]
    public void MutualRecursion_PromotesBothMethodsToWritesT()
    {
        const string source =
            "class G<T> {\n" +
            "    public T value;\n" +
            "    public void A() { this.B(); this.value = this.value; }\n" +
            "    public void B() { this.A(); }\n" +
            "}";

        var (map, model, diagnostics) = Analyze(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "G");
        var a = GetMember(typeSymbol, "A");
        var b = GetMember(typeSymbol, "B");

        Assert.Equal(SymbolKind.Method, a.Kind);
        Assert.Equal(SymbolKind.Method, b.Kind);

        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(a, T));
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(b, T));
    }

    /// <summary>
    /// A two-hop propagation chain: <c>C → B → A</c>, where <c>A</c>
    /// directly writes <c>T</c>. The fixed-point loop must take at least
    /// two full passes to promote <c>C</c> (pass 1 promotes <c>B</c> via
    /// <c>A</c>; pass 2 promotes <c>C</c> via the newly-promoted <c>B</c>).
    /// Validates that the iteration does not stop early.
    /// </summary>
    [Fact]
    public void TransitiveCallChain_PropagatesWritesTAllTheWayUp()
    {
        const string source =
            "class G<T> {\n" +
            "    public T value;\n" +
            "    public void A() { this.value = this.value; }\n" +
            "    public void B() { this.A(); }\n" +
            "    public void C() { this.B(); }\n" +
            "}";

        var (map, model, diagnostics) = Analyze(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "G");
        var a = GetMember(typeSymbol, "A");
        var b = GetMember(typeSymbol, "B");
        var c = GetMember(typeSymbol, "C");

        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(a, T));
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(b, T));
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(c, T));
    }

    /// <summary>
    /// Negative-space confirmation: a chain of internal calls whose
    /// leaf does not write T must remain <see cref="MemberClassification.DoesNotWriteT"/>
    /// after fixed-point iteration. Propagation must not invent WritesT
    /// out of nothing.
    /// </summary>
    [Fact]
    public void ChainWithNoTWrite_RemainsDoesNotWriteT()
    {
        const string source =
            "class G<T> {\n" +
            "    public int tag;\n" +
            "    public void A() { this.tag = 1; }\n" +
            "    public void B() { this.A(); }\n" +
            "    public void C() { this.B(); }\n" +
            "}";

        var (map, model, diagnostics) = Analyze(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "G");
        var a = GetMember(typeSymbol, "A");
        var b = GetMember(typeSymbol, "B");
        var c = GetMember(typeSymbol, "C");

        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(a, T));
        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(b, T));
        Assert.Equal(MemberClassification.DoesNotWriteT, map.GetMethodClassification(c, T));
    }

    /// <summary>
    /// Unqualified call form (no <c>this.</c>): <c>class G&lt;T&gt; { T value; void A() { B(); value = value; }  void B() { A(); } }</c>.
    /// The walker's Task 19 identifier-resolution branch must recognize
    /// <c>B()</c> (bare invocation on an identifier target) as calling
    /// the sibling method, otherwise mutual recursion would still break
    /// for this common Mist idiom.
    /// </summary>
    [Fact]
    public void MutualRecursion_WithoutThisPrefix_PromotesBoth()
    {
        // Use explicit this.value on the LHS to work around any
        // unqualified-identifier field resolution variation; the call
        // sites are what matters for the fixed-point test.
        const string source =
            "class G<T> {\n" +
            "    public T value;\n" +
            "    public void A() { B(); this.value = this.value; }\n" +
            "    public void B() { A(); }\n" +
            "}";

        var (map, model, diagnostics) = Analyze(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var (typeSymbol, T) = GetGenericTypeAndTypeParam(model, "G");
        var a = GetMember(typeSymbol, "A");
        var b = GetMember(typeSymbol, "B");

        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(a, T));
        Assert.Equal(MemberClassification.WritesT, map.GetMethodClassification(b, T));
    }
}
