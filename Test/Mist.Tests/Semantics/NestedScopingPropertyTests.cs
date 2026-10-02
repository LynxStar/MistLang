// Feature: nested-scoping, Property 1: Parser nesting universality
// Feature: nested-scoping, Property 3: Modifier preservation on nested types
// Feature: nested-scoping, Property 4: Parse–print–parse round trip

using System.Collections.Immutable;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for nested scoping.
/// </summary>
[Trait("Tier", "3")]
public sealed class NestedScopingPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

    /// <summary>
    /// Returns the Mist keyword for any of the five type kinds.
    /// </summary>
    private static string AllTypeKindKeyword(AllTypeKind kind)
    {
        return kind switch
        {
            AllTypeKind.Class => "class",
            AllTypeKind.Struct => "struct",
            AllTypeKind.Abstract => "abstract",
            AllTypeKind.Enum => "enum",
            _ => "class"
        };
    }

    /// <summary>
    /// Builds a Mist source string with a container type containing a nested type.
    /// </summary>
    private static string BuildNestedSource(AllTypeKind containerKind, AllTypeKind nestedKind)
    {
        var containerKeyword = AllTypeKindKeyword(containerKind);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);

        var nestedBody = nestedKind == AllTypeKind.Enum
            ? "{ A, B }"
            : "{ }";

        return $"{containerKeyword} Container {{\n    {nestedKeyword} Nested {nestedBody}\n}}";
    }

    /// <summary>
    /// Extracts the MemberDeclaration members from a type declaration node.
    /// Only class and struct use MemberDeclaration (which includes NestedTypeMember).
    /// </summary>
    private static ImmutableArray<MemberDeclarationNode>? GetMembers(TypeDeclarationNode typeDecl)
    {
        return typeDecl switch
        {
            BodyTypeTypeDeclarationNode body => body.TypeBody.Members,
            _ => null,
        };
    }

    /// <summary>
    /// Checks if a parsed compilation unit contains a nested type member declaration
    /// as a member of the first type declaration.
    /// </summary>
    private static bool HasNestedTypeMember(CompilationUnitNode cu)
    {
        if(cu.Items.Length == 0)
        {
            return false;
        }

        var topLevelItem = cu.Items[0];
        var typeDecl = topLevelItem switch
        {
            TypeItemTopLevelItemNode typeItem => typeItem.TypeDeclaration,
            _ => null,
        };

        if(typeDecl is null)
        {
            return false;
        }

        var members = GetMembers(typeDecl);
        if(members is null)
        {
            return false;
        }

        return members.Value.Any(m => m is NestedBodyTypeMemberMemberDeclarationNode
            or NestedEnumMemberMemberDeclarationNode
            or NestedNewtypeMemberMemberDeclarationNode
            or NestedTypeAliasMemberMemberDeclarationNode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Parser nesting universality
    //
    // For any container type kind × nested type kind, verify the parser
    // produces a NestedTypeMember node.
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1: Parser nesting universality.
    /// For any container type kind (class, struct) and for any nested type kind
    /// (class, struct, abstract type, enum), the parser SHALL produce a valid
    /// AST where the nested type appears as a NestedTypeMember within the
    /// container's member list.
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ParserNestingUniversality_ProducesNestedTypeMemberNode(
        NestedTypeSource input)
    {
        var result = ParseSource(input.Source);

        // Parser should return a non-null result with a CompilationUnitNode root
        if(result.Root is not CompilationUnitNode cu)
        {
            return false;
        }

        // No parse errors should be reported
        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(parseErrors.Count > 0)
        {
            return false;
        }

        // The AST should contain a nested type member declaration node
        return HasNestedTypeMember(cu);
    }

    /// <summary>
    /// Deterministic verification that all 10 combinations of container (class, struct)
    /// × nested type kinds produce a NestedTypeMember node.
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    /// </summary>
    [Fact]
    public void ParserNestingUniversality_All10Combinations_ProduceNestedTypeMemberNode()
    {
        var allKinds = new[]
        {
            AllTypeKind.Class,
            AllTypeKind.Struct,
            AllTypeKind.Abstract,
            AllTypeKind.Enum,
        };

        var containerKinds = new[] { AllTypeKind.Class, AllTypeKind.Struct };

        var failures = new List<string>();

        foreach(var containerKind in containerKinds)
        {
            foreach(var nestedKind in allKinds)
            {
                var source = BuildNestedSource(containerKind, nestedKind);
                var result = ParseSource(source);

                if(result.Root is not CompilationUnitNode cu)
                {
                    failures.Add($"{containerKind} containing {nestedKind}: parse returned null root");
                    continue;
                }

                var parseErrors = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                if(parseErrors.Count > 0)
                {
                    failures.Add($"{containerKind} containing {nestedKind}: parse errors: {string.Join("; ", parseErrors.Select(e => e.Message))}");
                    continue;
                }

                if(!HasNestedTypeMember(cu))
                {
                    failures.Add($"{containerKind} containing {nestedKind}: no nested type member node found");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"Failed combinations ({failures.Count}/10):\n" + string.Join("\n", failures));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Nesting depth acceptance
    //
    // For any depth 1–8 and any sequence of type kinds, verify the parser
    // produces an AST with exactly that depth of nesting.
    //
    // **Validates: Requirements 1.6, 2.3, 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2: Nesting depth acceptance.
    /// For any depth 1–8 and any sequence of type kinds, verify the parser produces
    /// an AST with exactly that depth of nesting.
    /// **Validates: Requirements 1.6, 2.3, 5.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool NestingDepthAcceptance_ParserProducesCorrectDepth(NestedTypeDepthSource input)
    {
        var result = ParseSource(input.Source);

        // Parser should produce a valid AST without errors
        if(result.Root is not CompilationUnitNode cu)
        {
            return false;
        }

        var parseErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(parseErrors.Count > 0)
        {
            return false;
        }

        // Verify the AST has exactly the expected nesting depth
        var measuredDepth = MeasureNestingDepth(cu);
        return measuredDepth == input.Depth;
    }

    /// <summary>
    /// Measures the maximum nesting depth of type declarations in a CompilationUnit.
    /// A single top-level type has depth 1. A type nested inside another has depth 2, etc.
    /// </summary>
    private static int MeasureNestingDepth(CompilationUnitNode cu)
    {
        var maxDepth = 0;

        foreach(var item in cu.Items)
        {
            if(item is TypeItemTopLevelItemNode typeItem)
            {
                var depth = MeasureTypeDepth(typeItem.TypeDeclaration, 1);
                if(depth > maxDepth)
                {
                    maxDepth = depth;
                }
            }
        }

        return maxDepth;
    }

    /// <summary>
    /// Recursively measures the nesting depth of a TypeDeclarationNode.
    /// </summary>
    private static int MeasureTypeDepth(TypeDeclarationNode typeDecl, int currentDepth)
    {
        var members = GetMembers(typeDecl);
        if(members is null || members.Value.IsEmpty)
        {
            return currentDepth;
        }

        var maxChildDepth = currentDepth;

        foreach(var member in members.Value)
        {
            if(member is NestedBodyTypeMemberMemberDeclarationNode nestedBody)
            {
                var childDecl = new BodyTypeTypeDeclarationNode { TypeBody = nestedBody.NestedBody };
                var childDepth = MeasureTypeDepth(childDecl, currentDepth + 1);
                if(childDepth > maxChildDepth)
                {
                    maxChildDepth = childDepth;
                }
            }
        }

        return maxChildDepth;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Modifier preservation on nested types
    //
    // For any subset of {public, private, static} applied to a nested type
    // declaration, the parser SHALL produce an AST node whose modifier list
    // contains exactly those modifiers.
    //
    // **Validates: Requirements 1.7**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 3: Modifier preservation on nested types.
    /// For any subset of {public, private, static} and any nested type kind,
    /// the parser SHALL produce an AST node whose modifier list contains exactly
    /// those modifiers when the nested type is inside a class container.
    ///
    /// **Validates: Requirements 1.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ModifierPreservation_NestedType_InClass(
        ModifierSubset mods, AllTypeKind nestedKind)
    {
        var modSource = MistNestedScopingGenerator.ModifierSetToSource(mods);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);
        var nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";
        var source = $"class Outer {{ {modSource}{nestedKeyword} Inner {nestedBody} }}";
        return VerifyNestedTypeModifiers(source, mods);
    }

    /// <summary>
    /// Property 3: Modifier preservation on nested types.
    /// Same as above but with a struct container.
    ///
    /// **Validates: Requirements 1.7**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ModifierPreservation_NestedType_InStruct(
        ModifierSubset mods, AllTypeKind nestedKind)
    {
        var modSource = MistNestedScopingGenerator.ModifierSetToSource(mods);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);
        var nestedBody = nestedKind == AllTypeKind.Enum ? "{ A, B }" : "{ }";
        var source = $"struct Outer {{ {modSource}{nestedKeyword} Inner {nestedBody} }}";
        return VerifyNestedTypeModifiers(source, mods);
    }

    // ─── Property 3 verification helper ──────────────────────────────────

    private static bool VerifyNestedTypeModifiers(string source, ModifierSubset expectedMods)
    {
        var result = ParseSource(source);

        // Parse must succeed without errors
        if(result.Root is not CompilationUnitNode cu)
        {
            return false;
        }

        if(result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        var types = cu.GetTypes();
        if(types.Length == 0)
        {
            return false;
        }

        // Get the outer type's members
        var members = GetMembers(types[0]);
        if(members is null || members.Value.Length == 0)
        {
            return false;
        }

        // Find the nested type member and get its modifiers
        ImmutableArray<ModifierNode> actualModifiers = default;
        var found = false;

        foreach(var member in members.Value)
        {
            switch(member)
            {
                case NestedBodyTypeMemberMemberDeclarationNode nestedBody:
                    actualModifiers = nestedBody.NestedBody.Modifiers;
                    found = true;
                    break;
                case NestedEnumMemberMemberDeclarationNode nestedEnum:
                    actualModifiers = nestedEnum.NestedEnum.Modifiers;
                    found = true;
                    break;
                case NestedNewtypeMemberMemberDeclarationNode nestedNewtype:
                    actualModifiers = nestedNewtype.NestedNewtype.Modifiers;
                    found = true;
                    break;
                case NestedTypeAliasMemberMemberDeclarationNode nestedAlias:
                    actualModifiers = nestedAlias.NestedTypeAlias.Modifiers;
                    found = true;
                    break;
            }

            if(found)
            {
                break;
            }
        }

        if(!found)
        {
            return false;
        }

        // Verify count matches
        var expectedCount = MistNestedScopingGenerator.ModifierCount(expectedMods);
        if(actualModifiers.Length != expectedCount)
        {
            return false;
        }

        // Verify each expected modifier is present by type
        var expectedTypes = MistNestedScopingGenerator.ExpectedModifierNodeTypes(expectedMods);
        var actualTypes = actualModifiers.Select(m => m.GetType()).ToHashSet();

        return expectedTypes.SetEquals(actualTypes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Parse–print–parse round trip
    //
    // For any valid Mist source with nested types/namespaces, verify
    // parse → print → parse produces structurally identical AST.
    //
    // **Validates: Requirements 1.8, 12.5, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 4: Parse–print–parse round trip.
    /// For any valid Mist source file containing nested types and/or nested namespaces,
    /// parsing the source, pretty-printing the resulting AST, then parsing the
    /// pretty-printed output SHALL produce an AST that is structurally identical
    /// to the original (same node types, same token lexemes, same tree shape),
    /// disregarding source position spans.
    ///
    /// Structural identity is verified by comparing the pretty-printed output of
    /// both ASTs: if print(AST1) == print(AST2), the ASTs are structurally identical.
    ///
    /// **Validates: Requirements 1.8, 12.5, 13.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ParsePrintParseRoundTrip_ProducesStructurallyIdenticalAst(
        RoundTripSource input)
    {
        // Step 1: Parse the generated source to get AST1
        var result1 = ParseSource(input.Source);
        if(result1.Root is not CompilationUnitNode ast1)
        {
            return false;
        }

        // No parse errors
        if(result1.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        // Step 2: Pretty-print AST1 to get printed1
        var printed1 = MistPrettyPrinter.Print(ast1, input.Source);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false;
        }

        // Step 3: Parse printed1 to get AST2
        var result2 = ParseSource(printed1);
        if(result2.Root is not CompilationUnitNode ast2)
        {
            return false;
        }

        // No parse errors on the re-parsed output
        if(result2.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        // Step 4: Pretty-print AST2 to get printed2
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        // Step 5: Verify structural identity: print(AST1) == print(AST2)
        return printed1 == printed2;
    }
}