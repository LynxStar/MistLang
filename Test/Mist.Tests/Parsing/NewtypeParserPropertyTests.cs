// Feature: newtypes, Property 1: Parsing produces correct AST structure
// **Validates: Requirements 1.1**

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
/// Property-based tests for newtype declaration parsing correctness.
///
/// **Validates: Requirements 1.1**
///
/// For any valid identifier and valid type reference, parsing
/// <c>type {id} = new {typeRef};</c> SHALL produce a NewtypeDeclaration AST node
/// whose Name lexeme equals the identifier and whose SourceType matches the type reference.
/// </summary>
public class NewtypeParserPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    public static Arbitrary<NewtypeSourceScenario> ArbitraryNewtypeSourceScenario()
    {
        return MistNewtypeGenerator.ArbitraryNewtypeSourceScenario();
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

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

    private static NewtypeDeclarationNode ExtractNewtypeDeclaration(CompilationUnitNode cu)
    {
        var item = cu.Items.FirstOrDefault()
            ?? throw new InvalidOperationException("No top-level items in compilation unit");

        if(item is TypeItemTopLevelItemNode { TypeDeclaration: NewtypeTypeTypeDeclarationNode newtypeType })
        {
            return newtypeType.NewtypeDeclaration;
        }

        throw new InvalidOperationException(
            $"Expected TypeItemTopLevelItemNode with NewtypeTypeTypeDeclarationNode but got {item.GetType().Name}");
    }

    private static string ExtractTypeReferenceText(TypeReferenceNode typeRef, string source)
    {
        return typeRef.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => EmitPrimitiveType(primitiveRef.PrimitiveType),
            NamedSingleTypeRefSingleTypeRefNode namedRef => ExtractSpanText(namedRef.Name, source),
            _ => throw new InvalidOperationException(
                $"Unexpected TypeReferenceNode variant: {typeRef.First.GetType().Name}")
        };
    }

    private static string ExtractSpanText(Storm.Runtime.SyntaxNode node, string source)
    {
        var span = node.Span;
        if(span.Length > 0 && span.Start + span.Length <= source.Length)
        {
            return source.Substring(span.Start, span.Length);
        }

        return "";
    }

    private static string EmitPrimitiveType(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
        {
            IntTypePrimitiveTypeNode => "int",
            UintTypePrimitiveTypeNode => "uint",
            LongTypePrimitiveTypeNode => "long",
            UlongTypePrimitiveTypeNode => "ulong",
            ShortTypePrimitiveTypeNode => "short",
            UshortTypePrimitiveTypeNode => "ushort",
            ByteTypePrimitiveTypeNode => "byte",
            SbyteTypePrimitiveTypeNode => "sbyte",
            FloatTypePrimitiveTypeNode => "float",
            DoubleTypePrimitiveTypeNode => "double",
            DecimalTypePrimitiveTypeNode => "decimal",
            BoolTypePrimitiveTypeNode => "bool",
            CharTypePrimitiveTypeNode => "char",
            StringTypePrimitiveTypeNode => "string",
            _ => "object",
        };
    }

    // ─── Property Test ──────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Parsing produces correct AST structure.
    ///
    /// For any valid identifier and valid type reference, parsing
    /// <c>type {id} = new {typeRef};</c> SHALL produce a NewtypeDeclaration AST node
    /// whose Name lexeme equals the identifier and whose SourceType matches the type reference.
    ///
    /// **Validates: Requirements 1.1**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(NewtypeParserPropertyTests) })]
    public Property Parsing_Newtype_ProducesCorrectAstStructure(NewtypeSourceScenario scenario)
    {
        var cu = ParseOrFail(scenario.Source);
        var newtypeDecl = ExtractNewtypeDeclaration(cu);

        // Verify newtype name matches
        var nameMatches = newtypeDecl.Name.Lexeme == scenario.NewtypeName;

        // Verify source type matches
        var sourceTypeText = ExtractTypeReferenceText(newtypeDecl.SourceType, scenario.Source);
        var sourceTypeMatches = sourceTypeText == scenario.SourceTypeText;

        return (nameMatches && sourceTypeMatches).ToProperty()
            .Label($"Name match: {nameMatches}, SourceType match: {sourceTypeMatches}")
            .Label($"Source: {scenario.Source}")
            .Label($"Expected name: {scenario.NewtypeName}, Actual: {newtypeDecl.Name.Lexeme}")
            .Label($"Expected sourceType: {scenario.SourceTypeText}, Actual: {sourceTypeText}");
    }
}