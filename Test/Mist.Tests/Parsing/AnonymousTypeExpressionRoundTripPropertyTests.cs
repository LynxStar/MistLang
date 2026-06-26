// Feature: anonymous-types, Property 1: Anonymous type expression round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.6, 1.7**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Represents a test scenario for anonymous type expression round-trip testing.
/// Contains a generated source string with an anonymous type expression and a description.
/// </summary>
public sealed class AnonymousTypeRoundTripScenario
{
    /// <summary>The complete Mist source string containing an anonymous type expression.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario variant.</summary>
    public string Description { get; }

    public AnonymousTypeRoundTripScenario(string source, string description)
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
/// Property-based tests for parsing round-trip of anonymous type expressions.
///
/// Property 1: For any valid anonymous type expression source string (containing any
/// combination of explicit assignments and property projections), parsing the source
/// then pretty-printing the resulting AST then re-parsing SHALL produce a structurally
/// equivalent AST node. Equivalence is verified via idempotence of parse → pretty-print
/// (printed1 == printed2).
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.6, 1.7**
/// </summary>
public class AnonymousTypeExpressionRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<AnonymousTypeRoundTripScenario> ArbitraryScenario()
    {
        return AnonymousTypeRoundTripGenerator.GenScenario().ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

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

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Anonymous type expression round-trip
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.6, 1.7**
    //
    // For any valid anonymous type expression source string (containing any
    // combination of explicit assignments and property projections), parsing
    // and pretty-printing the result, then parsing and pretty-printing again,
    // SHALL produce identical output (idempotence / round-trip stability).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypeExpressionRoundTripPropertyTests) })]
    public Property RoundTrip_AnonymousTypeExpression_PrintParseIsIdempotent(AnonymousTypeRoundTripScenario scenario)
    {
        // Step 1: Parse the generated source
        var ast1 = ParseOrFail(scenario.Source);

        // Step 2: Pretty-print AST1 → printed1
        var printed1 = MistPrettyPrinter.Print(ast1, scenario.Source);

        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false.ToProperty().Label("printed1 was empty");
        }

        // Step 3: Parse printed1 → AST2
        var ast2 = ParseOrFail(printed1);

        // Step 4: Pretty-print AST2 → printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify idempotence: printed1 == printed2
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nSource: {scenario.Source}\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }
}

