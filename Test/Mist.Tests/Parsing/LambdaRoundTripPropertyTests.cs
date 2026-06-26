// Feature: first-class-functions, Property 6: Pretty-print round-trip
// **Validates: Requirements 13.7, 13.8**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for <b>Property 6: Pretty-Print Round-Trip</b>.
///
/// <para><b>Validates: Requirements 13.7, 13.8</b></para>
///
/// <para>
/// Asserts that the pretty-printer is idempotent for programs containing lambda expressions
/// and <c>func&lt;...&gt;</c> type references. For any generated Mist source <c>S</c>:
/// </para>
/// <code>
///   print(parse(print(parse(S)))) == print(parse(S))
/// </code>
///
/// <para>
/// The generator produces programs with lambdas in:
/// </para>
/// <list type="bullet">
///   <item>Assignment RHS: <c>var f = x =&gt; x + 1;</c></item>
///   <item>Block lambdas: <c>var f = (int x) =&gt; { return x + 1; };</c></item>
///   <item>Call arguments: <c>list.ForEach(x =&gt; Console.WriteLine(x));</c></item>
///   <item>Return statements: <c>return x =&gt; x * 2;</c></item>
///   <item>Nested lambdas: <c>var f = x =&gt; y =&gt; x + y;</c></item>
///   <item>Static/async modifiers: <c>var f = static x =&gt; x;</c></item>
///   <item>Switch arms with guards: <c>value switch { int n when n &gt; 0 =&gt; n.ToString(), _ =&gt; "none" }</c></item>
///   <item><c>func&lt;T1, T2, ..., TReturn&gt;</c> type references in variable declarations</item>
/// </list>
///
/// <para>
/// These tests are parse + pretty-print only and run fully in-process — no <c>dotnet</c>
/// process is spawned.
/// </para>
/// </summary>
public class LambdaRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Arbitrary registration ──────────────────────────────────────────────

    public static Arbitrary<RoundTripScenario> ArbitraryRoundTripScenario()
    {
        return RoundTripGenerator.GenScenario().ToArbitrary();
    }

    // ─── Parse + pretty-print helpers ────────────────────────────────────────

    /// <summary>
    /// Parses a Mist source string, asserting no parse errors, and returns the
    /// <see cref="CompilationUnitNode"/> root.
    /// </summary>
    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    /// <summary>
    /// Performs one parse → pretty-print pass, returning the printed output.
    /// </summary>
    private static string PrintOnce(string source)
    {
        var ast = ParseOrFail(source);
        return MistPrettyPrinter.Print(ast, source);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Property 6: Pretty-Print Round-Trip
    //
    // For any generated program S containing lambdas and func types:
    //   print(parse(print(parse(S)))) == print(parse(S))
    //
    // **Validates: Requirements 13.7, 13.8**
    // ═══════════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(LambdaRoundTripPropertyTests) })]
    public Property PrettyPrint_RoundTrip_IsIdempotent(RoundTripScenario scenario)
    {
        string printed1;
        try
        {
            printed1 = PrintOnce(scenario.Source);
        }
        catch(Exception ex)
        {
            return false.ToProperty()
                .Label($"First print pass failed:\n{ex.Message}\nSource:\n{scenario.Source}");
        }

        string printed2;
        try
        {
            printed2 = PrintOnce(printed1);
        }
        catch(Exception ex)
        {
            return false.ToProperty()
                .Label($"Second print pass failed (re-parse of printed output):\n{ex.Message}\nPrinted1:\n{printed1}\nOriginal source:\n{scenario.Source}");
        }

        var isIdempotent = printed1 == printed2;

        return isIdempotent.ToProperty()
            .Label($"Pretty-print is NOT idempotent.\n" +
                   $"Description: {scenario.Description}\n" +
                   $"Original source:\n{scenario.Source}\n" +
                   $"Printed1:\n{printed1}\n" +
                   $"Printed2:\n{printed2}");
    }
}

/// <summary>
/// A generated Mist program scenario for the pretty-print round-trip property test.
/// </summary>
public sealed class RoundTripScenario
{
    /// <summary>The complete, parseable Mist source program.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of what the scenario exercises.</summary>
    public string Description { get; }

