// Feature: type-unions, Property 3: Pretty-Printer Formatting
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4**

using System.Collections.Immutable;
using System.Text.RegularExpressions;
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
/// Represents a generated union type scenario for pretty-printer testing.
/// </summary>
public sealed class UnionPrettyPrintScenario
{
    public ImmutableArray<string> Constituents { get; }

    public UnionPrettyPrintScenario(ImmutableArray<string> constituents)
    {
        Constituents = constituents;
    }

    public override string ToString() =>
        string.Join(" | ", Constituents);
}

/// <summary>
/// Property-based tests for pretty-printer formatting of union types.
///
/// Property 3: For any UnionTypeRefNode AST, the pretty-printer SHALL produce output
/// with exactly one space before and after each | operator, and constituent types
/// formatted according to their individual formatting rules.
/// </summary>
public class UnionPrettyPrinterPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known types for generation ─────────────────────────────────────────

    /// <summary>
    /// Valid type names that the parser and pretty-printer handle correctly.
    /// Includes primitives, array types, and named types.
    /// </summary>
    private static readonly string[] KnownTypes =
        ["int", "string", "bool", "float", "double", "int[]", "string[]", "bool[]"];

    // ─── Generator ──────────────────────────────────────────────────────────

    public static Arbitrary<UnionPrettyPrintScenario> ArbitraryUnionPrettyPrintScenario()
    {
        var gen =
            from count in Gen.Choose(2, 5)
            from types in Gen.ArrayOf(Gen.Elements(KnownTypes), count)
            select new UnionPrettyPrintScenario(types.ToImmutableArray());

        return gen.ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

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
    /// Wraps a union type annotation in a minimal program, parses it, pretty-prints
    /// the full program, and returns the pretty-printed output.
    /// </summary>
    private static string PrettyPrintProgram(string typeAnnotation)
    {
        var source = $"class T {{\n    public {typeAnnotation} x;\n}}";
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source);
    }

    /// <summary>
    /// Extracts the union type string from the pretty-printed field declaration.
    /// The field is formatted as "    public {type} x;" — we extract the type portion.
    /// </summary>
    private static string ExtractTypeFromPrettyPrinted(string prettyPrinted)
    {
        // Find the field declaration line: "    public {type} x;"
        var lines = prettyPrinted.Split('\n');
        var fieldLine = lines.FirstOrDefault(l => l.Contains(" x;"));
        if (fieldLine is null)
            throw new InvalidOperationException(
                $"Could not find field declaration in pretty-printed output:\n{prettyPrinted}");

        // Strip "    public " prefix and " x;" suffix
        var trimmed = fieldLine.Trim();
        const string prefix = "public ";
        const string suffix = " x;";
        if (trimmed.StartsWith(prefix) && trimmed.EndsWith(suffix))
        {
            return trimmed[prefix.Length..^suffix.Length];
        }

        throw new InvalidOperationException(
            $"Unexpected field format: '{fieldLine}'");
    }

    // ─── Property Tests ─────────────────────────────────────────────────────

    /// <summary>
    /// Property 3a: The pretty-printed union type uses exactly " | " (one space, pipe,
    /// one space) between each constituent. No double spaces, no missing spaces.
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_UnionType_HasExactSpacingAroundPipe(UnionPrettyPrintScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var prettyPrinted = PrettyPrintProgram(typeAnnotation);
        var typeStr = ExtractTypeFromPrettyPrinted(prettyPrinted);

        // Every | in the type string must be surrounded by exactly one space on each side
        for (var i = 0; i < typeStr.Length; i++)
        {
            if (typeStr[i] == '|')
            {
                // Must have exactly one space before
                if (i == 0 || typeStr[i - 1] != ' ')
                    return false;
                // Must not have double space before (i.e., char at i-2 should not be space)
                if (i >= 2 && typeStr[i - 2] == ' ')
                    return false;

                // Must have exactly one space after
                if (i == typeStr.Length - 1 || typeStr[i + 1] != ' ')
                    return false;
                // Must not have double space after
                if (i + 2 < typeStr.Length && typeStr[i + 2] == ' ')
                {
                    // Exception: the next char after space could be another type name starting
                    // with a space-containing token — but our types don't have spaces.
                    // For our known types, this would be a formatting error.
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Property 3b: The number of | separators in the pretty-printed union type equals
    /// N-1 for N constituents.
    ///
    /// **Validates: Requirements 4.1, 4.2, 4.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_UnionType_HasCorrectPipeCount(UnionPrettyPrintScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var prettyPrinted = PrettyPrintProgram(typeAnnotation);
        var typeStr = ExtractTypeFromPrettyPrinted(prettyPrinted);

        var pipeCount = typeStr.Count(c => c == '|');
        var expectedPipes = scenario.Constituents.Length - 1;

        return pipeCount == expectedPipes;
    }

    /// <summary>
    /// Property 3c: The pretty-printed output does not contain double spaces or
    /// missing spaces around any | character (no "x|y", "x |y", "x| y", or "x  | y").
    ///
    /// **Validates: Requirements 4.1, 4.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_UnionType_NoMalformedPipeSpacing(UnionPrettyPrintScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var prettyPrinted = PrettyPrintProgram(typeAnnotation);
        var typeStr = ExtractTypeFromPrettyPrinted(prettyPrinted);

        // Check for malformed patterns:
        // "|" not preceded by space (e.g., "int|string")
        if (Regex.IsMatch(typeStr, @"[^ ]\|"))
            return false;

        // "|" not followed by space (e.g., "int |string")
        if (Regex.IsMatch(typeStr, @"\|[^ ]"))
            return false;

        // Double space before pipe (e.g., "int  | string")
        if (Regex.IsMatch(typeStr, @"  \|"))
            return false;

        // Double space after pipe (e.g., "int |  string")
        if (Regex.IsMatch(typeStr, @"\|  "))
            return false;

        return true;
    }

    // ─── Property 4: Pretty-Printer Round-Trip ──────────────────────────────

    // Feature: type-unions, Property 4: Pretty-Printer Round-Trip
    /// <summary>
    /// Property 4: For any valid Mist source containing union type annotations,
    /// parsing then pretty-printing then parsing again then pretty-printing again
    /// SHALL produce identical pretty-printed output (round-trip stability).
    ///
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_UnionType_RoundTripIsStable(UnionPrettyPrintScenario scenario)
    {
        // Step 1: Build source with the union type
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var source = $"class T {{\n    public {typeAnnotation} x;\n}}";

        // Step 2: Parse → get AST1
        var ast1 = ParseOrFail(source);

        // Step 3: Pretty-print AST1 → printed source 1
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        // Step 4: Parse the printed source → get AST2
        var ast2 = ParseOrFail(printed1);

        // Step 5: Pretty-print AST2 → printed source 2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 6: Verify printed source 1 == printed source 2 (round-trip stability)
        return printed1 == printed2;
    }
}