/// <summary>
/// FsCheck generators for Mist source strings containing anonymous type expressions.
/// Generates random valid anonymous type expressions covering:
/// <list type="bullet">
///   <item>Only explicit assignments: <c>new { Name = expr, Value = expr }</c></item>
///   <item>Only projections from identifiers: <c>new { variable }</c></item>
///   <item>Only projections from member access: <c>new { obj.Member }</c></item>
///   <item>Mixed explicit assignments and projections: <c>new { Name = expr, variable }</c></item>
/// </list>
/// </summary>
public static class AnonymousTypeRoundTripGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────

    private static readonly string[] PropertyNames =
        ["Name", "Value", "Count", "Size", "Width", "Height", "Timeout", "Label", "Score", "Enabled"];

    private static readonly string[] IdentifierNames =
        ["name", "value", "count", "size", "width", "height", "timeout", "label", "score", "enabled"];

    private static readonly string[] ObjectNames =
        ["obj", "item", "data", "config", "state", "node"];

    private static readonly string[] MemberNames =
        ["Name", "Value", "Id", "Key", "Type", "Status"];

    // ─── Value expression generators ────────────────────────────────────

    private static Gen<string> GenIntExpr()
    {
        return Gen.Choose(0, 999).Select(n => n.ToString());
    }

    private static Gen<string> GenBoolExpr()
    {
        return Gen.Elements("true", "false");
    }

    private static Gen<string> GenStringExpr()
    {
        return Gen.Elements("\"hello\"", "\"world\"", "\"test\"", "\"\"");
    }

    private static Gen<string> GenValueExpr()
    {
        return Gen.OneOf(
            GenIntExpr(),
            GenBoolExpr(),
            GenStringExpr());
    }

    // ─── Anonymous property generators ──────────────────────────────────

    /// <summary>
    /// Generates an explicit property assignment: <c>Name = expr</c>
    /// </summary>
    private static Gen<string> GenExplicitProperty(int index)
    {
        return from value in GenValueExpr()
               select $"{PropertyNames[index % PropertyNames.Length]} = {value}";
    }

    /// <summary>
    /// Generates a projection from a simple identifier: <c>variable</c>
    /// </summary>
    private static Gen<string> GenIdentifierProjection(int index)
    {
        return Gen.Constant(IdentifierNames[index % IdentifierNames.Length]);
    }

    /// <summary>
    /// Generates a projection from a member access: <c>obj.Member</c>
    /// </summary>
    private static Gen<string> GenMemberAccessProjection()
    {
        return from objIdx in Gen.Choose(0, ObjectNames.Length - 1)
               from memberIdx in Gen.Choose(0, MemberNames.Length - 1)
               select $"{ObjectNames[objIdx]}.{MemberNames[memberIdx]}";
    }

    // ─── Scenario generators ────────────────────────────────────────────

    /// <summary>
    /// Generates an anonymous type with only explicit assignments.
    /// Example: <c>new { Name = "hello", Value = 42 }</c>
    /// </summary>
    private static Gen<AnonymousTypeRoundTripScenario> GenExplicitOnly()
    {
        return from count in Gen.Choose(1, 4)
               from properties in GenExplicitProperties(count)
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"explicit-only({count}): {newExpr}");
    }

    /// <summary>
    /// Generates an anonymous type with only identifier projections.
    /// Example: <c>new { name, value }</c>
    /// </summary>
    private static Gen<AnonymousTypeRoundTripScenario> GenIdentifierProjectionOnly()
    {
        return from count in Gen.Choose(1, 4)
               from properties in GenIdentifierProjections(count)
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"projection-identifier({count}): {newExpr}");
    }

    /// <summary>
    /// Generates an anonymous type with only member access projections.
    /// Example: <c>new { obj.Name, config.Value }</c>
    /// </summary>
    private static Gen<AnonymousTypeRoundTripScenario> GenMemberAccessProjectionOnly()
    {
        return from count in Gen.Choose(1, 3)
               from properties in GenMemberAccessProjections(count)
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"projection-member-access({count}): {newExpr}");
    }

    /// <summary>
    /// Generates an anonymous type with a mix of explicit assignments and projections.
    /// Example: <c>new { Name = "hello", value, obj.Id }</c>
    /// </summary>
    private static Gen<AnonymousTypeRoundTripScenario> GenMixed()
    {
        return from explicitCount in Gen.Choose(1, 2)
               from projectionCount in Gen.Choose(1, 2)
               from explicitProps in GenExplicitProperties(explicitCount)
               from projections in GenMixedProjections(projectionCount)
               let properties = $"{explicitProps}, {projections}"
               let newExpr = $"new {{ {properties} }}"
               let source = WrapInProgram(newExpr)
               select new AnonymousTypeRoundTripScenario(source,
                   $"mixed({explicitCount}+{projectionCount}): {newExpr}");
    }

    // ─── Combined generator ─────────────────────────────────────────────

    /// <summary>
    /// Generates a random anonymous type expression scenario covering all variants.
    /// </summary>
    public static Gen<AnonymousTypeRoundTripScenario> GenScenario()
    {
        return Gen.OneOf(
            GenExplicitOnly(),
            GenIdentifierProjectionOnly(),
            GenMemberAccessProjectionOnly(),
            GenMixed());
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static Gen<string> GenExplicitProperties(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(GenExplicitProperty)
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    private static Gen<string> GenIdentifierProjections(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(GenIdentifierProjection)
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    private static Gen<string> GenMemberAccessProjections(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(_ => GenMemberAccessProjection())
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    private static Gen<string> GenMixedProjections(int count)
    {
        var gens = Enumerable.Range(0, count)
            .Select(i => i % 2 == 0
                ? GenIdentifierProjection(i + 5) // offset to avoid name collision with explicit props
                : GenMemberAccessProjection())
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray())
            .Select(props => string.Join(", ", props));
    }

    /// <summary>
    /// Wraps an anonymous type expression in a minimal compilable Mist program structure.
    /// Uses var declaration since anonymous types require type inference.
    /// </summary>
    private static string WrapInProgram(string newExpr)
    {
        return $@"class Program {{
    public static void Main() {{
        var x = {newExpr};
    }}
}}";
    }
}