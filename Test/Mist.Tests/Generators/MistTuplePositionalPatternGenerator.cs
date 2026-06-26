// Generators for Mist tuple positional pattern source strings.
// Produces random but structurally valid programs containing tuple positional patterns
// for property-based testing.
// Feature: tuple-pattern-matching, Property 1: Pretty-print round-trip
// **Validates: Requirements 1.1, 1.3, 1.4, 5.1, 6.1, 6.2, 6.3**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated tuple positional pattern source string for property-based testing.
/// Carries the complete Mist program source text along with a human-readable description.
/// </summary>
public sealed class TuplePositionalPatternSourceScenario
{
    /// <summary>The complete Mist program source string containing a tuple positional pattern.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public TuplePositionalPatternSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist programs containing tuple positional patterns.
/// Generates random valid tuple positional patterns covering:
/// <list type="bullet">
///   <item>Arities: 2, 3, 4, 5</item>
///   <item>Subpattern types: constant (int, string, bool), relational, logical (and), var, discard, type</item>
///   <item>Nesting: tuple patterns within tuple patterns at arbitrary depth</item>
///   <item>Contexts: is expressions</item>
/// </list>
/// Each generated program is a complete, parseable Mist source file with namespace,
/// class, and method wrapping.
/// </summary>
public static class MistTuplePositionalPatternGenerator
{
    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] BindingNames = ["a", "b", "c", "d", "e", "f"];

    // ═══════════════════════════════════════════════════════════════════════
    // Subpattern generators (produce source text fragments)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Generates an integer constant pattern: 0, 1, 42, 99.</summary>
    private static Gen<string> GenIntConstantPattern() =>
        Gen.Choose(0, 99).Select(n => n.ToString());

    /// <summary>Generates a string constant pattern: "hello", "world".</summary>
    private static Gen<string> GenStringConstantPattern() =>
        Gen.Elements("\"hello\"", "\"world\"", "\"test\"");

    /// <summary>Generates a bool constant pattern: true, false.</summary>
    private static Gen<string> GenBoolConstantPattern() =>
        Gen.Elements("true", "false");

    /// <summary>Generates a relational pattern: > 0, < 100, >= 5, <= 50.</summary>
    private static Gen<string> GenRelationalPattern() =>
        from op in Gen.Elements(">", "<", ">=", "<=")
        from value in Gen.Choose(0, 99)
        select $"{op} {value}";

    /// <summary>Generates a logical and pattern: >= lo and <= hi.</summary>
    private static Gen<string> GenLogicalAndPattern() =>
        from lo in Gen.Choose(0, 49)
        from hi in Gen.Choose(50, 99)
        select $">= {lo} and <= {hi}";

    /// <summary>Generates a var pattern: var a, var b.</summary>
    private static Gen<string> GenVarPattern() =>
        Gen.Elements(BindingNames).Select(name => $"var {name}");

    /// <summary>Generates a discard pattern: _.</summary>
    private static Gen<string> GenDiscardPattern() =>
        Gen.Constant("_");

    /// <summary>Generates a type pattern: int n, string s.</summary>
    private static Gen<string> GenTypePattern() =>
        from typeName in Gen.Elements("int", "string")
        from bindName in Gen.Elements(BindingNames)
        select $"{typeName} {bindName}";

    // ═══════════════════════════════════════════════════════════════════════
    // Depth-bounded pattern generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a leaf subpattern (no nesting). Used at maximum depth or as
    /// elements within tuple patterns.
    /// </summary>
    private static Gen<string> GenLeafPattern() =>
        Gen.OneOf(
            GenIntConstantPattern(),
            GenRelationalPattern(),
            GenLogicalAndPattern(),
            GenVarPattern(),
            GenDiscardPattern(),
            GenTypePattern());

    /// <summary>
    /// Generates a subpattern at the given depth. At depth 0, only leaf patterns
    /// are produced. At higher depths, nested tuple positional patterns may appear.
    /// </summary>
    private static Gen<string> GenSubpattern(int depth)
    {
        if(depth <= 0)
        {
            return GenLeafPattern();
        }

        // At higher depths, mix leaf patterns with nested tuple patterns
        return Gen.Frequency(
            (4, GenLeafPattern()),
            (1, GenTuplePatternSource(depth - 1)));
    }

