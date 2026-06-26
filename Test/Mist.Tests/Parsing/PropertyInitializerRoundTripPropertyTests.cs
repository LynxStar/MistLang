// Feature: property-initializers, Property 1: Pretty-printer round trip
// **Validates: Requirements 9.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for pretty-printer round trip with property initializer blocks.
///
/// **Validates: Requirements 9.4**
///
/// For any valid Mist source with property initializers, parse → pretty-print → parse →
/// pretty-print produces identical text.
/// </summary>
public class PropertyInitializerRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<PropertyInitializerSourceScenario> ArbitraryScenario()
    {
        return PropertyInitializerSourceGenerator.GenScenario().ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
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
    // Property 1: Pretty-printer round trip
    //
    // **Validates: Requirements 9.4**
    //
    // For any valid Mist source with property initializers, parsing and
    // pretty-printing the result, then parsing and pretty-printing again,
    // SHALL produce identical output (idempotence / round-trip stability).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(PropertyInitializerRoundTripPropertyTests) })]
    public Property RoundTrip_PropertyInitializer_PrintParseIsIdempotent(PropertyInitializerSourceScenario scenario)
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
/// Metadata about a generated property initializer source string for round-trip testing.
/// </summary>
public sealed class PropertyInitializerSourceScenario
{
    /// <summary>The complete Mist source string containing a property initializer expression.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public PropertyInitializerSourceScenario(string source, string description)
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
/// FsCheck generators for Mist source strings containing property initializer blocks.
/// Generates random valid <c>new</c> expressions with property initializer blocks covering:
/// <list type="bullet">
///   <item>With constructor arguments</item>
///   <item>Without constructor arguments (no-args variant)</item>
///   <item>Varying assignment counts (0–5)</item>
///   <item>Trailing commas</item>
///   <item>Nested expressions as values</item>
/// </list>
/// </summary>
public static class PropertyInitializerSourceGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Config", "Point", "Settings", "Options", "Data", "State"];

    private static readonly string[] PropertyNames =
        ["Name", "Value", "Count", "Size", "Width", "Height", "Timeout", "Label", "Score", "Enabled"];

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

    private static Gen<string> GenAdditiveExpr()
    {
        return from left in Gen.Choose(0, 100)
               from right in Gen.Choose(1, 50)
               select $"{left} + {right}";
    }

    private static Gen<string> GenMultiplicativeExpr()
    {
        return from left in Gen.Choose(1, 20)
               from right in Gen.Choose(1, 10)
               select $"{left} * {right}";
    }

    private static Gen<string> GenValueExpr()
    {
        return Gen.OneOf(
            GenIntExpr(),
            GenBoolExpr(),
            GenStringExpr(),
            GenAdditiveExpr(),
            GenMultiplicativeExpr());
    }

    // ─── Assignment generators ──────────────────────────────────────────

    private static Gen<string> GenAssignment(int index)
    {
        return from value in GenValueExpr()
               select $"{PropertyNames[index]} = {value}";
    }

    private static Gen<string[]> GenAssignments(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(Array.Empty<string>());
        }

        // Use distinct property names by picking from the pool sequentially
        var gens = Enumerable.Range(0, count)
            .Select(i => GenAssignment(i))
            .ToArray();

        return gens.Aggregate(
            Gen.Constant(Array.Empty<string>()),
            (acc, gen) => from arr in acc
                          from item in gen
                          select arr.Append(item).ToArray());
    }

    // ─── Constructor argument generators ────────────────────────────────

    private static Gen<string> GenConstructorArgs()
    {
        return Gen.OneOf(
            Gen.Constant(""),
            Gen.Choose(0, 100).Select(n => n.ToString()),
            from a in Gen.Choose(0, 50)
            from b in Gen.Choose(0, 50)
            select $"{a}, {b}",
            from a in Gen.Choose(0, 50)
            from b in Gen.Choose(0, 50)
            from c in Gen.Choose(0, 50)
            select $"{a}, {b}, {c}");
    }

    // ─── Scenario generators ────────────────────────────────────────────

    /// <summary>
    /// Generates a new expression with constructor args and property initializer block.
    /// Example: <c>new Point(1, 2) { X = 3, Y = 4 }</c>
    /// </summary>
    private static Gen<PropertyInitializerSourceScenario> GenWithArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from args in GenConstructorArgs()
               from assignmentCount in Gen.Choose(1, 5)
               from assignments in GenAssignments(assignmentCount)
               from trailingComma in Gen.Elements(true, false)
               let assignmentList = string.Join(", ", assignments) + (trailingComma ? "," : "")
               let newExpr = $"new {typeName}({args}) {{ {assignmentList} }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"with-args({assignmentCount}): new {typeName}({args}) {{ ... }}");
    }

    /// <summary>
    /// Generates a new expression without constructor args (no-args variant).
    /// Example: <c>new Config { Timeout = 30, Verbose = true }</c>
    /// </summary>
    private static Gen<PropertyInitializerSourceScenario> GenNoArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from assignmentCount in Gen.Choose(1, 5)
               from assignments in GenAssignments(assignmentCount)
               from trailingComma in Gen.Elements(true, false)
               let assignmentList = string.Join(", ", assignments) + (trailingComma ? "," : "")
               let newExpr = $"new {typeName} {{ {assignmentList} }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"no-args({assignmentCount}): new {typeName} {{ ... }}");
    }

    /// <summary>
    /// Generates a new expression with constructor args and an empty initializer block.
    /// Example: <c>new Config() { }</c>
    /// </summary>
    private static Gen<PropertyInitializerSourceScenario> GenEmptyBlock()
    {
        return from typeName in Gen.Elements(TypeNames)
               from args in GenConstructorArgs()
               let newExpr = $"new {typeName}({args}) {{  }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"empty-block: new {typeName}({args}) {{  }}");
    }

    /// <summary>
    /// Generates a new expression with nested new expressions as values.
    /// Example: <c>new Config { Value = new Point(1, 2) { X = 3 } }</c>
    /// </summary>
    private static Gen<PropertyInitializerSourceScenario> GenNested()
    {
        return from outerType in Gen.Elements(TypeNames)
               from innerType in Gen.Elements(TypeNames)
               from innerArgs in GenConstructorArgs()
               from innerPropIndex in Gen.Choose(0, 4)
               from innerValue in GenIntExpr()
               from outerPropIndex in Gen.Choose(5, 9)
               let innerExpr = $"new {innerType}({innerArgs}) {{ {PropertyNames[innerPropIndex]} = {innerValue} }}"
               let newExpr = $"new {outerType}() {{ {PropertyNames[outerPropIndex]} = {innerExpr} }}"
               let source = WrapInProgram(newExpr)
               select new PropertyInitializerSourceScenario(source,
                   $"nested: new {outerType}() {{ ... = new {innerType}(...) {{ ... }} }}");
    }

    // ─── Combined generator ─────────────────────────────────────────────

    /// <summary>
    /// Generates a random property initializer source scenario covering all variants.
    /// </summary>
    public static Gen<PropertyInitializerSourceScenario> GenScenario()
    {
        return Gen.OneOf(
            GenWithArgs(),
            GenNoArgs(),
            GenEmptyBlock(),
            GenNested());
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a new expression in a minimal compilable Mist program structure.
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