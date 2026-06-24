// Generators for Mist trait declaration and with-block AST nodes.
// Produces random but structurally valid trait declarations and with blocks for property-based testing.
// Feature: traits, Property 1: Trait and with-block declaration round-trip
// **Validates: Requirements 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using Mist.Generated;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generators for Mist trait declaration and with-block AST nodes.
/// Generates random trait declarations with varying field, method, and property members,
/// and with blocks with varying member mappings (including same-name mappings).
/// </summary>
public static class MistTraitGenerator
{
    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ═══════════════════════════════════════════════════════════════════════
    // Name pools
    // ═══════════════════════════════════════════════════════════════════════

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable", "Readable", "Writable"];

    private static readonly string[] FieldNames =
        ["value", "count", "size", "width", "height", "length", "depth", "weight"];

    private static readonly string[] MethodNames =
        ["Compute", "Process", "Execute", "Transform", "Validate", "Render", "Serialize"];

    private static readonly string[] PropertyNames =
        ["Name", "Label", "Title", "Status", "Score", "Rating", "Level"];

    private static readonly string[] ParamNames =
        ["input", "data", "item", "arg", "param"];

    private static readonly string[] TypeNames =
        ["Widget", "Item", "Record", "Entry", "Node", "Element", "Config"];

    private static readonly string[] MappingSourceNames =
        ["myValue", "myCount", "mySize", "localWidth", "localHeight", "internalLen"];

