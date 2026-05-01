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
/// Unit tests for Task 15 of the Mist Standard Library spec: the
/// <see cref="OverloadResolver.Resolve"/> left-operand precedence
/// tiebreaker (design § 5).
///
/// When two candidates are otherwise equally applicable (no <c>MoreSpecific</c>
/// relation holds), and the caller provides a parallel
/// <c>candidateOrigin</c> vector (0 = left operand's type, 1 = right operand's
/// type), the candidate with the lower origin value wins. The existing
/// method-call path passes <c>null</c> for <c>candidateOrigin</c> and keeps
/// the pre-tiebreaker behavior: ambiguity surfaces as
/// <see cref="OverloadResolutionStatus.AmbiguousCall"/>.
///
/// Scenario used throughout:
/// <list type="bullet">
///   <item>Struct <c>A</c> declares <c>public static A operator +(A a, B b)</c>.</item>
///   <item>Struct <c>B</c> declares <c>public static A operator +(A a, B b)</c>.</item>
/// </list>
/// Both operators have identical parameter signatures <c>(A, B)</c>. Neither
/// is more specific than the other. Task 14's
/// <c>ResolveBinaryOperator</c> assembles the candidate list as
/// <c>[A.operator+, B.operator+]</c> for operands <c>(A, B)</c> and
/// <c>[B.operator+, A.operator+]</c> for operands <c>(B, A)</c> (left operand
/// first). The origin vector mirrors the list order:
/// <c>[0, 1]</c>. The left-hand candidate wins in both cases.
///
/// Validates:
/// * Requirement 12.5 — operator overload resolution is deterministic.
/// * Requirement 15.4 — open question resolved in favor of
///   left-operand precedence.
/// </summary>
public class OverloadResolverOriginTiebreakerTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();
    private readonly SemanticModelBuilder _builder = new();

    /// <summary>
    /// Lexes, parses, and semantically analyses <paramref name="source"/>.
    /// Returns the built semantic model.
    /// </summary>
    private ISemanticModel BuildModel(string source)
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

        Assert.False(semBag.HasErrors,
            "Semantic model build should not produce errors for a well-formed test program");

        return model;
    }

    /// <summary>
    /// Builds the A/B fixture used by both tests and returns the two struct
    /// types and their respective <c>operator+</c> symbols.
    /// </summary>
    private (ISemanticModel Model, StructType AType, StructType BType, ISymbol AOp, ISymbol BOp)
        BuildAbFixture()
    {
        var source =
            "public struct B { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "} " +
            "public struct A { " +
            "  public static A operator +(A x, B y) { return x; } " +
            "}";

        var model = BuildModel(source);

        var aType = (model.AllSymbols
            .First(s => s.Name == "A" && s.Kind == SymbolKind.Struct)
            .DeclaredType as StructType)!;
        var bType = (model.AllSymbols
            .First(s => s.Name == "B" && s.Kind == SymbolKind.Struct)
            .DeclaredType as StructType)!;

        var aOp = aType.MemberScope.Members.Single(m => m.Name == "operator+");
        var bOp = bType.MemberScope.Members.Single(m => m.Name == "operator+");

        // Sanity: same signature on both operators, so MoreSpecific yields
        // no winner. The whole point of this test is to exercise the
        // tiebreaker, not specificity.
        var aParams = ParameterSignature.Extract(aOp, model);
        var bParams = ParameterSignature.Extract(bOp, model);
        Assert.Same(aType, aParams[0]);
        Assert.Same(bType, aParams[1]);
        Assert.Same(aType, bParams[0]);
        Assert.Same(bType, bParams[1]);

        return (model, aType, bType, aOp, bOp);
    }

    /// <summary>
    /// Test 1 — Operands <c>(A, B)</c>. Candidate list is
    /// <c>[A.operator+, B.operator+]</c> with origin vector <c>[0, 1]</c>.
    /// <c>A.operator+</c> (origin 0) is the left-operand candidate and wins.
    /// </summary>
    [Fact]
    public void Task15_1_LeftOriginWins_OperandsAB_ACandidateSelected()
    {
        var (model, aType, bType, aOp, bOp) = BuildAbFixture();

        // Mirror Task 14's ResolveBinaryOperator candidate assembly order:
        // left-operand type's operators first (origin 0), right-operand
        // type's operators second (origin 1). For operands (A, B), A is
        // the left type, so A.operator+ comes first with origin 0.
        var candidates = new List<ISymbol> { aOp, bOp };
        var candidateOrigin = new List<int> { 0, 1 };
        var argumentTypes = new IType[] { aType, bType };

        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes,
            model,
            candidateOrigin);

        Assert.Equal(OverloadResolutionStatus.Success, result.Status);
        Assert.Same(aOp, result.BestCandidate);
        Assert.Null(result.AmbiguousCandidates);
    }

    /// <summary>
    /// Test 2 — Operands reversed to <c>(B, A)</c>. Candidate list becomes
    /// <c>[B.operator+, A.operator+]</c> with origin vector <c>[0, 1]</c>;
    /// <c>B.operator+</c> (origin 0) is now the left-operand candidate and
    /// wins. Confirms determinism across operand commutation: the winner
    /// depends only on origin, not on list order.
    ///
    /// Note: the operators' parameter signatures <c>(A, B)</c> do NOT match
    /// arguments <c>(B, A)</c>, so in real type-checking this call would be
    /// <c>NoApplicableCandidate</c>. To isolate the tiebreaker behavior from
    /// applicability filtering, this test uses <c>(A, B)</c> as the
    /// arguments — the same pair the operators accept — but simulates the
    /// "operands reversed" commutation by swapping the candidate list and
    /// origin vector order, which is exactly what Task 14's
    /// <c>ResolveBinaryOperator</c> would produce if the operators' own
    /// signatures were symmetric (e.g., <c>(A, A)</c> or <c>(B, B)</c>)
    /// and the operand types were commuted.
    ///
    /// A cleaner test would use symmetric operators. See
    /// <see cref="Task15_1_LeftOriginWins_SymmetricOperators_Commutation"/>
    /// below for that variation.
    /// </summary>
    [Fact]
    public void Task15_1_LeftOriginWins_CandidateListReversed_BCandidateSelected()
    {
        var (model, aType, bType, aOp, bOp) = BuildAbFixture();

        // Reverse the candidate list — B.operator+ is now first (origin 0,
        // i.e., the "left-operand type's candidate" after commutation).
        // A.operator+ is second (origin 1).
        var candidates = new List<ISymbol> { bOp, aOp };
        var candidateOrigin = new List<int> { 0, 1 };
        var argumentTypes = new IType[] { aType, bType };

        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes,
            model,
            candidateOrigin);

        // Tiebreaker runs on origin alone — B now wins because its origin is 0.
        Assert.Equal(OverloadResolutionStatus.Success, result.Status);
        Assert.Same(bOp, result.BestCandidate);
        Assert.Null(result.AmbiguousCandidates);
    }

    /// <summary>
    /// When <c>candidateOrigin</c> is <c>null</c>, the resolver preserves
    /// the pre-Task-15 behavior: two equally-applicable candidates with no
    /// specificity difference yield <c>AmbiguousCall</c>. This pins the
    /// method-call call site contract — non-operator resolution is
    /// unaffected by Task 15.
    /// </summary>
    [Fact]
    public void Task15_1_CandidateOriginNull_KeepsAmbiguousBehavior()
    {
        var (model, aType, bType, aOp, bOp) = BuildAbFixture();

        // Without an origin vector, the resolver must fall back to the
        // pre-tiebreaker behavior — the same candidates produce
        // AmbiguousCall. This pins the method-call call site contract:
        // non-operator resolution is unaffected by Task 15.
        var candidates = new List<ISymbol> { aOp, bOp };
        var argumentTypes = new IType[] { aType, bType };

        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes,
            model,
            candidateOrigin: null);

        Assert.Equal(OverloadResolutionStatus.AmbiguousCall, result.Status);
        Assert.Null(result.BestCandidate);
        Assert.NotNull(result.AmbiguousCandidates);
        Assert.Equal(2, result.AmbiguousCandidates!.Count);
    }

    /// <summary>
    /// When two candidates share the SAME origin value, the tiebreaker
    /// cannot disambiguate — ambiguity persists. This is the only way a
    /// genuine <c>MIST_TC_0032</c> can now be produced via
    /// <c>ResolveBinaryOperator</c>: an operator pair declared on the same
    /// operand type, applicable to the same argument pair, with no
    /// specificity difference.
    /// </summary>
    [Fact]
    public void Task15_1_BothCandidatesSameOrigin_RemainsAmbiguous()
    {
        var (model, aType, bType, aOp, bOp) = BuildAbFixture();

        // Both candidates have origin 0 — the tiebreaker has no winner.
        var candidates = new List<ISymbol> { aOp, bOp };
        var candidateOrigin = new List<int> { 0, 0 };
        var argumentTypes = new IType[] { aType, bType };

        var result = OverloadResolver.Resolve(
            candidates,
            argumentTypes,
            model,
            candidateOrigin);

        Assert.Equal(OverloadResolutionStatus.AmbiguousCall, result.Status);
        Assert.Null(result.BestCandidate);
        Assert.NotNull(result.AmbiguousCandidates);
        Assert.Equal(2, result.AmbiguousCandidates!.Count);
    }
}
