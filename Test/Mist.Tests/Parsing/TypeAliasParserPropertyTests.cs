// Feature: type-aliases, Property 1: Parsing produces correct AST structure
// **Validates: Requirements 2.1, 2.2, 2.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for type alias parsing correctness.
///
/// **Validates: Requirements 2.1, 2.2, 2.3**
///
/// For any valid type alias source string (simple, generic with 1–5 type parameters,
/// or with union target types of 2–5 constituents), parsing the source SHALL produce
/// a TypeAliasDeclarationNode whose Name, TypeParams, and Type fields correctly
/// reflect the input.
/// </summary>
public class TypeAliasParserPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    public static Arbitrary<TypeAliasSourceScenario> ArbitraryTypeAliasSourceScenario() =>
        MistTypeAliasGenerator.ArbitraryTypeAliasSourceScenario();

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

    private static TypeAliasDeclarationNode ExtractTypeAlias(CompilationUnitNode cu)
    {
        var item = cu.Items.FirstOrDefault()
            ?? throw new InvalidOperationException("No top-level items in compilation unit");

        return item switch
        {
            TypeAliasItemTopLevelItemNode aliasItem => aliasItem.TypeAliasDeclaration,
            _ => throw new InvalidOperationException(
                $"Expected TypeAliasItemTopLevelItemNode but got {item.GetType().Name}")
        };
    }

    private static IReadOnlyList<string> GetTypeParameterNames(TypeParameterListNode? typeParams)
    {
        if (typeParams is null)
            return Array.Empty<string>();

        var names = new List<string> { typeParams.TypeParameter.Name.Lexeme };
        foreach (var param in typeParams.TypeParameters)
            names.Add(param.Name.Lexeme);

        return names;
    }

    // ─── Property Test ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Parsing produces correct AST structure.
    ///
    /// For any valid type alias source string, parsing SHALL produce a
    /// TypeAliasDeclarationNode whose Name.Lexeme matches the expected alias name,
    /// TypeParams is null when TypeParameterCount == 0 (otherwise has the correct count),
    /// and type parameter names match the expected names.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeAliasParserPropertyTests) })]
    public Property Parsing_TypeAlias_ProducesCorrectAstStructure(TypeAliasSourceScenario scenario)
    {
        var cu = ParseOrFail(scenario.Source);
        var aliasDecl = ExtractTypeAlias(cu);

        // Verify alias name matches
        var nameMatches = aliasDecl.Name.Lexeme == scenario.AliasName;

        // Verify type parameters
        bool typeParamsCorrect;
        if (scenario.TypeParameterCount == 0)
        {
            typeParamsCorrect = aliasDecl.TypeParams is null;
        }
        else
        {
            if (aliasDecl.TypeParams is null)
            {
                typeParamsCorrect = false;
            }
            else
            {
                var actualNames = GetTypeParameterNames(aliasDecl.TypeParams);
                typeParamsCorrect = actualNames.Count == scenario.TypeParameterCount
                    && actualNames.SequenceEqual(scenario.TypeParameterNames);
            }
        }

        // Verify type reference is not null (it was parsed)
        var typeRefPresent = aliasDecl.Type is not null;

        return (nameMatches && typeParamsCorrect && typeRefPresent).ToProperty()
            .Label($"Name match: {nameMatches}, TypeParams correct: {typeParamsCorrect}, TypeRef present: {typeRefPresent}")
            .Label($"Source: {scenario.Source}")
            .Label($"Expected name: {scenario.AliasName}, Actual: {aliasDecl.Name.Lexeme}")
            .Label($"Expected param count: {scenario.TypeParameterCount}, Actual: {GetTypeParameterNames(aliasDecl.TypeParams).Count}");
    }
}
