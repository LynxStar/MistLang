// Feature: first-class-functions, Property 10: Lambda is reachable from every value position
// **Validates: Requirements 7.1, 7.2, 7.3, 12.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for <b>Property 10: Lambda Is Reachable From Every Value Position</b>.
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 12.3**
///
/// For any generated lambda <c>L</c>, embedding <c>L</c> as
/// <list type="bullet">
///   <item>an assignment RHS (<c>f = L</c>),</item>
///   <item>a call argument (<c>g(L)</c>),</item>
///   <item>a return value (<c>return L;</c>), and</item>
///   <item>a nested lambda body (<c>q =&gt; L</c>)</item>
/// </list>
/// each parses to a lambda node in the corresponding AST slot. Relocating lambda to
/// the <c>Expression</c> level (and widening the assignment RHS to <c>Expression</c>)
/// preserves reachability in all value positions.
///
/// These are parse-only / in-process tests — no <c>dotnet</c> process is spawned.
/// </summary>
public class LambdaReachabilityPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────────

    public static Arbitrary<LambdaReachabilityScenario> ArbitraryLambdaReachabilityScenario() =>
        LambdaReachabilityGenerator.GenLambda().ToArbitrary();

    // ─── Parse + navigation helpers ───────────────────────────────────────────

    /// <summary>
    /// Parses a complete Mist program, asserting that no parse errors occurred, and
    /// returns the <see cref="CompilationUnitNode"/> root.
    /// </summary>
    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    /// <summary>
    /// Recursively walks the syntax tree (via <see cref="SyntaxNode.Children"/>)
    /// and returns the first node of type <typeparamref name="T"/>, or null.
    /// </summary>
    private static T? FindFirst<T>(SyntaxNode root) where T : SyntaxNode
    {
        if (root is T match)
            return match;

        foreach (var child in root.Children())
        {
            if (child is null)
                continue;

            var found = FindFirst<T>(child);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Wraps a body of statements in a complete, parseable Mist program. The method
    /// return type is left as <c>func&lt;int, int&gt;</c> so that <c>return L;</c> is a
    /// syntactically natural value position (the parser does not type-check the body).
    /// </summary>
    private static string WrapMethodBody(string body) =>
        "namespace TestNs;\n\npublic class C\n{\n    public func<int, int> M()\n    {\n        "
        + body + "\n    }\n}";

    // ═══════════════════════════════════════════════════════════════════════════
    // Property 10: Lambda reachable from every value position.
    //
    // For each generated lambda L, four programs are parsed — one per value
    // position — and each is checked for a lambda node in the corresponding slot.
    // ═══════════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaReachabilityPropertyTests) })]
    public Property Lambda_IsReachable_FromEveryValuePosition(LambdaReachabilityScenario scenario)
    {
        var l = scenario.LambdaText;

        // ── Slot 1: assignment RHS — f = L; ──────────────────────────────────
        var assignFailure = CheckAssignmentRhs(l);
        if (assignFailure is not null)
            return false.ToProperty().Label(assignFailure);

        // ── Slot 2: call argument — g(L); ────────────────────────────────────
        var argFailure = CheckCallArgument(l);
        if (argFailure is not null)
            return false.ToProperty().Label(argFailure);

        // ── Slot 3: return value — return L; ─────────────────────────────────
        var returnFailure = CheckReturnValue(l);
        if (returnFailure is not null)
            return false.ToProperty().Label(returnFailure);

        // ── Slot 4: nested lambda body — q => L ──────────────────────────────
        var nestedFailure = CheckNestedLambdaBody(l);
        if (nestedFailure is not null)
            return false.ToProperty().Label(nestedFailure);

        return true.ToProperty();
    }

    /// <summary>
    /// Verifies that in <c>f = L;</c> the assignment's right-hand side is a lambda.
    /// Returns a failure description, or null on success.
    /// </summary>
    private static string? CheckAssignmentRhs(string lambdaText)
    {
        var source = WrapMethodBody($"f = {lambdaText};");
        var cu = ParseOrFail(source);

        var stmt = FindFirst<ExpressionStatementNode>(cu);
        if (stmt is null)
            return $"[assignment RHS] no ExpressionStatementNode found.\nLambda: {lambdaText}\nSource:\n{source}";

        if (stmt.Expression is not AssignmentFormExpressionNode assignForm)
            return $"[assignment RHS] expression was {stmt.Expression?.GetType().Name ?? "null"}, expected AssignmentFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";

        if (assignForm.Assignment.Value is not LambdaFormExpressionNode)
            return $"[assignment RHS] assignment value was {assignForm.Assignment.Value?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";

        return null;
    }

    /// <summary>
    /// Verifies that in <c>g(L);</c> the call argument's expression is a lambda.
    /// Returns a failure description, or null on success.
    /// </summary>
    private static string? CheckCallArgument(string lambdaText)
    {
        var source = WrapMethodBody($"g({lambdaText});");
        var cu = ParseOrFail(source);

        var arg = FindFirst<ValueArgumentArgumentNode>(cu);
        if (arg is null)
            return $"[call argument] no ValueArgumentArgumentNode found.\nLambda: {lambdaText}\nSource:\n{source}";

        if (arg.Expression is not LambdaFormExpressionNode)
            return $"[call argument] argument expression was {arg.Expression?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";

        return null;
    }

    /// <summary>
    /// Verifies that in <c>return L;</c> the return value is a lambda.
    /// Returns a failure description, or null on success.
    /// </summary>
    private static string? CheckReturnValue(string lambdaText)
    {
        var source = WrapMethodBody($"return {lambdaText};");
        var cu = ParseOrFail(source);

        var ret = FindFirst<ReturnStatementNode>(cu);
        if (ret is null)
            return $"[return value] no ReturnStatementNode found.\nLambda: {lambdaText}\nSource:\n{source}";

        if (ret.Value is not LambdaFormExpressionNode)
            return $"[return value] return value was {ret.Value?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";

        return null;
    }

    /// <summary>
    /// Verifies that in <c>q =&gt; L</c> the outer lambda's body is itself a lambda.
    /// Returns a failure description, or null on success.
    /// </summary>
    private static string? CheckNestedLambdaBody(string lambdaText)
    {
        var source = WrapMethodBody($"var h = q => {lambdaText};");
        var cu = ParseOrFail(source);

        // The outer lambda is the first lambda encountered top-down.
        var outer = FindFirst<ExpressionLambdaLambdaExpressionNode>(cu);
        if (outer is null)
            return $"[nested lambda body] no outer ExpressionLambdaLambdaExpressionNode found.\nLambda: {lambdaText}\nSource:\n{source}";

        if (outer.Body is not LambdaFormExpressionNode)
            return $"[nested lambda body] outer lambda body was {outer.Body?.GetType().Name ?? "null"}, expected LambdaFormExpressionNode.\nLambda: {lambdaText}\nSource:\n{source}";

        return null;
    }
}

/// <summary>
/// A generated lambda scenario for <see cref="LambdaReachabilityPropertyTests"/>.
/// Carries the lambda source fragment to be embedded in each value position.
/// </summary>
public sealed class LambdaReachabilityScenario
{
    /// <summary>The lambda source fragment, e.g. <c>static (int x) =&gt; x + 1</c>.</summary>
    public string LambdaText { get; }

    public LambdaReachabilityScenario(string lambdaText)
    {
        LambdaText = lambdaText;
    }

    public override string ToString() => LambdaText;
}

/// <summary>
/// FsCheck generator producing structurally valid lambda source fragments across
/// every syntactic axis: modifiers (none/<c>static</c>/<c>async</c>/<c>static async</c>),
/// parameter forms (single implicit, parenthesized zero/one/many, typed), and bodies
/// (expression body, block body, empty block body).
/// </summary>
public static class LambdaReachabilityGenerator
{
    // Modifier prefixes (with trailing space when present).
    private static readonly string[] Modifiers =
        ["", "static ", "async ", "static async "];

    // Parameter lists. A bare single implicit identifier plus parenthesized forms.
    private static readonly string[] ParamLists =
        ["x", "(x)", "(x, y)", "()", "(int x)", "(int x, string y)"];

    // Expression bodies (no method calls or assignments, so the only lambda node and
    // the only call argument in any wrapping comes from the lambda itself).
    private static readonly string[] ExpressionBodies =
        ["x + 1", "42", "x", "\"hi\"", "x * y", "true"];

    private static Gen<string> GenExpressionBodyLambda() =>
        from modifier in Gen.Elements(Modifiers)
        from paramList in Gen.Elements(ParamLists)
        from body in Gen.Elements(ExpressionBodies)
        select $"{modifier}{paramList} => {body}";

    private static Gen<string> GenBlockBodyLambda() =>
        from modifier in Gen.Elements(Modifiers)
        from paramList in Gen.Elements(ParamLists)
        from body in Gen.Elements(ExpressionBodies)
        from empty in Gen.Elements(true, false)
        let block = empty ? "{ }" : $"{{ return {body}; }}"
        select $"{modifier}{paramList} => {block}";

    /// <summary>Generates a random lambda scenario covering all syntactic forms.</summary>
    public static Gen<LambdaReachabilityScenario> GenLambda() =>
        Gen.OneOf(GenExpressionBodyLambda(), GenBlockBodyLambda())
            .Select(text => new LambdaReachabilityScenario(text));
}
