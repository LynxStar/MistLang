// Feature: unknown-readonly-semantics (Task 6)
// **Validates: Requirement 11.3** (backward compat for accepted programs)
//
// Unit tests verifying that `SubstituteClosedUnknownInResultType` correctly
// substitutes `unknown` for type parameters in the result type of member
// access on `Closed_Generic_With_Unknown` receivers.
//
// This logic is part of type resolution (not readonly enforcement) and must
// be preserved during the reachability cleanup (Tasks 7–8).
//
// These tests run fully in-process via the shared
// `SemanticModelBuilder` + `TypeCheckerAnalyzer` — no `dotnet` process
// is spawned, per the workspace test-process-spawning policy.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests for the <c>SubstituteClosedUnknownInResultType</c> logic in
/// <see cref="TypeCheckerAnalyzer"/>. When a field of type <c>T</c> is read
/// on a <c>List&lt;unknown&gt;</c> receiver, the result type must be
/// <c>unknown</c> (not the raw type parameter <c>T</c>).
/// </summary>
public class SubstituteClosedUnknownResultTypeTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    /// <summary>
    /// Lexes, parses, builds the semantic model, then runs the type
    /// checker against <paramref name="source"/>. Returns the semantic
    /// model and the AST root for further inspection.
    /// </summary>
    private (ISemanticModel Model, SyntaxNode Ast, ImmutableArray<Diagnostic> Diagnostics) CompileAndAnalyze(string source)
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

        var allDiagnostics = semBag.ToImmutableArray().Concat(tcBag.ToImmutableArray()).ToImmutableArray();
        return (model, parseResult.Root!, allDiagnostics);
    }

    /// <summary>
    /// Recursively walks the AST and returns all <see cref="MemberAccessSuffixPostfixSuffixNode"/>
    /// nodes whose <c>Name</c> lexeme matches <paramref name="memberName"/>.
    /// </summary>
    private static IEnumerable<MemberAccessSuffixPostfixSuffixNode> FindMemberAccessSuffixes(
        SyntaxNode root, string memberName)
    {
        var stack = new Stack<SyntaxNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MemberAccessSuffixPostfixSuffixNode suffix
                && suffix.Name.Lexeme == memberName)
            {
                yield return suffix;
            }
            foreach (var child in node.Children())
            {
                if (child is not null)
                    stack.Push(child);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 6 — reading a T-typed field on List<unknown> resolves to unknown
    //
    // SubstituteClosedUnknownInResultType substitutes `unknown` for T in
    // the result type when the receiver is a Closed_Generic_With_Unknown.
    // This ensures that `xs.item` where `xs: List<unknown>` and
    // `item: T` yields type `unknown`, not the raw type parameter.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reading a T-typed field on a <c>List&lt;unknown&gt;</c> receiver
    /// must resolve to type <c>unknown</c>. This validates that
    /// <c>SubstituteClosedUnknownInResultType</c> correctly maps T → unknown
    /// in the result type of the member access.
    /// </summary>
    [Fact]
    public void FieldRead_OnListUnknown_ResolvesToUnknownType()
    {
        // A generic class with a T-typed field. The consumer reads the
        // field through a List<unknown> receiver and returns it as `unknown`.
        // If substitution works correctly, no type mismatch is emitted.
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public unknown Read(List<unknown> xs) { return xs.item; }\n" +
            "}";

        var (model, ast, diagnostics) = CompileAndAnalyze(source);

        // Find the `item` member access suffix in the AST
        var suffixes = FindMemberAccessSuffixes(ast, "item")
            .Where(s =>
            {
                // We want the suffix that is NOT inside the List<T> body
                // (i.e., not `this.item = x`). The one in Foo.Read is the
                // one accessed on a List<unknown> receiver.
                var resolvedType = model.GetTypeOf(s);
                return resolvedType is not null;
            })
            .ToList();

        // There should be at least one `item` access (the one in Foo.Read)
        Assert.NotEmpty(suffixes);

        // Find the suffix in Foo.Read (the one on the List<unknown> receiver).
        // After substitution, its resolved type should be UnknownType.
        var fooReadSuffix = suffixes.FirstOrDefault(s => model.GetTypeOf(s) is UnknownType);
        Assert.NotNull(fooReadSuffix);
        Assert.IsType<UnknownType>(model.GetTypeOf(fooReadSuffix));
    }

    /// <summary>
    /// Reading a T-typed field on a <c>List&lt;unknown&gt;</c> receiver
    /// and assigning it to an <c>unknown</c>-typed variable must produce
    /// no type mismatch diagnostic. This confirms the substitution makes
    /// the assignment well-typed.
    /// </summary>
    [Fact]
    public void FieldRead_OnListUnknown_AssignableToUnknownVariable()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(List<unknown> xs) {\n" +
            "        unknown val = xs.item;\n" +
            "    }\n" +
            "}";

        var (_, _, diagnostics) = CompileAndAnalyze(source);

        // No type mismatch (MIST_TC_0001) — the field read yields `unknown`
        // after substitution, which is assignable to an `unknown` variable.
        var typeMismatch = diagnostics.Where(d => d.Id.Value == "MIST_TC_0001").ToList();
        Assert.Empty(typeMismatch);

        // No filter diagnostic either — field reads are always permitted.
        var filterDiag = diagnostics.Where(d => d.Id.Value == "MIST_TC_0062").ToList();
        Assert.Empty(filterDiag);
    }

    /// <summary>
    /// Reading a T-typed field on a <c>List&lt;unknown&gt;</c> receiver
    /// and assigning it to an <c>int</c>-typed variable must produce a
    /// type mismatch diagnostic. This confirms the substitution correctly
    /// reports the result as <c>unknown</c> (not <c>T</c> or <c>int</c>).
    /// </summary>
    [Fact]
    public void FieldRead_OnListUnknown_NotAssignableToConcreteType()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(List<unknown> xs) {\n" +
            "        int val = xs.item;\n" +
            "    }\n" +
            "}";

        var (_, _, diagnostics) = CompileAndAnalyze(source);

        // A type mismatch should be emitted: `unknown` is not assignable to `int`.
        // The diagnostic may be MIST_TC_0001 (type mismatch) or MIST_TC_0060
        // (unknown-to-non-unknown assignment). Either confirms the substitution
        // correctly reports the result as `unknown`.
        var relevantDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0001" || d.Id.Value == "MIST_TC_0060")
            .ToList();
        Assert.NotEmpty(relevantDiags);
    }

    /// <summary>
    /// Contrast: reading a T-typed field on a concrete <c>List&lt;int&gt;</c>
    /// receiver must resolve to <c>int</c> (not <c>unknown</c>). This
    /// confirms the substitution only fires for unknown-closed generics.
    /// </summary>
    [Fact]
    public void FieldRead_OnListInt_ResolvesToConcreteType()
    {
        const string source =
            "class List<T> {\n" +
            "    public T item;\n" +
            "    public void Add(T x) { this.item = x; }\n" +
            "}\n" +
            "class Foo {\n" +
            "    public void M(List<int> xs) {\n" +
            "        int val = xs.item;\n" +
            "    }\n" +
            "}";

        var (model, ast, diagnostics) = CompileAndAnalyze(source);

        // No type mismatch — the field read yields `int` after substitution,
        // which is assignable to an `int` variable.
        var typeMismatch = diagnostics.Where(d => d.Id.Value == "MIST_TC_0001").ToList();
        Assert.Empty(typeMismatch);

        // Find the `item` suffix and verify its type is not UnknownType
        var suffixes = FindMemberAccessSuffixes(ast, "item")
            .Where(s => model.GetTypeOf(s) is not null)
            .ToList();

        Assert.NotEmpty(suffixes);
        // None of the `item` accesses should resolve to UnknownType
        Assert.DoesNotContain(suffixes, s => model.GetTypeOf(s) is UnknownType);
    }
}
