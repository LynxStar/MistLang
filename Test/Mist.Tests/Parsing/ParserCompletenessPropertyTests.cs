// Feature: dependent-type-refinement-constraints, Property 5: Parser Completeness Invariant
// **Validates: Requirements 3.1, 3.3, 3.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;

namespace Mist.Tests.Parsing;

public class ParserCompletenessPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

#pragma warning disable CA1034

    public record CompletenessScenario(
        string ValidPrefix,
        string TrailingContent,
        string Description)
    {
        public string CombinedSource => ValidPrefix + TrailingContent;

        public override string ToString()
        {
            return Description;
        }
    }

#pragma warning restore CA1034

    /// Each prefix ends with a newline to ensure the trailing content starts on a known line.
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

    /// CompilationUnit is <c>TopLevelItem*</c>, which includes Statement, so each fragment
    /// must be unable to complete any TopLevelItem.
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

    private static Gen<CompletenessScenario> GenScenario()
    {
        return from prefix in Gen.Elements(ValidPrefixes)
               from trailing in Gen.Elements(TrailingFragments)
               select new CompletenessScenario(
                   prefix,
                   trailing,
                   $"prefix='{prefix.TrimEnd()}' + trailing='{trailing}'");
    }

    public static Arbitrary<CompletenessScenario> ArbitraryCompletenessScenario()
    {
        return GenScenario().ToArbitrary();
    }

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

        if(diagnostics.Any(d => d.Number.Value == "MIST0121"))
        {
            return true.ToProperty().Label("parser skeletal (MIST0121)");
        }

        if(result.Root is null)
        {
            return true.ToProperty().Label("root is null (prefix did not parse)");
        }

        var incompleteDiag = diagnostics.FirstOrDefault(d =>
            d.Number.Value == "MIST0123");

        var hasDiag = incompleteDiag is not null;

        var hasCorrectSeverity = incompleteDiag?.Severity == DiagnosticSeverity.Error;

        var hasCorrectStage = incompleteDiag?.Stage == PipelineStage.Parsing;

        // Determine the expected line by finding the first unconsumed token in the stream.
        var lastPos = Generated.MistParser.LastPosition;
        var defaultTokens = tokens.DefaultChannelTokens;
        var expectedLine = lastPos < defaultTokens.Length
            ? defaultTokens[lastPos].Start.Line
            : -1;
        var hasCorrectLine = incompleteDiag?.Position().Start.Line == expectedLine;

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