    public RoundTripScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// FsCheck generator producing complete Mist programs that exercise lambda expressions
/// and <c>func&lt;...&gt;</c> type references in various syntactic positions.
/// </summary>
public static class RoundTripGenerator
{
    // ─── Building blocks ─────────────────────────────────────────────────────

    private static readonly string[] Identifiers =
        ["a", "b", "c", "f", "g", "x", "y", "z", "foo", "bar"];

    private static readonly string[] SimpleTypes =
        ["int", "string", "bool", "double"];

    private static readonly string[] Modifiers =
        ["", "static ", "async ", "static async "];

    private static readonly string[] ExpressionBodies =
        ["x + 1", "42", "x", "x * y", "true", "\"hi\""];

    private static readonly string[] BlockBodies =
        ["{ return x + 1; }", "{ return 42; }", "{ }"];

    private static readonly string[] GuardExpressions =
        ["n > 0", "n < 100", "n >= 1", "n <= 50", "n == 42", "isValid"];

    private static readonly string[] SwitchResults =
        ["n.ToString()", "\"yes\"", "0", "42"];

    // ─── Lambda generators ───────────────────────────────────────────────────

    /// <summary>Generates a single-param expression lambda: <c>x =&gt; expr</c>.</summary>
    private static Gen<string> GenSingleParamExprLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from param in Gen.Elements(Identifiers)
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{param} => {body}";
    }

