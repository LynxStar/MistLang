// Feature: traits, Property 5: Trait satisfaction via with-block
// **Validates: Requirements 6.1, 6.7, 6.8**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying trait satisfaction via with-block.
///
/// Property 5: For any type and trait where a with block provides valid mappings
/// for all trait members not already structurally matched by name, the semantic
/// model builder SHALL register the type as satisfying the trait in the
/// TraitSatisfactionMap. Conversely, for any with block that does not cover all
/// unmapped trait members, the semantic model builder SHALL emit a diagnostic
/// listing the unsatisfied members.
/// </summary>
public class TraitWithBlockSatisfactionPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModelWithDiagnostics(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
            "string" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            },
            "bool" => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            },
            _ => new PrimitiveTypeRefTypeReferenceNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            },
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(params TopLevelItemNode[] items) =>
        new() { Items = items.ToImmutableArray() };

    private static TopLevelItemNode MakeClassTypeItem(ClassDeclarationNode classDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl },
        };

    private static TopLevelItemNode MakeStructTypeItem(StructDeclarationNode structDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new StructTypeTypeDeclarationNode { StructDeclaration = structDecl },
        };

    private static TopLevelItemNode MakeTraitTypeItem(TraitDeclarationNode traitDecl) =>
        new TypeItemTopLevelItemNode
        {
            TypeDeclaration = new TraitTypeTypeDeclarationNode { TraitDeclaration = traitDecl },
        };

    private static TopLevelItemNode MakeWithItem(WithDeclarationNode withDecl) =>
        new WithItemTopLevelItemNode { WithDeclaration = withDecl };

    private static ClassDeclarationNode MakeClassWithPublicFields(
        string className, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    private static StructDeclarationNode MakeStructWithPublicFields(
        string structName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new StructDeclarationNode { Name = Tok(structName), Members = members };
    }

    private static TraitDeclarationNode MakeTraitWithFields(
        string traitName, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (TraitMemberDeclarationNode)new TraitFieldMemberTraitMemberDeclarationNode
            {
                Field = new TraitFieldDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    private static WithDeclarationNode MakeWithDeclaration(
        string typeName, string traitName, params (string source, string target)[] mappings)
    {
        return new WithDeclarationNode
        {
            TypeName = Tok(typeName),
            TraitName = Tok(traitName),
            Mappings = mappings.Select(m => new MemberMappingNode
            {
                SourceName = Tok(m.source),
                TargetName = Tok(m.target),
            }).ToImmutableArray(),
        };
    }

    private static IType? FindType(ISemanticModel model, string name, SymbolKind kind)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == kind)
            ?.DeclaredType;
    }

    private static TraitType? FindTrait(ISemanticModel model, string name)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == SymbolKind.Trait)
            ?.DeclaredType as TraitType;
    }

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon"];

    private static readonly string[] StructNames =
        ["Point", "Vector", "Rect", "Size", "Color"];

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable"];

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    /// <summary>
    /// Source field names on the type — deliberately different from trait field names
    /// so that structural matching by name does NOT apply.
    /// </summary>
    private static readonly string[] SourceFieldNames =
        ["myValue", "myCount", "mySize", "myWidth", "myHeight"];

    /// <summary>
    /// Trait field names — deliberately different from source field names.
    /// </summary>
    private static readonly string[] TraitFieldNames =
        ["value", "count", "size", "width", "height"];

    /// <summary>Whether the implementing type is a class or struct.</summary>
    public enum ImplTypeKind { Class, Struct }

    // ═══════════════════════════════════════════════════════════════════════
    // Scenario types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>
    /// A scenario where a with-block provides complete mappings for all trait members
    /// (no structural matches, all via with-block).
    /// </summary>
    public record FullWithBlockScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string FieldType, string SourceName, string TraitFieldName)> Mappings)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var maps = string.Join(", ", Mappings.Select(m => $"{m.SourceName} as {m.TraitFieldName}"));
            return $"{kind} {TypeName} satisfies trait {TraitName} via with {{ {maps} }}";
        }
    }

    /// <summary>
    /// A scenario where a with-block is incomplete — it does not cover all unmapped
    /// trait members, so a MIST_TRAIT_WITH_UNSATISFIED diagnostic should be emitted.
    /// </summary>
    public record IncompleteWithBlockScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string FieldType, string SourceName, string TraitFieldName)> MappedMembers,
        ImmutableArray<(string FieldType, string TraitFieldName)> UnsatisfiedMembers)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var mapped = string.Join(", ", MappedMembers.Select(m => $"{m.SourceName} as {m.TraitFieldName}"));
            var unsatisfied = string.Join(", ", UnsatisfiedMembers.Select(m => m.TraitFieldName));
            return $"{kind} {TypeName} incomplete with for trait {TraitName} [mapped: {mapped}] [unsatisfied: {unsatisfied}]";
        }
    }

    /// <summary>
    /// A scenario where some trait members are structurally matched by name and
    /// the remaining are covered by a with-block.
    /// </summary>
    public record MixedSatisfactionScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string FieldType, string FieldName)> StructuralFields,
        ImmutableArray<(string FieldType, string SourceName, string TraitFieldName)> WithMappings)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var structural = string.Join(", ", StructuralFields.Select(f => f.FieldName));
            var mapped = string.Join(", ", WithMappings.Select(m => $"{m.SourceName} as {m.TraitFieldName}"));
            return $"{kind} {TypeName} satisfies trait {TraitName} [structural: {structural}] [with: {mapped}]";
        }
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where a with-block provides complete mappings for all
    /// trait members. The type's field names are deliberately different from the
    /// trait's field names so structural matching does not apply.
    /// </summary>
    private static Gen<FullWithBlockScenario> GenFullWithBlockScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(fieldCount)
               let sourceNames = SourceFieldNames.Take(fieldCount).ToArray()
               let traitFieldNames = TraitFieldNames.Take(fieldCount).ToArray()
               let mappings = Enumerable.Range(0, fieldCount)
                   .Select(i => (fieldTypes[i], sourceNames[i], traitFieldNames[i]))
                   .ToImmutableArray()
               select new FullWithBlockScenario(typeName, traitName, typeKind, mappings);
    }

    /// <summary>
    /// Generates a scenario where a with-block is incomplete — it maps some but not
    /// all trait members, so a diagnostic should be emitted.
    /// </summary>
    private static Gen<IncompleteWithBlockScenario> GenIncompleteWithBlockScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from totalCount in Gen.Choose(2, 4)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(totalCount)
               let sourceNames = SourceFieldNames.Take(totalCount).ToArray()
               let traitFieldNames = TraitFieldNames.Take(totalCount).ToArray()
               from mappedCount in Gen.Choose(1, totalCount - 1)
               let mapped = Enumerable.Range(0, mappedCount)
                   .Select(i => (fieldTypes[i], sourceNames[i], traitFieldNames[i]))
                   .ToImmutableArray()
               let unsatisfied = Enumerable.Range(mappedCount, totalCount - mappedCount)
                   .Select(i => (fieldTypes[i], traitFieldNames[i]))
                   .ToImmutableArray()
               select new IncompleteWithBlockScenario(typeName, traitName, typeKind, mapped, unsatisfied);
    }

    /// <summary>
    /// Generates a mixed scenario where some trait members are structurally matched
    /// (same name on type and trait) and the rest are covered by a with-block.
    /// </summary>
    private static Gen<MixedSatisfactionScenario> GenMixedSatisfactionScenario()
    {
        // Use shared names for structural matches, different names for with-block mappings.
        // Shared names come from TraitFieldNames (used on both type and trait).
        // With-block source names come from SourceFieldNames (different from trait names).
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from structuralCount in Gen.Choose(1, 2)
               from withCount in Gen.Choose(1, 2)
               from fieldTypes in Gen.Elements(PrimitiveTypes).ArrayOf(structuralCount + withCount)
               // Structural fields: same name on both type and trait
               let structuralFields = Enumerable.Range(0, structuralCount)
                   .Select(i => (fieldTypes[i], TraitFieldNames[i]))
                   .ToImmutableArray()
               // With-block mappings: different source names mapped to trait names
               let withMappings = Enumerable.Range(0, withCount)
                   .Select(i => (fieldTypes[structuralCount + i], SourceFieldNames[i], TraitFieldNames[structuralCount + i]))
                   .ToImmutableArray()
               select new MixedSatisfactionScenario(typeName, traitName, typeKind, structuralFields, withMappings);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 5a: For any type and trait where a with-block provides valid mappings
    /// for ALL trait members (none structurally matched), the TraitSatisfactionMap
    /// SHALL report the type as satisfying the trait.
    ///
    /// **Validates: Requirements 6.1, 6.7**
    /// </summary>
    [Fact]
    public void FullWithBlock_TypeSatisfiesTrait()
    {
        var property = Prop.ForAll(
            GenFullWithBlockScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with fields using trait field names
                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    scenario.Mappings.Select(m => (m.FieldType, m.TraitFieldName)).ToArray());

                // Build type with fields using source names (different from trait names)
                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicFields(
                        scenario.TypeName,
                        scenario.Mappings.Select(m => (m.FieldType, m.SourceName)).ToArray());
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    var structDecl = MakeStructWithPublicFields(
                        scenario.TypeName,
                        scenario.Mappings.Select(m => (m.FieldType, m.SourceName)).ToArray());
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                // Build with-block mapping source names to trait field names
                var withDecl = MakeWithDeclaration(
                    scenario.TypeName,
                    scenario.TraitName,
                    scenario.Mappings.Select(m => (m.SourceName, m.TraitFieldName)).ToArray());

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl), MakeWithItem(withDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return model.TraitSatisfactionMap.Satisfies(type, trait)
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} to satisfy {scenario.TraitName} via with-block");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 5b: For any with-block that does NOT cover all unmapped trait members,
    /// the semantic model builder SHALL emit a MIST_TRAIT_WITH_UNSATISFIED diagnostic
    /// listing the unsatisfied members, and the TraitSatisfactionMap SHALL NOT report
    /// the type as satisfying the trait.
    ///
    /// **Validates: Requirements 6.8**
    /// </summary>
    [Fact]
    public void IncompleteWithBlock_EmitsDiagnosticAndDoesNotSatisfy()
    {
        var property = Prop.ForAll(
            GenIncompleteWithBlockScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with ALL fields (mapped + unsatisfied)
                var allTraitFields = scenario.MappedMembers
                    .Select(m => (m.FieldType, m.TraitFieldName))
                    .Concat(scenario.UnsatisfiedMembers.Select(u => (u.FieldType, u.TraitFieldName)))
                    .ToArray();
                var traitDecl = MakeTraitWithFields(scenario.TraitName, allTraitFields);

                // Build type with fields for the mapped members only (using source names)
                // Also add fields for the unsatisfied members but with DIFFERENT names
                // so they don't structurally match
                var typeFields = scenario.MappedMembers
                    .Select(m => (m.FieldType, m.SourceName))
                    .ToArray();

                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicFields(scenario.TypeName, typeFields);
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    var structDecl = MakeStructWithPublicFields(scenario.TypeName, typeFields);
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                // Build with-block that only maps the mapped members (not the unsatisfied ones)
                var withDecl = MakeWithDeclaration(
                    scenario.TypeName,
                    scenario.TraitName,
                    scenario.MappedMembers.Select(m => (m.SourceName, m.TraitFieldName)).ToArray());

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl), MakeWithItem(withDecl));
                var (model, diagnostics) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                // Should NOT satisfy the trait
                var notSatisfied = !model.TraitSatisfactionMap.Satisfies(type, trait);

                // Should have a MIST_TRAIT_WITH_UNSATISFIED diagnostic
                var hasDiagnostic = diagnostics.Any(d =>
                    d.Id.Value == "MIST_TRAIT_WITH_UNSATISFIED"
                    && d.Message.Contains(scenario.TypeName)
                    && d.Message.Contains(scenario.TraitName));

                // The diagnostic should mention each unsatisfied member
                var mentionsUnsatisfied = scenario.UnsatisfiedMembers.All(u =>
                    diagnostics.Any(d =>
                        d.Id.Value == "MIST_TRAIT_WITH_UNSATISFIED"
                        && d.Message.Contains(u.TraitFieldName)));

                return (notSatisfied && hasDiagnostic && mentionsUnsatisfied)
                    .ToProperty()
                    .Label($"Expected MIST_TRAIT_WITH_UNSATISFIED for {scenario.TypeName} : {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 5c: For any type and trait where some members are structurally matched
    /// by name and the remaining are covered by a with-block, the TraitSatisfactionMap
    /// SHALL report the type as satisfying the trait.
    ///
    /// **Validates: Requirements 6.1, 6.7**
    /// </summary>
    [Fact]
    public void MixedStructuralAndWithBlock_TypeSatisfiesTrait()
    {
        var property = Prop.ForAll(
            GenMixedSatisfactionScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with all fields: structural fields + with-block target fields
                var allTraitFields = scenario.StructuralFields
                    .Select(f => (f.FieldType, f.FieldName))
                    .Concat(scenario.WithMappings.Select(m => (m.FieldType, m.TraitFieldName)))
                    .ToArray();
                var traitDecl = MakeTraitWithFields(scenario.TraitName, allTraitFields);

                // Build type with:
                // - Fields matching structural trait fields by name (same name)
                // - Fields with different names for with-block mappings
                var typeFields = scenario.StructuralFields
                    .Select(f => (f.FieldType, f.FieldName))
                    .Concat(scenario.WithMappings.Select(m => (m.FieldType, m.SourceName)))
                    .ToArray();

                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicFields(scenario.TypeName, typeFields);
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    var structDecl = MakeStructWithPublicFields(scenario.TypeName, typeFields);
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                // Build with-block mapping only the non-structural members
                var withDecl = MakeWithDeclaration(
                    scenario.TypeName,
                    scenario.TraitName,
                    scenario.WithMappings.Select(m => (m.SourceName, m.TraitFieldName)).ToArray());

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl), MakeWithItem(withDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return model.TraitSatisfactionMap.Satisfies(type, trait)
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} to satisfy {scenario.TraitName} via mixed structural + with-block");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }
}
