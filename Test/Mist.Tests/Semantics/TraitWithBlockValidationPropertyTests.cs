// Feature: traits, Property 4: With-block validation diagnostics
// **Validates: Requirements 6.2, 6.3, 6.4, 6.5, 6.6, 6.9, 10.2, 10.3, 10.4**

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
/// Property-based tests verifying with-block validation diagnostics.
///
/// Property 4: For any with block that references an undefined type name, an undefined
/// trait name, an undefined source member, an undefined target trait member, or maps a
/// source member with an incompatible type signature, the semantic model builder SHALL
/// emit exactly one diagnostic error for each invalid reference. For any duplicate with
/// block for the same type-trait pair, the semantic model builder SHALL emit a diagnostic error.
/// </summary>
public class TraitWithBlockValidationPropertyTests
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

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta"];

    private static readonly string[] TraitNames =
        ["Printable", "Drawable", "Serializable", "Hashable", "Comparable"];

    private static readonly string[] FieldNames =
        ["value", "count", "size", "width", "height", "length"];

    /// <summary>Names guaranteed to NOT appear in any generated class or trait.</summary>
    private static readonly string[] UndefinedTypeNames =
        ["NoSuchType", "MissingClass", "GhostType", "PhantomStruct", "VoidEntity"];

    private static readonly string[] UndefinedTraitNames =
        ["NoSuchTrait", "MissingTrait", "GhostTrait", "PhantomTrait", "VoidTrait"];

    private static readonly string[] UndefinedMemberNames =
        ["noSuchField", "missingMember", "ghostProp", "phantomVal", "voidField"];

    /// <summary>Pairs of (sourceType, targetType) that are guaranteed to be incompatible.</summary>
    private static readonly (string Source, string Target)[] IncompatibleTypePairs =
    [
        ("string", "int"),
        ("int", "string"),
        ("bool", "int"),
        ("int", "bool"),
        ("string", "bool"),
        ("bool", "string"),
    ];

    // ═══════════════════════════════════════════════════════════════════════
    // Generators for invalid with-block scenarios
    // ═══════════════════════════════════════════════════════════════════════

    private static Gen<(CompilationUnitNode Ast, string UndefinedTypeName)> GenUndefinedTypeScenario()
    {
        return from traitName in Gen.Elements(TraitNames)
               from undefinedTypeName in Gen.Elements(UndefinedTypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let traitDecl = MakeTraitWithFields(traitName, ("int", fieldName))
               let withDecl = MakeWithDeclaration(undefinedTypeName, traitName, ("x", fieldName))
               let ast = MakeCompilationUnit(
                   MakeTraitTypeItem(traitDecl),
                   MakeWithItem(withDecl))
               select (ast, undefinedTypeName);
    }

    private static Gen<(CompilationUnitNode Ast, string UndefinedTraitName)> GenUndefinedTraitScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from undefinedTraitName in Gen.Elements(UndefinedTraitNames)
               from fieldName in Gen.Elements(FieldNames)
               let classDecl = MakeClassWithPublicFields(className, ("int", fieldName))
               let withDecl = MakeWithDeclaration(className, undefinedTraitName, (fieldName, "x"))
               let ast = MakeCompilationUnit(
                   MakeClassTypeItem(classDecl),
                   MakeWithItem(withDecl))
               select (ast, undefinedTraitName);
    }

    private static Gen<(CompilationUnitNode Ast, string ClassName, string TraitName)> GenDuplicateWithScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldName in Gen.Elements(FieldNames)
               let classDecl = MakeClassWithPublicFields(className, ("int", fieldName))
               let traitDecl = MakeTraitWithFields(traitName, ("int", "traitField"))
               let withDecl1 = MakeWithDeclaration(className, traitName, (fieldName, "traitField"))
               let withDecl2 = MakeWithDeclaration(className, traitName, (fieldName, "traitField"))
               let ast = MakeCompilationUnit(
                   MakeClassTypeItem(classDecl),
                   MakeTraitTypeItem(traitDecl),
                   MakeWithItem(withDecl1),
                   MakeWithItem(withDecl2))
               select (ast, className, traitName);
    }

    private static Gen<(CompilationUnitNode Ast, string UndefinedSourceName, string ClassName)> GenUndefinedSourceScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from realFieldName in Gen.Elements(FieldNames)
               from undefinedSourceName in Gen.Elements(UndefinedMemberNames)
               let classDecl = MakeClassWithPublicFields(className, ("int", realFieldName))
               let traitDecl = MakeTraitWithFields(traitName, ("int", "traitField"))
               let withDecl = MakeWithDeclaration(className, traitName, (undefinedSourceName, "traitField"))
               let ast = MakeCompilationUnit(
                   MakeClassTypeItem(classDecl),
                   MakeTraitTypeItem(traitDecl),
                   MakeWithItem(withDecl))
               select (ast, undefinedSourceName, className);
    }

    private static Gen<(CompilationUnitNode Ast, string UndefinedTargetName, string TraitName)> GenUndefinedTargetScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldName in Gen.Elements(FieldNames)
               from undefinedTargetName in Gen.Elements(UndefinedMemberNames)
               let classDecl = MakeClassWithPublicFields(className, ("int", fieldName))
               let traitDecl = MakeTraitWithFields(traitName, ("int", "traitField"))
               let withDecl = MakeWithDeclaration(className, traitName, (fieldName, undefinedTargetName))
               let ast = MakeCompilationUnit(
                   MakeClassTypeItem(classDecl),
                   MakeTraitTypeItem(traitDecl),
                   MakeWithItem(withDecl))
               select (ast, undefinedTargetName, traitName);
    }

    private static Gen<(CompilationUnitNode Ast, string ClassName, string SourceName, string TraitName, string TargetName)> GenTypeMismatchScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from sourceName in Gen.Elements(FieldNames)
               from typePair in Gen.Elements(IncompatibleTypePairs)
               let classDecl = MakeClassWithPublicFields(className, (typePair.Source, sourceName))
               let traitDecl = MakeTraitWithFields(traitName, (typePair.Target, "traitField"))
               let withDecl = MakeWithDeclaration(className, traitName, (sourceName, "traitField"))
               let ast = MakeCompilationUnit(
                   MakeClassTypeItem(classDecl),
                   MakeTraitTypeItem(traitDecl),
                   MakeWithItem(withDecl))
               select (ast, className, sourceName, traitName, "traitField");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests — using Property.ForAll with explicit generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any with block referencing an undefined type name, the semantic model builder
    /// SHALL emit exactly one MIST_TRAIT_WITH_UNDEFINED_TYPE diagnostic mentioning the type name.
    ///
    /// **Validates: Requirements 6.2, 10.3**
    /// </summary>
    [Fact]
    public void UndefinedType_EmitsExactlyOneDiagnostic()
    {
        var property = Prop.ForAll(
            GenUndefinedTypeScenario().ToArbitrary(),
            scenario =>
            {
                var (ast, undefinedTypeName) = scenario;
                var (_, diagnostics) = BuildModelWithDiagnostics(ast);

                var matching = diagnostics.Where(d =>
                    d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_TYPE"
                    && d.Message.Contains(undefinedTypeName)).ToList();

                return (matching.Count == 1).ToProperty();
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// For any with block referencing an undefined trait name, the semantic model builder
    /// SHALL emit exactly one MIST_TRAIT_WITH_UNDEFINED_TRAIT diagnostic mentioning the trait name.
    ///
    /// **Validates: Requirements 6.3, 10.4**
    /// </summary>
    [Fact]
    public void UndefinedTrait_EmitsExactlyOneDiagnostic()
    {
        var property = Prop.ForAll(
            GenUndefinedTraitScenario().ToArbitrary(),
            scenario =>
            {
                var (ast, undefinedTraitName) = scenario;
                var (_, diagnostics) = BuildModelWithDiagnostics(ast);

                var matching = diagnostics.Where(d =>
                    d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_TRAIT"
                    && d.Message.Contains(undefinedTraitName)).ToList();

                return (matching.Count == 1).ToProperty();
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// For any duplicate with block for the same type-trait pair, the semantic model builder
    /// SHALL emit exactly one MIST_TRAIT_DUPLICATE_WITH diagnostic mentioning both the type and trait names.
    ///
    /// **Validates: Requirements 6.9**
    /// </summary>
    [Fact]
    public void DuplicateWith_EmitsExactlyOneDiagnostic()
    {
        var property = Prop.ForAll(
            GenDuplicateWithScenario().ToArbitrary(),
            scenario =>
            {
                var (ast, className, traitName) = scenario;
                var (_, diagnostics) = BuildModelWithDiagnostics(ast);

                var matching = diagnostics.Where(d =>
                    d.Id.Value == "MIST_TRAIT_DUPLICATE_WITH"
                    && d.Message.Contains(className)
                    && d.Message.Contains(traitName)).ToList();

                return (matching.Count == 1).ToProperty();
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// For any with block mapping referencing an undefined source member, the semantic model builder
    /// SHALL emit exactly one MIST_TRAIT_WITH_UNDEFINED_SOURCE diagnostic mentioning the member and type names.
    ///
    /// **Validates: Requirements 6.4, 10.3**
    /// </summary>
    [Fact]
    public void UndefinedSource_EmitsExactlyOneDiagnostic()
    {
        var property = Prop.ForAll(
            GenUndefinedSourceScenario().ToArbitrary(),
            scenario =>
            {
                var (ast, undefinedSourceName, className) = scenario;
                var (_, diagnostics) = BuildModelWithDiagnostics(ast);

                var matching = diagnostics.Where(d =>
                    d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_SOURCE"
                    && d.Message.Contains(undefinedSourceName)
                    && d.Message.Contains(className)).ToList();

                return (matching.Count == 1).ToProperty();
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// For any with block mapping referencing an undefined target trait member, the semantic model builder
    /// SHALL emit exactly one MIST_TRAIT_WITH_UNDEFINED_TARGET diagnostic mentioning the member and trait names.
    ///
    /// **Validates: Requirements 6.5, 10.4**
    /// </summary>
    [Fact]
    public void UndefinedTarget_EmitsExactlyOneDiagnostic()
    {
        var property = Prop.ForAll(
            GenUndefinedTargetScenario().ToArbitrary(),
            scenario =>
            {
                var (ast, undefinedTargetName, traitName) = scenario;
                var (_, diagnostics) = BuildModelWithDiagnostics(ast);

                var matching = diagnostics.Where(d =>
                    d.Id.Value == "MIST_TRAIT_WITH_UNDEFINED_TARGET"
                    && d.Message.Contains(undefinedTargetName)
                    && d.Message.Contains(traitName)).ToList();

                return (matching.Count == 1).ToProperty();
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// For any with block mapping where the source member type is incompatible with the target
    /// trait member type, the semantic model builder SHALL emit exactly one
    /// MIST_TRAIT_WITH_TYPE_MISMATCH diagnostic mentioning the source and target members.
    ///
    /// **Validates: Requirements 6.6, 10.2**
    /// </summary>
    [Fact]
    public void TypeMismatch_EmitsExactlyOneDiagnostic()
    {
        var property = Prop.ForAll(
            GenTypeMismatchScenario().ToArbitrary(),
            scenario =>
            {
                var (ast, className, sourceName, traitName, targetName) = scenario;
                var (_, diagnostics) = BuildModelWithDiagnostics(ast);

                var matching = diagnostics.Where(d =>
                    d.Id.Value == "MIST_TRAIT_WITH_TYPE_MISMATCH"
                    && d.Message.Contains($"{className}.{sourceName}")
                    && d.Message.Contains($"{traitName}.{targetName}")).ToList();

                return (matching.Count == 1).ToProperty();
            });

        property.Check(Config.Default.WithMaxTest(100));
    }
}
