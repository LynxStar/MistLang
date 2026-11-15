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

[Trait("Tier", "3")]
public sealed class NestedScopingPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static ParseResult ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        return Parser.Parse(tokens, bag);
    }

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

    private static string BuildNestedSource(AllTypeKind containerKind, AllTypeKind nestedKind)
    {
        var containerKeyword = AllTypeKindKeyword(containerKind);
        var nestedKeyword = AllTypeKindKeyword(nestedKind);

        var nestedBody = nestedKind == AllTypeKind.Enum
            ? "{ A, B }"
            : "{ }";

        return $"{containerKeyword} Container {{\n    {nestedKeyword} Nested {nestedBody}\n}}";
    }

    private static ImmutableArray<MemberDeclarationNode>? GetMembers(TypeDeclarationNode typeDecl)
    {
        return typeDecl switch
        {
            BodyTypeTypeDeclarationNode body => body.TypeBody.Members,
            _ => null,
        };
    }

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

    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ParserNestingUniversality_ProducesNestedTypeMemberNode(
        NestedTypeSource input)
    {
        var result = ParseSource(input.Source);

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

        return HasNestedTypeMember(cu);
    }

    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5**
    [Fact]
    public void ParserNestingUniversality_AllContainerCombinations_ProduceNestedTypeMemberNode()
    {
        var allKinds = new[]
        {
            AllTypeKind.Class,
            AllTypeKind.Struct,
            AllTypeKind.Abstract,
            AllTypeKind.Enum,
        };

        var failures = new List<string>();

        foreach(var containerKind in MistNestedScopingGenerator.ContainerKinds)
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
            $"Failed combinations ({failures.Count}/{MistNestedScopingGenerator.ContainerKinds.Length * allKinds.Length}):\n" + string.Join("\n", failures));
    }

    /// **Validates: Requirements 1.6, 2.3, 5.3**
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool NestingDepthAcceptance_ParserProducesCorrectDepth(NestedTypeDepthSource input)
    {
        var result = ParseSource(input.Source);

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

        var measuredDepth = MeasureNestingDepth(cu);
        return measuredDepth == input.Depth;
    }

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

    /// **Validates: Requirements 1.7**
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

    /// **Validates: Requirements 1.7**
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

    private static bool VerifyNestedTypeModifiers(string source, ModifierSubset expectedMods)
    {
        var result = ParseSource(source);

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

        var members = GetMembers(types[0]);
        if(members is null || members.Value.Length == 0)
        {
            return false;
        }

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

        var expectedCount = MistNestedScopingGenerator.ModifierCount(expectedMods);
        if(actualModifiers.Length != expectedCount)
        {
            return false;
        }

        var expectedTypes = MistNestedScopingGenerator.ExpectedModifierNodeTypes(expectedMods);
        var actualTypes = actualModifiers.Select(m => m.GetType()).ToHashSet();

        return expectedTypes.SetEquals(actualTypes);
    }

    /// Structural identity is verified by comparing the pretty-printed output of
    /// both ASTs: if print(AST1) == print(AST2), the ASTs are structurally identical.
    ///
    /// **Validates: Requirements 1.8, 12.5, 13.3**
    [Property(Arbitrary = new[] { typeof(MistNestedScopingGenerator) }, MaxTest = 100)]
    public bool ParsePrintParseRoundTrip_ProducesStructurallyIdenticalAst(
        RoundTripSource input)
    {
        var result1 = ParseSource(input.Source);
        if(result1.Root is not CompilationUnitNode ast1)
        {
            return false;
        }

        if(result1.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        var printed1 = MistPrettyPrinter.Print(ast1, input.Source);
        if(string.IsNullOrWhiteSpace(printed1))
        {
            return false;
        }

        var result2 = ParseSource(printed1);
        if(result2.Root is not CompilationUnitNode ast2)
        {
            return false;
        }

        if(result2.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return false;
        }

        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        return printed1 == printed2;
    }
}