    /// <summary>Generates a parenthesized expression lambda: <c>(x, y) =&gt; expr</c>.</summary>
    private static Gen<string> GenParenExprLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(
                   Gen.Constant("()"),
                   Gen.Elements(Identifiers).Select(n => $"({n})"),
                   from n1 in Gen.Elements(Identifiers)
                   from n2 in Gen.Elements(Identifiers)
                   select $"({n1}, {n2})")
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{paramForm} => {body}";
    }

    /// <summary>Generates a typed-param expression lambda: <c>(int x) =&gt; expr</c>.</summary>
    private static Gen<string> GenTypedParamExprLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(
                   from t in Gen.Elements(SimpleTypes)
                   from n in Gen.Elements(Identifiers)
                   select $"({t} {n})",
                   from t1 in Gen.Elements(SimpleTypes)
                   from n1 in Gen.Elements(Identifiers)
                   from t2 in Gen.Elements(SimpleTypes)
                   from n2 in Gen.Elements(Identifiers)
                   select $"({t1} {n1}, {t2} {n2})")
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{paramForm} => {body}";
    }

    /// <summary>Generates a block-body lambda: <c>(int x) =&gt; { return expr; }</c>.</summary>
    private static Gen<string> GenBlockLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from paramForm in Gen.OneOf(
                   Gen.Elements(Identifiers).Select(n => $"({n})"),
                   from t in Gen.Elements(SimpleTypes)
                   from n in Gen.Elements(Identifiers)
                   select $"({t} {n})",
                   Gen.Constant("()"))
               from body in Gen.Elements(BlockBodies)
               select $"{modifier}{paramForm} => {body}";
    }

    /// <summary>Generates a nested lambda: <c>x =&gt; y =&gt; expr</c>.</summary>
    private static Gen<string> GenNestedLambda()
    {
        return from modifier in Gen.Elements(Modifiers)
               from p1 in Gen.Elements(Identifiers)
               from p2 in Gen.Elements(Identifiers)
               from body in Gen.Elements(ExpressionBodies)
               select $"{modifier}{p1} => {p2} => {body}";
    }

    /// <summary>Generates any lambda form.</summary>
    private static Gen<string> GenAnyLambda()
    {
        return Gen.OneOf(
            GenSingleParamExprLambda(),
            GenParenExprLambda(),
            GenTypedParamExprLambda(),
            GenBlockLambda(),
            GenNestedLambda());
    }

    // ─── func<...> type reference generators ─────────────────────────────────

    /// <summary>Generates a <c>func&lt;T1, ..., TReturn&gt;</c> type reference.</summary>
    private static Gen<string> GenFuncTypeRef()
    {
        return Gen.Choose(1, 4).SelectMany(arity =>
            Gen.ArrayOf(Gen.Elements(SimpleTypes), arity)
                .Select(types => $"func<{string.Join(", ", types)}>"));
    }

    // ─── Program-level scenario generators ───────────────────────────────────

    /// <summary>
    /// Wraps a method body in a complete Mist program with a class and method declaration.
    /// </summary>
    private static string WrapInMethod(string body)
    {
        return $"namespace TestNs;\n\npublic class Program\n{{\n    public void Run()\n    {{\n        {body}\n    }}\n}}";
    }

    /// <summary>Lambda in assignment RHS with inferred type: <c>var f = lambda;</c></summary>
    private static Gen<RoundTripScenario> GenAssignmentRhsScenario()
    {
        return from lambda in GenAnyLambda()
               from name in Gen.Elements(Identifiers)
               let body = $"var {name} = {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"assignment RHS: var {name} = {lambda}");
    }

    /// <summary>Lambda in assignment RHS with func type: <c>func&lt;int, int&gt; f = lambda;</c></summary>
    private static Gen<RoundTripScenario> GenFuncTypedAssignmentScenario()
    {
        return from funcType in GenFuncTypeRef()
               from lambda in GenAnyLambda()
               from name in Gen.Elements(Identifiers)
               let body = $"{funcType} {name} = {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"func-typed assignment: {funcType} {name} = {lambda}");
    }

    /// <summary>Lambda as a call argument: <c>g(lambda);</c></summary>
    private static Gen<RoundTripScenario> GenCallArgumentScenario()
    {
        return from lambda in GenAnyLambda()
               from fn in Gen.Elements(Identifiers)
               let body = $"{fn}({lambda});"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"call argument: {fn}({lambda})");
    }

    /// <summary>Lambda in a return statement: <c>return lambda;</c></summary>
    private static Gen<RoundTripScenario> GenReturnScenario()
    {
        return from lambda in GenAnyLambda()
               let body = $"return {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"return: return {lambda}");
    }

    /// <summary>Lambda in a nested lambda body: <c>var h = q =&gt; lambda;</c></summary>
    private static Gen<RoundTripScenario> GenNestedBodyScenario()
    {
        return from lambda in GenAnyLambda()
               from outer in Gen.Elements(Identifiers)
               let body = $"var h = {outer} => {lambda};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"nested body: var h = {outer} => {lambda}");
    }

    /// <summary>
    /// Switch expression with a guarded arm containing a lambda in the result position:
    /// <c>var r = value switch { int n when guard =&gt; result, _ =&gt; lambda };</c>
    /// </summary>
    private static Gen<RoundTripScenario> GenSwitchArmScenario()
    {
        return from guard in Gen.Elements(GuardExpressions)
               from result in Gen.Elements(SwitchResults)
               from defaultResult in Gen.Elements(SwitchResults)
               let body = $"var r = value switch {{ int n when {guard} => {result}, _ => {defaultResult} }};"
               let source = WrapInMethod(body)
               select new RoundTripScenario(source, $"switch arm: when {guard} => {result}");
    }

    /// <summary>
    /// A <c>func&lt;...&gt;</c> type reference used as a method parameter type.
    /// </summary>
    private static Gen<RoundTripScenario> GenFuncTypeParamScenario()
    {
        return from funcType in GenFuncTypeRef()
               from paramName in Gen.Elements(Identifiers)
               let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public void Run({funcType} {paramName})\n    {{\n    }}\n}}"
               select new RoundTripScenario(source, $"func type param: {funcType} {paramName}");
    }

    /// <summary>
    /// Combines all scenario generators with equal weight.
    /// </summary>
    public static Gen<RoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenAssignmentRhsScenario(),
            GenFuncTypedAssignmentScenario(),
            GenCallArgumentScenario(),
            GenReturnScenario(),
            GenNestedBodyScenario(),
            GenSwitchArmScenario(),
            GenFuncTypeParamScenario());
    }
}