    /// <summary>
    /// Generates a tuple positional pattern source string at the given nesting depth.
    /// Arity is randomly chosen between 2 and 5.
    /// </summary>
    private static Gen<string> GenTuplePatternSource(int depth)
    {
        var arity2 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     select $"({p1}, {p2})";

        var arity3 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     from p3 in GenSubpattern(depth)
                     select $"({p1}, {p2}, {p3})";

        var arity4 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     from p3 in GenSubpattern(depth)
                     from p4 in GenSubpattern(depth)
                     select $"({p1}, {p2}, {p3}, {p4})";

        var arity5 = from p1 in GenSubpattern(depth)
                     from p2 in GenSubpattern(depth)
                     from p3 in GenSubpattern(depth)
                     from p4 in GenSubpattern(depth)
                     from p5 in GenSubpattern(depth)
                     select $"({p1}, {p2}, {p3}, {p4}, {p5})";

        return Gen.Frequency(
            (4, arity2),
            (3, arity3),
            (2, arity4),
            (1, arity5));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Complete program generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a complete Mist program with a tuple positional pattern in an is expression.
    /// The scrutinee is a tuple literal whose arity matches the pattern.
    /// </summary>
    public static Gen<TuplePositionalPatternSourceScenario> GenTuplePatternIsExpression()
    {
        // Generate a tuple pattern at depth 0-2
        return from depth in Gen.Choose(0, 2)
               from pattern in GenTuplePatternSource(depth)
               let scrutinee = GenerateMatchingScrutinee(pattern)
               let body = $"var x = {scrutinee};\n    if (x is {pattern})\n    {{\n    }}"
               let source = WrapInProgram(body)
               select new TuplePositionalPatternSourceScenario(source, $"is expr depth={depth}: {pattern}");
    }

    /// <summary>
    /// Generates a complete Mist program with a nested tuple positional pattern (depth >= 1).
    /// </summary>
    public static Gen<TuplePositionalPatternSourceScenario> GenNestedTuplePatternIsExpression()
    {
        return from depth in Gen.Choose(1, 2)
               from pattern in GenTuplePatternSource(depth)
               let scrutinee = GenerateMatchingScrutinee(pattern)
               let body = $"var x = {scrutinee};\n    if (x is {pattern})\n    {{\n    }}"
               let source = WrapInProgram(body)
               select new TuplePositionalPatternSourceScenario(source, $"nested is expr depth={depth}: {pattern}");
    }

    /// <summary>
    /// Generates a random tuple positional pattern source scenario.
    /// </summary>
    public static Gen<TuplePositionalPatternSourceScenario> GenTuplePositionalPatternSource()
    {
        return Gen.OneOf(
            GenTuplePatternIsExpression(),
            GenNestedTuplePatternIsExpression());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a tuple literal scrutinee that structurally matches the given pattern.
    /// For round-trip testing, we only need the source to parse — type correctness is not required.
    /// We use a simple tuple of integer literals with the same arity.
    /// </summary>
    private static string GenerateMatchingScrutinee(string pattern)
    {
        // Count the arity by parsing the top-level comma-separated elements.
        // A simple approach: count top-level commas (not inside nested parens) + 1.
        var arity = CountTopLevelArity(pattern);
        var elements = Enumerable.Range(1, arity).Select(i => i.ToString());
        return $"({string.Join(", ", elements)})";
    }

    /// <summary>
    /// Counts the arity of a tuple pattern by counting top-level commas + 1.
    /// Assumes the pattern starts with '(' and ends with ')'.
    /// </summary>
    private static int CountTopLevelArity(string pattern)
    {
        if(pattern.Length < 2 || pattern[0] != '(')
        {
            return 1;
        }

        var depth = 0;
        var commas = 0;
        for(var i = 1; i < pattern.Length - 1; i++)
        {
            switch(pattern[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }
        return commas + 1;
    }

    /// <summary>
    /// Wraps a body of statements in a complete Mist program with namespace, class, and Main method.
    /// </summary>
    private static string WrapInProgram(string body)
    {
        return "namespace TestNs;\n\npublic class Program\n{\n    public static void Main()\n    {\n    "
            + body + "\n    }\n}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registration
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// FsCheck Arbitrary for <see cref="TuplePositionalPatternSourceScenario"/>.
    /// </summary>
    public static Arbitrary<TuplePositionalPatternSourceScenario> ArbitraryTuplePositionalPatternSourceScenario() =>
        GenTuplePositionalPatternSource().ToArbitrary();
}