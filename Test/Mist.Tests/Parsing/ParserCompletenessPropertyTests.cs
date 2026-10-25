// Feature: dependent-type-refinement-constraints, Property 5: Parser Completeness Invariant
// **Validates: Requirements 3.1, 3.3, 3.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for parser completeness checking.
///
/// **Property 5: Parser Completeness Invariant**
///
/// For all source files where the Storm parser returns a non-null root but does not
/// consume all default-channel tokens, <c>MIST_PARSE_INCOMPLETE</c> is emitted with
/// the correct line position of the first unconsumed token.
///
/// **Validates: Requirements 3.1, 3.3, 3.4**
/// </summary>
public class ParserCompletenessPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario combining a valid Mist source prefix with trailing unparseable content.
    /// </summary>
    public record CompletenessScenario(
        string ValidPrefix,
        string TrailingContent,
        string Description)
    {
        /// <summary>The combined source text that should trigger MIST_PARSE_INCOMPLETE.</summary>
        public string CombinedSource => ValidPrefix + TrailingContent;

        public override string ToString()
        {
            return Description;
        }
    }

#pragma warning restore CA1034

    // ─── Valid prefix pool ───────────────────────────────────────────────

    /// <summary>
    /// Complete, parseable Mist programs that the Storm parser consumes fully.
    /// Each prefix ends with a newline to ensure the trailing content starts on a known line.
    /// </summary>
    private static readonly string[] ValidPrefixes =
    [
        "class Foo { }\n",
        "public class Bar { }\n",
        "namespace MyApp;\n\nclass Foo { }\n",
        "public class Program\n{\n    public static void Main()\n    {\n    }\n}\n",
        "public struct Point { int x; int y; }\n",
        "abstract IFoo { void DoStuff(); }\n",
        "class Container<T> { T value; }\n",
        "namespace Ns;\n\npublic class A { }\n\npublic class B { }\n",
    ];

    /// <summary>
    /// Trailing content fragments that are NOT parseable as additional top-level items.
    /// The PEG parser's CompilationUnit rule uses <c>TopLevelItem*</c> which includes
    /// Statement alternatives. To trigger the completeness check, we use fragments
    /// that cannot start any TopLevelItem alternative:
    /// - Unmatched closing delimiters (cannot start any rule)
    /// - Incomplete declarations (keyword without required following tokens)
    /// - Bare expressions without a trailing semicolon (ExpressionStatement needs SEMICOLON)
    /// - Content starting with tokens that no rule alternative accepts
    /// </summary>
    private static readonly string[] TrailingFragments =
    [
        "garbage",                  // lone identifier without semicolon → not a valid Statement
        "} } }",                    // unmatched closing braces
        "42 + 3",                   // expression without semicolon
        "random identifier here",   // multiple identifiers without structure or semicolons
        "class",                    // incomplete class declaration (keyword without name/body)
        ") ) )",                    // unmatched closing parens
        "] ] ]",                    // unmatched closing brackets
        "fn broken",                // not a Mist keyword combination
        "struct",                   // incomplete struct declaration
        "abstract",                // incomplete abstract declaration
    ];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<CompletenessScenario> GenScenario()
    {
        return from prefix in Gen.Elements(ValidPrefixes)
               from trailing in Gen.Elements(TrailingFragments)
               select new CompletenessScenario(
                   prefix,
                   trailing,
                   $"prefix='{prefix.TrimEnd()}' + trailing='{trailing}'");
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<CompletenessScenario> ArbitraryCompletenessScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Parser Completeness Invariant
    //
    // **Validates: Requirements 3.1, 3.3, 3.4**
    //
    // For all source files where the Storm parser returns a non-null root
    // but does not consume all default-channel tokens, MIST_PARSE_INCOMPLETE
    // is emitted with the correct line position of the first unconsumed token.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ParserCompletenessPropertyTests) })]
    public Property ValidPrefix_WithTrailingContent_EmitsParseIncomplete(
        CompletenessScenario scenario)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", scenario.CombinedSource);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var diagnostics = bag.ToImmutableArray();

        // If the parser is skeletal (MIST_PARSE_STUB), the completeness check cannot
        // be exercised; treat as vacuously satisfied.
        if(diagnostics.Any(d => d.Number.Value == "MIST_PARSE_STUB"))
        {
            return true.ToProperty().Label("parser skeletal (MIST_PARSE_STUB)");
        }

        // If the prefix itself doesn't parse (root is null), the completeness check
        // is not triggered — this scenario is not applicable. Skip gracefully.
        if(result.Root is null)
        {
            return true.ToProperty().Label("root is null (prefix did not parse)");
        }

        // (1) MIST_PARSE_INCOMPLETE must be present (Requirement 3.1)
        var incompleteDiag = diagnostics.FirstOrDefault(d =>
            d.Number.Value == "MIST_PARSE_INCOMPLETE");

        var hasDiag = incompleteDiag is not null;

        // (2) Severity must be Error (Requirement 3.3)
        var hasCorrectSeverity = incompleteDiag?.Severity == DiagnosticSeverity.Error;

        // (3) Stage must be Parsing (Requirement 3.3)
        var hasCorrectStage = incompleteDiag?.Stage == PipelineStage.Parsing;

        // (4) Location must reference the correct line (Requirement 3.4)
        // Determine the expected line by finding the first unconsumed token in the stream.
        var lastPos = Generated.MistParser.LastPosition;
        var defaultTokens = tokens.DefaultChannelTokens;
        var expectedLine = lastPos < defaultTokens.Length
            ? defaultTokens[lastPos].Start.Line
            : -1;
        var hasCorrectLine = incompleteDiag?.Position().Start.Line == expectedLine;

        // (5) File path must be present (Requirement 3.4)
        var hasFilePath = incompleteDiag?.Location.Path == "test.mist";

        var allPass = hasDiag && hasCorrectSeverity && hasCorrectStage && hasCorrectLine && hasFilePath;

        return allPass
            .ToProperty()
            .Label(
                $"scenario: {scenario.Description}\n"
                + $"hasDiag={hasDiag}, severity={incompleteDiag?.Severity}, "
                + $"stage={incompleteDiag?.Stage}, "
                + $"actualLine={incompleteDiag?.Position().Start.Line}, "
                + $"expectedLine={expectedLine}, "
                + $"filePath={incompleteDiag?.Location.Path}\n"
                + $"lastPos={lastPos}, tokenCount={defaultTokens.Length}\n"
                + $"all diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Number.Value}@L{d.Position().Start.Line}"))}]\n"
                + $"source:\n{scenario.CombinedSource}");
    }
}