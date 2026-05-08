// Feature: generic-traits, Property 1: Generic trait and inheritance syntax round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 6.1, 6.2, 6.3, 7.1, 7.2, 7.5, 8.1, 8.2, 9.1, 9.2, 9.3, 9.4, 9.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Parsing;
using Mist.Tests.Generators;

namespace Mist.Tests.Parsing;

/// <summary>
/// Property-based tests for generic trait and inheritance syntax round-trip.
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6,
///              6.1, 6.2, 6.3, 7.1, 7.2, 7.5, 8.1, 8.2, 9.1, 9.2, 9.3, 9.4, 9.5**
///
/// For any valid Mist AST containing trait declarations with varying combinations of
/// type parameter lists (0, 1, 2+ parameters), inheritance clauses (0, 1, 2+ parent
/// traits, with and without type arguments), trait member signatures referencing type
/// parameters, with blocks referencing generic traits with type argument lists, and
/// constructed trait type references (including nested generics like Comparable&lt;List&lt;int&gt;&gt;),
/// pretty-printing the AST and then parsing the result and pretty-printing again
/// SHALL produce identical output.
/// </summary>
public class GenericTraitRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Token helper ────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] TraitNames =
        ["Comparable", "Converter", "Sortable", "Mappable", "Reducible", "Filterable"];

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W"];

    private static readonly string[] FieldNames =
        ["value", "count", "size", "weight", "length"];

    private static readonly string[] MethodNames =
        ["CompareTo", "Convert", "GetValue", "Transform", "Process"];

    private static readonly string[] ParamNames =
        ["other", "input", "item", "arg"];

    private static readonly string[] TypeNames =
        ["MyType", "Product", "Widget", "Record", "Entry"];

    private static readonly string[] MappingSourceNames =
        ["price", "name", "label", "score", "rating"];

    // ═══════════════════════════════════════════════════════════════════════
    // TypeParameterList generator (reused from GenericRoundTripPropertyTests pattern)
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<TypeParameterListNode> GenTypeParameterList(int count)
    {
        var names = TypeParamNames.Take(count).ToArray();
        var first = new RegularTypeParameterTypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();

        return Gen.Constant(new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        });
    }

    private static Gen<TypeParameterListNode> GenTypeParameterList()
    {
        return Gen.Choose(1, 3).SelectMany(GenTypeParameterList);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TypeArgumentList generators
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<TypeArgumentListNode> GenTypeArgumentList(int count)
    {
        return Gen.ArrayOf(MistTypeGenerator.GenPrimitiveTypeRef(), count)
            .Select(args => new TypeArgumentListNode
            {
                TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = args[0] },
                TypeArguments = args.Skip(1).Select(t => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = t }).ToImmutableArray(),
            });
    }

    private static Gen<TypeArgumentListNode> GenSimpleTypeArgumentList()
    {
        return Gen.Choose(1, 3).SelectMany(GenTypeArgumentList);
    }

    /// <summary>
    /// Generates a type argument list that may include nested generics like List&lt;int&gt;.
    /// </summary>
    private static Gen<TypeArgumentListNode> GenNestedTypeArgumentList()
    {
        // Generate a NamedTypeRef with its own type arguments (nested generic)
        var nestedTypeRef = from typeName in Gen.Elements("List", "Set", "Map")
                           from innerType in MistTypeGenerator.GenPrimitiveTypeRef()
                           select (TypeReferenceNode)new NamedTypeRefTypeReferenceNode
                           {
                               Name = new QualifiedIdentifierNode
                               {
                                   IDENTIFIER = Tok(typeName),
                                   IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
                               },
                               TypeArgs = new TypeArgumentListNode
                               {
                                   TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = innerType },
                                   TypeArguments = ImmutableArray<TypeArgumentNode>.Empty,
                               },
                           };

        return from nested in nestedTypeRef
               select new TypeArgumentListNode
               {
                   TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = nested },
                   TypeArguments = ImmutableArray<TypeArgumentNode>.Empty,
               };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BaseList generators (for trait inheritance clauses)
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<BaseTypeRefNode> GenBaseTypeRef()
    {
        return from name in Gen.Elements(TraitNames)
               from hasTypeArgs in Gen.Elements(true, false)
               from typeArgs in hasTypeArgs
                   ? GenSimpleTypeArgumentList().Select(a => (TypeArgumentListNode?)a)
                   : Gen.Constant((TypeArgumentListNode?)null)
               select new BaseTypeRefNode { Name = Tok(name), TypeArgs = typeArgs };
    }

    private static Gen<BaseListNode> GenSingleParentBaseList()
    {
        return GenBaseTypeRef().Select(entry => new BaseListNode
        {
            First = entry,
            Rest = entry,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
        });
    }

    private static Gen<BaseListNode> GenMultiParentBaseList()
    {
        return from first in GenBaseTypeRef()
               from second in GenBaseTypeRef()
               select new BaseListNode
               {
                   First = first,
                   Rest = first,
                   BaseTypeRefs = ImmutableArray.Create(second),
               };
    }

    private static Gen<BaseListNode?> GenOptionalParentList()
    {
        return Gen.Frequency(
            (2, Gen.Constant((BaseListNode?)null)),
            (2, GenSingleParentBaseList().Select(b => (BaseListNode?)b)),
            (1, GenMultiParentBaseList().Select(b => (BaseListNode?)b)));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Trait member generators (with type parameter references)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a type reference that may be a type parameter name (T, U, etc.)
    /// or a primitive type.
    /// </summary>
    private static Gen<TypeReferenceNode> GenTypeRefWithParams(string[] availableTypeParams)
    {
        if (availableTypeParams.Length == 0)
            return MistTypeGenerator.GenPrimitiveTypeRef();

        var typeParamRef = Gen.Elements(availableTypeParams)
            .Select(name => (TypeReferenceNode)new NamedTypeRefTypeReferenceNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = Tok(name),
                    IDENTIFIERs = ImmutableArray<Storm.Runtime.Token>.Empty,
                },
            });

        return Gen.OneOf(MistTypeGenerator.GenPrimitiveTypeRef(), typeParamRef);
    }

    private static Gen<TraitMemberDeclarationNode> GenTraitFieldMember(string[] typeParams)
    {
        return from type in GenTypeRefWithParams(typeParams)
               from name in Gen.Elements(FieldNames)
               select (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode
               {
                   Field = new TraitFieldDeclarationNode
                   {
                       Type = type,
                       Name = Tok(name),
                   },
               };
    }

    private static Gen<TraitMemberDeclarationNode> GenTraitMethodMember(string[] typeParams)
    {
        return from returnType in GenTypeRefWithParams(typeParams)
               from name in Gen.Elements(MethodNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in GenTypeRefWithParams(typeParams)
               from paramName in Gen.Elements(ParamNames)
               select (TraitMemberDeclarationNode)new TraitMethodMemberTraitMemberDeclarationNode
               {
                   Method = new TraitMethodDeclarationNode
                   {
                       ReturnType = returnType,
                       Name = Tok(name),
                       Parameters = hasParam
                           ? new ParameterListNode
                           {
                               Parameter = new ParameterNode
                               {
                                   Type = paramType,
                                   Name = Tok(paramName),
                               },
                           }
                           : null,
                   },
               };
    }

    private static Gen<TraitMemberDeclarationNode> GenTraitMember(string[] typeParams)
    {
        return Gen.OneOf(
            GenTraitFieldMember(typeParams),
            GenTraitMethodMember(typeParams));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Trait declaration generators (with type params and inheritance)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a trait with no type parameters and no inheritance.
    /// Example: trait Foo { int X; }
    /// </summary>
    private static Gen<TraitDeclarationNode> GenNonGenericTraitNoInheritance()
    {
        return from name in Gen.Elements(TraitNames)
               from memberCount in Gen.Choose(1, 3)
               from members in Gen.ArrayOf(GenTraitMember([]), memberCount)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   Members = members.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait with a single type parameter.
    /// Example: trait Foo&lt;T&gt; { T X; }
    /// </summary>
    private static Gen<TraitDeclarationNode> GenSingleTypeParamTrait()
    {
        return from name in Gen.Elements(TraitNames)
               from typeParams in GenTypeParameterList(1)
               from memberCount in Gen.Choose(1, 3)
               from members in Gen.ArrayOf(GenTraitMember(["T"]), memberCount)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   TypeParams = typeParams,
                   Members = members.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait with multiple type parameters.
    /// Example: trait Foo&lt;T, U&gt; { T X; U Y; }
    /// </summary>
    private static Gen<TraitDeclarationNode> GenMultiTypeParamTrait()
    {
        return from name in Gen.Elements(TraitNames)
               from count in Gen.Choose(2, 3)
               from typeParams in GenTypeParameterList(count)
               let paramNames = TypeParamNames.Take(count).ToArray()
               from memberCount in Gen.Choose(1, 3)
               from members in Gen.ArrayOf(GenTraitMember(paramNames), memberCount)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   TypeParams = typeParams,
                   Members = members.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait with inheritance (with or without type params).
    /// Example: trait Foo&lt;T&gt; : Bar&lt;T&gt; { ... }
    /// </summary>
    private static Gen<TraitDeclarationNode> GenTraitWithInheritance()
    {
        return from name in Gen.Elements(TraitNames)
               from hasTypeParams in Gen.Elements(true, false)
               from typeParams in hasTypeParams
                   ? GenTypeParameterList().Select(tp => (TypeParameterListNode?)tp)
                   : Gen.Constant((TypeParameterListNode?)null)
               let paramNames = hasTypeParams
                   ? TypeParamNames.Take(
                       typeParams!.TypeParameters.Length + 1).ToArray()
                   : Array.Empty<string>()
               from parents in Gen.OneOf(GenSingleParentBaseList(), GenMultiParentBaseList())
               from memberCount in Gen.Choose(0, 2)
               from members in Gen.ArrayOf(GenTraitMember(paramNames), memberCount)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   TypeParams = typeParams,
                   Parents = parents,
                   Members = members.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait with multiple non-generic parents.
    /// Example: trait Foo : Bar, Baz { ... }
    /// </summary>
    private static Gen<TraitDeclarationNode> GenTraitWithMultipleNonGenericParents()
    {
        return from name in Gen.Elements(TraitNames)
               from parent1 in Gen.Elements("Bar", "Baz", "Qux")
               from parent2 in Gen.Elements("Alpha", "Beta", "Gamma")
               from memberCount in Gen.Choose(0, 2)
               from members in Gen.ArrayOf(GenTraitMember([]), memberCount)
               let baseList = new BaseListNode
               {
                   First = new BaseTypeRefNode { Name = Tok(parent1) },
                   Rest = new BaseTypeRefNode { Name = Tok(parent1) },
                   BaseTypeRefs = ImmutableArray.Create(
                       new BaseTypeRefNode { Name = Tok(parent2) }),
               }
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   Parents = baseList,
                   Members = members.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait with multiple generic parents.
    /// Example: trait Foo&lt;T&gt; : Bar&lt;T&gt;, Baz&lt;T&gt; { ... }
    /// </summary>
    private static Gen<TraitDeclarationNode> GenTraitWithMultipleGenericParents()
    {
        return from name in Gen.Elements(TraitNames)
               from typeParams in GenTypeParameterList(1)
               from parent1Name in Gen.Elements("Bar", "Baz", "Qux")
               from parent2Name in Gen.Elements("Alpha", "Beta", "Gamma")
               from parent1TypeArgs in GenSimpleTypeArgumentList()
               from parent2TypeArgs in GenSimpleTypeArgumentList()
               from memberCount in Gen.Choose(0, 2)
               from members in Gen.ArrayOf(GenTraitMember(["T"]), memberCount)
               let baseList = new BaseListNode
               {
                   First = new BaseTypeRefNode { Name = Tok(parent1Name), TypeArgs = parent1TypeArgs },
                   Rest = new BaseTypeRefNode { Name = Tok(parent1Name), TypeArgs = parent1TypeArgs },
                   BaseTypeRefs = ImmutableArray.Create(
                       new BaseTypeRefNode { Name = Tok(parent2Name), TypeArgs = parent2TypeArgs }),
               }
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   TypeParams = typeParams,
                   Parents = baseList,
                   Members = members.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates any kind of generic trait declaration.
    /// </summary>
    private static Gen<TraitDeclarationNode> GenGenericTraitDeclaration()
    {
        return Gen.OneOf(
            GenNonGenericTraitNoInheritance(),
            GenSingleTypeParamTrait(),
            GenMultiTypeParamTrait(),
            GenTraitWithInheritance(),
            GenTraitWithMultipleNonGenericParents(),
            GenTraitWithMultipleGenericParents());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // With declaration generators (with type arguments on trait reference)
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<MemberMappingNode> GenMemberMapping()
    {
        return from sourceName in Gen.Elements(MappingSourceNames)
               from targetName in Gen.Elements(FieldNames.Concat(MethodNames).ToArray())
               select new MemberMappingNode
               {
                   SourceName = Tok(sourceName),
                   TargetName = Tok(targetName),
               };
    }

    /// <summary>
    /// Generates a with block with type arguments on the trait reference.
    /// Example: with MyType : Comparable&lt;int&gt; { x as y; }
    /// </summary>
    private static Gen<WithDeclarationNode> GenWithDeclarationWithTypeArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from traitName in Gen.Elements(TraitNames)
               from typeArgs in GenSimpleTypeArgumentList()
               from mappings in GenMemberMapping().ListOf()
                   .Where(list => list.Count > 0 && list.Count <= 3)
               select new WithDeclarationNode
               {
                   TypeName = Tok(typeName),
                   TraitName = Tok(traitName),
                   TraitTypeArgs = typeArgs,
                   Mappings = mappings.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a with block with nested generic type arguments.
    /// Example: with MyType : Comparable&lt;List&lt;int&gt;&gt; { x as y; }
    /// </summary>
    private static Gen<WithDeclarationNode> GenWithDeclarationWithNestedTypeArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from traitName in Gen.Elements(TraitNames)
               from typeArgs in GenNestedTypeArgumentList()
               from mappings in GenMemberMapping().ListOf()
                   .Where(list => list.Count > 0 && list.Count <= 3)
               select new WithDeclarationNode
               {
                   TypeName = Tok(typeName),
                   TraitName = Tok(traitName),
                   TraitTypeArgs = typeArgs,
                   Mappings = mappings.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a with block without type arguments (non-generic trait reference).
    /// Example: with MyType : Printable { x as y; }
    /// </summary>
    private static Gen<WithDeclarationNode> GenWithDeclarationNoTypeArgs()
    {
        return from typeName in Gen.Elements(TypeNames)
               from traitName in Gen.Elements(TraitNames)
               from mappings in GenMemberMapping().ListOf()
                   .Where(list => list.Count > 0 && list.Count <= 3)
               select new WithDeclarationNode
               {
                   TypeName = Tok(typeName),
                   TraitName = Tok(traitName),
                   Mappings = mappings.ToImmutableArray(),
               };
    }

    private static Gen<WithDeclarationNode> GenWithDeclaration()
    {
        return Gen.OneOf(
            GenWithDeclarationWithTypeArgs(),
            GenWithDeclarationWithNestedTypeArgs(),
            GenWithDeclarationNoTypeArgs());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // CompilationUnit generators (complete programs)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a program with a single generic trait declaration.
    /// </summary>
    private static Gen<CompilationUnitNode> GenSingleGenericTrait()
    {
        return GenGenericTraitDeclaration().Select(traitDecl =>
            new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TopLevelItemNode>(
                    new TypeItemTopLevelItemNode
                    {
                        TypeDeclaration = new TraitTypeTypeDeclarationNode
                        {
                            TraitDeclaration = traitDecl,
                        },
                    }),
            });
    }

    /// <summary>
    /// Generates a program with a generic trait and a with block.
    /// </summary>
    private static Gen<CompilationUnitNode> GenGenericTraitWithWithBlock()
    {
        return from traitDecl in GenGenericTraitDeclaration()
               from withDecl in GenWithDeclaration()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new TraitTypeTypeDeclarationNode
                           {
                               TraitDeclaration = traitDecl,
                           },
                       },
                       new WithItemTopLevelItemNode
                       {
                           WithDeclaration = withDecl,
                       }),
               };
    }

    /// <summary>
    /// Generates a program with multiple generic traits (testing inheritance references).
    /// </summary>
    private static Gen<CompilationUnitNode> GenMultipleGenericTraits()
    {
        return from traits in GenGenericTraitDeclaration().ListOf()
                   .Where(list => list.Count >= 2 && list.Count <= 3)
               select new CompilationUnitNode
               {
                   Items = traits.Select(t =>
                       (TopLevelItemNode)new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new TraitTypeTypeDeclarationNode
                           {
                               TraitDeclaration = t,
                           },
                       }).ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a program with a generic trait, a with block with type args,
    /// and a with block with nested generics.
    /// </summary>
    private static Gen<CompilationUnitNode> GenTraitWithMultipleWithBlocks()
    {
        return from traitDecl in GenGenericTraitDeclaration()
               from withDecl1 in GenWithDeclarationWithTypeArgs()
               from withDecl2 in GenWithDeclarationWithNestedTypeArgs()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new TraitTypeTypeDeclarationNode
                           {
                               TraitDeclaration = traitDecl,
                           },
                       },
                       new WithItemTopLevelItemNode
                       {
                           WithDeclaration = withDecl1,
                       },
                       new WithItemTopLevelItemNode
                       {
                           WithDeclaration = withDecl2,
                       }),
               };
    }

    /// <summary>
    /// Generates a random program with generic trait-related declarations.
    /// </summary>
    public static Gen<CompilationUnitNode> GenGenericTraitProgram()
    {
        return Gen.OneOf(
            GenSingleGenericTrait(),
            GenGenericTraitWithWithBlock(),
            GenMultipleGenericTraits(),
            GenTraitWithMultipleWithBlocks());
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<CompilationUnitNode> ArbitraryCompilationUnitNode() =>
        GenGenericTraitProgram().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Generic trait and inheritance syntax round-trip
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 2.2, 2.3,
    //              2.4, 2.5, 2.6, 6.1, 6.2, 6.3, 7.1, 7.2, 7.5, 8.1, 8.2,
    //              9.1, 9.2, 9.3, 9.4, 9.5**
    //
    // For any valid Mist AST containing generic trait declarations and with blocks,
    // pretty-printing → parsing → pretty-printing SHALL produce identical output.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(GenericTraitRoundTripPropertyTests) })]
    public Property RoundTrip_PrintParsePrintPreservesGenericTraitSyntax(CompilationUnitNode ast)
    {
        // Step 1: Pretty-print the generated AST to source text
        var printed1 = MistPrettyPrinter.Print(ast);
        if (string.IsNullOrWhiteSpace(printed1))
            return false.ToProperty().Label("printed1 was empty");

        // Step 2: Parse the printed source text back to an AST
        var sourceText = new Mist.Common.Text.SourceText("test.mist", printed1);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
            return false.ToProperty()
                .Label($"Parse errors: {string.Join("; ", errors.Select(e => e.Message))}\nSource:\n{printed1}");

        var reparsed = result.Root as CompilationUnitNode;
        if (reparsed is null)
            return false.ToProperty().Label($"Reparsed root was null.\nSource:\n{printed1}");

        // Step 3: Pretty-print the reparsed AST
        var printed2 = MistPrettyPrinter.Print(reparsed, printed1);

        // Step 4: Compare the two printed outputs — they must be identical
        return (printed1 == printed2).ToProperty()
            .Label($"Round-trip mismatch.\nPrinted1:\n{printed1}\nPrinted2:\n{printed2}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(GenericTraitRoundTripPropertyTests) })]
    public Property RoundTrip_PrintedOutputContainsTraitSyntax(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);

        // The output should contain at least one trait or with-related keyword
        var containsTraitSyntax =
            printed.Contains("trait ") ||
            printed.Contains("with ");

        return containsTraitSyntax.ToProperty()
            .Label($"No trait or with syntax found in output:\n{printed}");
    }
}
