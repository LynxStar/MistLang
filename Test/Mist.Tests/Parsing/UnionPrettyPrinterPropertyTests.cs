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

public sealed class UnionPrettyPrintScenario
{
    public ImmutableArray<string> Constituents { get; }

    public UnionPrettyPrintScenario(ImmutableArray<string> constituents)
    {
        Constituents = constituents;
    }

    public override string ToString()
    {
        return string.Join(" | ", Constituents);
    }
}

public class UnionPrettyPrinterPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static readonly string[] KnownTypes =
        ["int", "string", "bool", "float", "double", "int[]", "string[]", "bool[]"];

    public static Arbitrary<UnionPrettyPrintScenario> ArbitraryUnionPrettyPrintScenario()
    {
        var gen =
            from count in Gen.Choose(2, 5)
            from types in Gen.ArrayOf(Gen.Elements(KnownTypes), count)
            select new UnionPrettyPrintScenario(types.ToImmutableArray());

        return gen.ToArbitrary();
    }

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

    private static string PrettyPrintProgram(string typeAnnotation)
    {
        var source = $"class T {{\n    public {typeAnnotation} x;\n}}";
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source);
    }

    private static string ExtractTypeFromPrettyPrinted(string prettyPrinted)
    {
        var lines = prettyPrinted.Split('\n');
        var fieldLine = lines.FirstOrDefault(l => l.Contains(" x;")) ?? throw new InvalidOperationException(
                $"Could not find field declaration in pretty-printed output:\n{prettyPrinted}");

        var trimmed = fieldLine.Trim();
        const string prefix = "public ";
        const string suffix = " x;";
        if(trimmed.StartsWith(prefix) && trimmed.EndsWith(suffix))
        {
            return trimmed[prefix.Length..^suffix.Length];
        }

        throw new InvalidOperationException(
            $"Unexpected field format: '{fieldLine}'");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_UnionType_HasExactSpacingAroundPipe(UnionPrettyPrintScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var prettyPrinted = PrettyPrintProgram(typeAnnotation);
        var typeStr = ExtractTypeFromPrettyPrinted(prettyPrinted);

        for(var i = 0; i < typeStr.Length; i++)
        {
            if(typeStr[i] == '|')
            {
                if(i == 0 || typeStr[i - 1] != ' ')
                {
                    return false;
                }

                if(i >= 2 && typeStr[i - 2] == ' ')
                {
                    return false;
                }

                if(i == typeStr.Length - 1 || typeStr[i + 1] != ' ')
                {
                    return false;
                }

                if(i + 2 < typeStr.Length && typeStr[i + 2] == ' ')
                {
                    // KnownTypes contain no spaces, so a second space here is always a formatting error.
                    return false;
                }
            }
        }

        return true;
    }

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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_UnionType_NoMalformedPipeSpacing(UnionPrettyPrintScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var prettyPrinted = PrettyPrintProgram(typeAnnotation);
        var typeStr = ExtractTypeFromPrettyPrinted(prettyPrinted);

        if(Regex.IsMatch(typeStr, @"[^ ]\|"))
        {
            return false;
        }

        if(Regex.IsMatch(typeStr, @"\|[^ ]"))
        {
            return false;
        }

        if(Regex.IsMatch(typeStr, @"  \|"))
        {
            return false;
        }

        if(Regex.IsMatch(typeStr, @"\|  "))
        {
            return false;
        }

        return true;
    }

    // Feature: type-unions, Property 4: Pretty-Printer Round-Trip
    /// <summary>
    /// **Validates: Requirements 4.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionPrettyPrinterPropertyTests) })]
    public bool PrettyPrinter_UnionType_RoundTripIsStable(UnionPrettyPrintScenario scenario)
    {
        var typeAnnotation = string.Join(" | ", scenario.Constituents);
        var source = $"class T {{\n    public {typeAnnotation} x;\n}}";

        var ast1 = ParseOrFail(source);

        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return printed1 == printed2;
    }
}