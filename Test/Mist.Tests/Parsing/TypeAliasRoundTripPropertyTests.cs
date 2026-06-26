// Feature: type-aliases, Property 2: Pretty-print round-trip
// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**

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
/// Represents a generated type alias source string for round-trip testing.
/// </summary>
public sealed class TypeAliasRoundTripScenario
{
    public string Source { get; }
    public string Description { get; }

    public TypeAliasRoundTripScenario(string source, string description)
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
/// Property-based tests for type alias pretty-print round-trip (idempotence).
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
///
/// For any valid type alias declaration source string, parsing it and pretty-printing
/// the resulting AST, then parsing and pretty-printing again, SHALL produce identical
/// text output (idempotence).
/// </summary>
public class TypeAliasRoundTripPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Known type names for generation ────────────────────────────────────

    private static readonly string[] SimpleTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    private static readonly string[] AliasNames =
        ["Foo", "Bar", "Baz", "Qux", "MyType", "Result", "Value", "Item"];

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W", "K"];

    private static readonly string[] NamedTypes =
        ["List", "Map", "Set", "Option", "Container", "Wrapper"];

    // ─── Generator ──────────────────────────────────────────────────────────

    public static Arbitrary<TypeAliasRoundTripScenario> ArbitraryTypeAliasRoundTripScenario()
    {
        var gen = Gen.OneOf(
            GenSimpleAlias(),
            GenGenericAlias(),
            GenUnionAlias(),
            GenGenericWithUnionAlias());

        return gen.ToArbitrary();
    }

    /// <summary>Generates a simple type alias: type Foo = int;</summary>
    private static Gen<TypeAliasRoundTripScenario> GenSimpleAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from targetType in Gen.Elements(SimpleTypes)
               let source = $"type {name} = {targetType};"
               select new TypeAliasRoundTripScenario(source, $"simple: {source}");
    }

    /// <summary>Generates a generic type alias: type Bar&lt;T&gt; = List&lt;T&gt;;</summary>
    private static Gen<TypeAliasRoundTripScenario> GenGenericAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from paramCount in Gen.Choose(1, 3)
               from namedType in Gen.Elements(NamedTypes)
               let typeParams = string.Join(", ", TypeParamNames.Take(paramCount))
               let typeArgs = string.Join(", ", TypeParamNames.Take(paramCount))
               let source = $"type {name}<{typeParams}> = {namedType}<{typeArgs}>;"
               select new TypeAliasRoundTripScenario(source, $"generic: {source}");
    }

    /// <summary>Generates a union type alias: type Result = int | string;</summary>
    private static Gen<TypeAliasRoundTripScenario> GenUnionAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from count in Gen.Choose(2, 4)
               from types in Gen.ArrayOf(Gen.Elements(SimpleTypes), count)
               let unionExpr = string.Join(" | ", types)
               let source = $"type {name} = {unionExpr};"
               select new TypeAliasRoundTripScenario(source, $"union: {source}");
    }

    /// <summary>Generates a generic alias with union target: type Result&lt;T&gt; = T | string;</summary>
    private static Gen<TypeAliasRoundTripScenario> GenGenericWithUnionAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from paramCount in Gen.Choose(1, 2)
               from extraType in Gen.Elements(SimpleTypes)
               let typeParams = string.Join(", ", TypeParamNames.Take(paramCount))
               let firstParam = TypeParamNames[0]
               let source = $"type {name}<{typeParams}> = {firstParam} | {extraType};"
               select new TypeAliasRoundTripScenario(source, $"generic+union: {source}");
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

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    // ─── Property Test ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 2: Pretty-print round-trip (idempotence).
    ///
    /// For any valid type alias declaration source string, parsing and pretty-printing
    /// the result, then parsing and pretty-printing again, SHALL produce identical output.
    ///
    /// Verifies: print(parse(print(parse(s)))) == print(parse(s))
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeAliasRoundTripPropertyTests) })]
    public Property RoundTrip_TypeAlias_PrintParseIsIdempotent(TypeAliasRoundTripScenario scenario)
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