    // ═══════════════════════════════════════════════════════════════════════
    // Trait field member generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a trait field declaration: type name;
    /// </summary>
    public static Gen<TraitFieldDeclarationNode> GenTraitFieldDeclaration()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from name in Gen.Elements(FieldNames)
               select new TraitFieldDeclarationNode
               {
                   Type = type,
                   Name = Tok(name),
               };
    }

    /// <summary>
    /// Generates a trait field member declaration node.
    /// </summary>
    public static Gen<TraitMemberDeclarationNode> GenTraitFieldMember()
    {
        return GenTraitFieldDeclaration().Select(f =>
            (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode { Field = f });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Trait method member generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a trait method declaration: returnType name(parameters);
    /// </summary>
    public static Gen<TraitMethodDeclarationNode> GenTraitMethodDeclaration()
    {
        return from returnType in MistTypeGenerator.GenReturnTypeRef()
               from name in Gen.Elements(MethodNames)
               from hasParams in Gen.Elements(true, false)
               from paramType in MistTypeGenerator.GenPrimitiveTypeRef()
               from paramName in Gen.Elements(ParamNames)
               select new TraitMethodDeclarationNode
               {
                   ReturnType = returnType,
                   Name = Tok(name),
                   Parameters = hasParams
                       ? new ParameterListNode
                       {
                           Parameter = new ParameterNode
                           {
                               Type = paramType,
                               Name = Tok(paramName),
                           },
                       }
                       : null,
               };
    }

    /// <summary>
    /// Generates a trait method member declaration node.
    /// </summary>
    public static Gen<TraitMemberDeclarationNode> GenTraitMethodMember()
    {
        return GenTraitMethodDeclaration().Select(m =>
            (TraitMemberDeclarationNode)new TraitMethodMemberTraitMemberDeclarationNode { Method = m });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Trait property member generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a trait property declaration: type name { get; set; }
    /// Uses InterfaceAccessorDeclaration (get/set) as specified by the grammar.
    /// </summary>
    public static Gen<TraitPropertyDeclarationNode> GenTraitPropertyDeclaration()
    {
        var getOnly = Gen.Constant(ImmutableArray.Create<AbstractAccessorDeclarationNode>(
            new AbstractGetAccessorAbstractAccessorDeclarationNode()));

        var getSet = Gen.Constant(ImmutableArray.Create<AbstractAccessorDeclarationNode>(
            new AbstractGetAccessorAbstractAccessorDeclarationNode(),
            new AbstractSetAccessorAbstractAccessorDeclarationNode()));

        var setOnly = Gen.Constant(ImmutableArray.Create<AbstractAccessorDeclarationNode>(
            new AbstractSetAccessorAbstractAccessorDeclarationNode()));

        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from name in Gen.Elements(PropertyNames)
               from accessors in Gen.OneOf(getOnly, getSet, setOnly)
               select new TraitPropertyDeclarationNode
               {
                   Type = type,
                   Name = Tok(name),
                   Accessors = accessors,
               };
    }

    /// <summary>
    /// Generates a trait property member declaration node.
    /// </summary>
    public static Gen<TraitMemberDeclarationNode> GenTraitPropertyMember()
    {
        return GenTraitPropertyDeclaration().Select(p =>
            (TraitMemberDeclarationNode)new TraitPropertyMemberTraitMemberDeclarationNode { Property = p });
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Trait member generators (any kind)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a random trait member declaration (field, method, or property).
    /// </summary>
    public static Gen<TraitMemberDeclarationNode> GenTraitMember()
    {
        return Gen.OneOf(
            GenTraitFieldMember(),
            GenTraitMethodMember(),
            GenTraitPropertyMember());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Trait declaration generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates an empty trait declaration: trait Name { }
    /// </summary>
    public static Gen<TraitDeclarationNode> GenEmptyTraitDeclaration()
    {
        return from name in Gen.Elements(TraitNames)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   Members = ImmutableArray<TraitMemberDeclarationNode>.Empty,
               };
    }

    /// <summary>
    /// Generates a trait declaration with only field members.
    /// </summary>
    public static Gen<TraitDeclarationNode> GenFieldOnlyTraitDeclaration()
    {
        return from name in Gen.Elements(TraitNames)
               from fields in GenTraitFieldMember().ListOf()
                   .Where(list => list.Count > 0 && list.Count <= 3)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   Members = fields.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait declaration with only method members.
    /// </summary>
    public static Gen<TraitDeclarationNode> GenMethodOnlyTraitDeclaration()
    {
        return from name in Gen.Elements(TraitNames)
               from methods in GenTraitMethodMember().ListOf()
                   .Where(list => list.Count > 0 && list.Count <= 3)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   Members = methods.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait declaration with only property members.
    /// </summary>
    public static Gen<TraitDeclarationNode> GenPropertyOnlyTraitDeclaration()
    {
        return from name in Gen.Elements(TraitNames)
               from props in GenTraitPropertyMember().ListOf()
                   .Where(list => list.Count > 0 && list.Count <= 3)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   Members = props.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a trait declaration with a mix of field, method, and property members.
    /// </summary>
    public static Gen<TraitDeclarationNode> GenMixedTraitDeclaration()
    {
        return from name in Gen.Elements(TraitNames)
               from members in GenTraitMember().ListOf()
                   .Where(list => list.Count > 0 && list.Count <= 5)
               select new TraitDeclarationNode
               {
                   Name = Tok(name),
                   Members = members.ToImmutableArray(),
               };
    }

    /// <summary>
    /// Generates a random trait declaration (empty, field-only, method-only, property-only, or mixed).
    /// </summary>
    public static Gen<TraitDeclarationNode> GenTraitDeclaration()
    {
        return Gen.OneOf(
            GenEmptyTraitDeclaration(),
            GenFieldOnlyTraitDeclaration(),
            GenMethodOnlyTraitDeclaration(),
            GenPropertyOnlyTraitDeclaration(),
            GenMixedTraitDeclaration());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // With declaration generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a single member mapping: sourceName as targetName;
    /// </summary>
    public static Gen<MemberMappingNode> GenMemberMapping()
    {
        return from sourceName in Gen.Elements(MappingSourceNames)
               from targetName in Gen.Elements(FieldNames.Concat(MethodNames).Concat(PropertyNames).ToArray())
               select new MemberMappingNode
               {
                   SourceName = Tok(sourceName),
                   TargetName = Tok(targetName),
               };
    }

    /// <summary>
    /// Generates a member mapping where source and target names are the same (same-name mapping).
    /// </summary>
    public static Gen<MemberMappingNode> GenSameNameMapping()
    {
        return from name in Gen.Elements(FieldNames)
               select new MemberMappingNode
               {
                   SourceName = Tok(name),
                   TargetName = Tok(name),
               };
    }

    /// <summary>
    /// Generates a with declaration: with TypeName : TraitName { mappings... }
    /// </summary>
    public static Gen<WithDeclarationNode> GenWithDeclaration()
    {
        var mixedMappings = from mapping in GenMemberMapping().ListOf()
                                .Where(list => list.Count > 0 && list.Count <= 4)
                            select mapping.ToImmutableArray();

        var sameNameMappings = from mapping in GenSameNameMapping().ListOf()
                                   .Where(list => list.Count > 0 && list.Count <= 3)
                               select mapping.ToImmutableArray();

        var combinedMappings = from regular in GenMemberMapping().ListOf()
                                   .Where(list => list.Count > 0 && list.Count <= 2)
                               from sameName in GenSameNameMapping().ListOf()
                                   .Where(list => list.Count > 0 && list.Count <= 2)
                               select regular.Concat(sameName).ToImmutableArray();

        return from typeName in Gen.Elements(TypeNames)
               from traitName in Gen.Elements(TraitNames)
               from mappings in Gen.OneOf(mixedMappings, sameNameMappings, combinedMappings)
               select new WithDeclarationNode
               {
                   TypeName = Tok(typeName),
                   TraitName = Tok(traitName),
                   Mappings = mappings,
               };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Complete program generators (with traits and with blocks)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a CompilationUnitNode with a single trait declaration.
    /// </summary>
    public static Gen<CompilationUnitNode> GenTraitOnly()
    {
        return GenTraitDeclaration().Select(traitDecl =>
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
    /// Generates a CompilationUnitNode with a trait declaration and a with block.
    /// </summary>
    public static Gen<CompilationUnitNode> GenTraitWithWithBlock()
    {
        return from traitDecl in GenTraitDeclaration()
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
    /// Generates a CompilationUnitNode with a class and a trait declaration.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassAndTrait()
    {
        return from className in Gen.Elements(TypeNames)
               from traitDecl in GenTraitDeclaration()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new ClassTypeTypeDeclarationNode
                           {
                               ClassDeclaration = new ClassDeclarationNode
                               {
                                   Name = Tok(className),
                                   Members = ImmutableArray<MemberDeclarationNode>.Empty,
                               },
                           },
                       },
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new TraitTypeTypeDeclarationNode
                           {
                               TraitDeclaration = traitDecl,
                           },
                       }),
               };
    }

    /// <summary>
    /// Generates a CompilationUnitNode with a class, a trait, and a with block.
    /// </summary>
    public static Gen<CompilationUnitNode> GenClassTraitAndWithBlock()
    {
        return from className in Gen.Elements(TypeNames)
               from traitDecl in GenTraitDeclaration()
               from withDecl in GenWithDeclaration()
               select new CompilationUnitNode
               {
                   Items = ImmutableArray.Create<TopLevelItemNode>(
                       new TypeItemTopLevelItemNode
                       {
                           TypeDeclaration = new ClassTypeTypeDeclarationNode
                           {
                               ClassDeclaration = new ClassDeclarationNode
                               {
                                   Name = Tok(className),
                                   Members = ImmutableArray<MemberDeclarationNode>.Empty,
                               },
                           },
                       },
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
    /// Generates a CompilationUnitNode with multiple traits.
    /// </summary>
    public static Gen<CompilationUnitNode> GenMultipleTraits()
    {
        return from traits in GenTraitDeclaration().ListOf()
                   .Where(list => list.Count >= 2 && list.Count <= 4)
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
    /// Generates a CompilationUnitNode with only a with block (no trait or class).
    /// This tests that the pretty-printer handles standalone with blocks.
    /// </summary>
    public static Gen<CompilationUnitNode> GenWithBlockOnly()
    {
        return GenWithDeclaration().Select(withDecl =>
            new CompilationUnitNode
            {
                Items = ImmutableArray.Create<TopLevelItemNode>(
                    new WithItemTopLevelItemNode
                    {
                        WithDeclaration = withDecl,
                    }),
            });
    }

    /// <summary>
    /// Generates a random CompilationUnitNode with trait-related declarations.
    /// </summary>
    public static Gen<CompilationUnitNode> GenTraitProgram()
    {
        return Gen.OneOf(
            GenTraitOnly(),
            GenTraitWithWithBlock(),
            GenClassAndTrait(),
            GenClassTraitAndWithBlock(),
            GenMultipleTraits(),
            GenWithBlockOnly());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // FsCheck Arbitrary registrations
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>FsCheck Arbitrary for trait programs.</summary>
    public static Arbitrary<CompilationUnitNode> ArbitraryTraitProgram() =>
        GenTraitProgram().ToArbitrary();
}
