// Feature: traits, Property 3: Structural match correctness
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7**

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
/// Property-based tests verifying structural match correctness for trait satisfaction.
///
/// Property 3: For any type (class or struct) and for any trait, the structural match
/// result SHALL be true if and only if every trait member has a corresponding public
/// member on the type with the same name and compatible type signature (fields matched
/// by public fields or properties, methods matched by name + return type + parameter
/// types, properties matched by name + type + accessor subset). Private members SHALL
/// be excluded from matching, and name matches with incompatible type signatures SHALL
/// cause the match to fail.
/// </summary>
public class TraitStructuralMatchPropertyTests
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
            "int" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
            "string" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new StringTypePrimitiveTypeNode(),
            } },
            "bool" => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new BoolTypePrimitiveTypeNode(),
            } },
            _ => new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
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

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates a class with public field members.
    /// </summary>
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

    /// <summary>
    /// Creates a class with private field members (no public modifier).
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPrivateFields(
        string className, params (string typeName, string fieldName)[] fields)
    {
        var members = fields.Select(f =>
            (MemberDeclarationNode)new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray<ModifierNode>.Empty,
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    /// <summary>
    /// Creates a class with a mix of public and private fields.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithMixedFields(
        string className,
        (string typeName, string fieldName)[] publicFields,
        (string typeName, string fieldName)[] privateFields)
    {
        var members = new List<MemberDeclarationNode>();

        foreach (var f in publicFields)
        {
            members.Add(new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            });
        }

        foreach (var f in privateFields)
        {
            members.Add(new FieldMemberMemberDeclarationNode
            {
                Field = new FieldDeclarationNode
                {
                    Modifiers = ImmutableArray<ModifierNode>.Empty,
                    Type = MakePrimitiveTypeRef(f.typeName),
                    Name = Tok(f.fieldName),
                },
            });
        }

        return new ClassDeclarationNode { Name = Tok(className), Members = members.ToImmutableArray() };
    }

    /// <summary>
    /// Creates a class with a public method.
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicMethods(
        string className,
        params (string returnType, string methodName, (string type, string name)[]? parameters)[] methods)
    {
        var members = methods.Select(m =>
        {
            ParameterListNode? paramList = null;
            if (m.parameters is { Length: > 0 })
            {
                var first = m.parameters[0];
                paramList = new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = MakePrimitiveTypeRef(first.type),
                        Name = Tok(first.name),
                    },
                };
            }

            return (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
            {
                Method = new MethodDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    ReturnType = m.returnType == "void"
                        ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                        : MakePrimitiveTypeRef(m.returnType),
                    Name = Tok(m.methodName),
                    Parameters = paramList,
                    Body = new MethodBlockBodyMethodBodyNode
                    {
                        Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                    },
                },
            };
        }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    /// <summary>
    /// Creates a class with a public block property (with get/set accessors).
    /// </summary>
    private static ClassDeclarationNode MakeClassWithPublicProperties(
        string className,
        params (string typeName, string propName, bool hasGet, bool hasSet)[] properties)
    {
        var members = properties.Select(p =>
        {
            var accessors = new List<AccessorDeclarationNode>();
            if (p.hasGet)
                accessors.Add(new AutoGetAccessorAccessorDeclarationNode());
            if (p.hasSet)
                accessors.Add(new AutoSetAccessorAccessorDeclarationNode());

            return (MemberDeclarationNode)new PropertyMemberMemberDeclarationNode
            {
                Property = new BlockPropertyPropertyDeclarationNode
                {
                    Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                    Type = MakePrimitiveTypeRef(p.typeName),
                    Name = Tok(p.propName),
                    Accessors = accessors.ToImmutableArray(),
                },
            };
        }).ToImmutableArray();

        return new ClassDeclarationNode { Name = Tok(className), Members = members };
    }

    /// <summary>
    /// Creates a struct with public field members.
    /// </summary>
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

    /// <summary>
    /// Creates a trait with field members.
    /// </summary>
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

    /// <summary>
    /// Creates a trait with method members.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithMethods(
        string traitName,
        params (string returnType, string methodName, (string type, string name)[]? parameters)[] methods)
    {
        var members = methods.Select(m =>
        {
            ParameterListNode? paramList = null;
            if (m.parameters is { Length: > 0 })
            {
                var first = m.parameters[0];
                paramList = new ParameterListNode
                {
                    Parameter = new ParameterNode
                    {
                        Type = MakePrimitiveTypeRef(first.type),
                        Name = Tok(first.name),
                    },
                };
            }

            return (TraitMemberDeclarationNode)new TraitMethodMemberTraitMemberDeclarationNode
            {
                Method = new TraitMethodDeclarationNode
                {
                    ReturnType = m.returnType == "void"
                        ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                        : MakePrimitiveTypeRef(m.returnType),
                    Name = Tok(m.methodName),
                    Parameters = paramList,
                },
            };
        }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    /// <summary>
    /// Creates a trait with property members.
    /// </summary>
    private static TraitDeclarationNode MakeTraitWithProperties(
        string traitName,
        params (string typeName, string propName, bool hasGet, bool hasSet)[] properties)
    {
        var members = properties.Select(p =>
        {
            var accessors = new List<AbstractAccessorDeclarationNode>();
            if (p.hasGet)
                accessors.Add(new AbstractGetAccessorAbstractAccessorDeclarationNode());
            if (p.hasSet)
                accessors.Add(new AbstractSetAccessorAbstractAccessorDeclarationNode());

            return (TraitMemberDeclarationNode)new TraitPropertyMemberTraitMemberDeclarationNode
            {
                Property = new TraitPropertyDeclarationNode
                {
                    Type = MakePrimitiveTypeRef(p.typeName),
                    Name = Tok(p.propName),
                    Accessors = accessors.ToImmutableArray(),
                },
            };
        }).ToImmutableArray();

        return new TraitDeclarationNode { Name = Tok(traitName), Members = members };
    }

    /// <summary>
    /// Finds a type in the semantic model by name and kind.
    /// </summary>
    private static IType? FindType(ISemanticModel model, string name, SymbolKind kind)
    {
        return model.AllSymbols
            .FirstOrDefault(s => s.Name == name && s.Kind == kind)
            ?.DeclaredType;
    }

    /// <summary>
    /// Finds a TraitType in the semantic model by name.
    /// </summary>
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

    private static readonly string[] FieldNames =
        ["value", "count", "size", "width", "height"];

    private static readonly string[] MethodNames =
        ["Compute", "Process", "Execute", "Transform", "Validate"];

    private static readonly string[] PropertyNames =
        ["Name", "Label", "Title", "Status", "Score"];

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

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
    // Scenario types
    // ═══════════════════════════════════════════════════════════════════════

#pragma warning disable CA1034

    /// <summary>Whether the implementing type is a class or struct.</summary>
    public enum ImplTypeKind { Class, Struct }

    /// <summary>
    /// A scenario where a type has all public members matching a trait's fields.
    /// </summary>
    public record FieldSatisfiedScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string TypeName, string FieldName)> Fields)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var fields = string.Join(", ", Fields.Select(f => $"{f.TypeName} {f.FieldName}"));
            return $"{kind} {TypeName} satisfies trait {TraitName} {{ {fields} }}";
        }
    }

    /// <summary>
    /// A scenario where a type is missing some trait field members.
    /// </summary>
    public record FieldMissingScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        ImmutableArray<(string TypeName, string FieldName)> ProvidedFields,
        ImmutableArray<(string TypeName, string FieldName)> MissingFields)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var provided = string.Join(", ", ProvidedFields.Select(f => $"{f.TypeName} {f.FieldName}"));
            var missing = string.Join(", ", MissingFields.Select(f => f.FieldName));
            return $"{kind} {TypeName} missing [{missing}] for trait {TraitName} [provided: {provided}]";
        }
    }

    /// <summary>
    /// A scenario where a type has private members matching trait member names.
    /// </summary>
    public record PrivateMemberScenario(
        string TypeName,
        string TraitName,
        ImmutableArray<(string TypeName, string FieldName)> PrivateFields)
    {
        public override string ToString()
        {
            var fields = string.Join(", ", PrivateFields.Select(f => f.FieldName));
            return $"class {TypeName} has private [{fields}] matching trait {TraitName}";
        }
    }

    /// <summary>
    /// A scenario where a type has name matches but incompatible types.
    /// </summary>
    public record TypeMismatchScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        string FieldName,
        string TypeFieldType,
        string TraitFieldType)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            return $"{kind} {TypeName}.{FieldName}:{TypeFieldType} vs trait {TraitName}.{FieldName}:{TraitFieldType}";
        }
    }

    /// <summary>
    /// A scenario where a type has public methods matching a trait's methods.
    /// </summary>
    public record MethodSatisfiedScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        string ReturnType,
        string MethodName,
        bool HasParam,
        string ParamType)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var param = HasParam ? $"{ParamType} arg" : "";
            return $"{kind} {TypeName} satisfies trait {TraitName} {{ {ReturnType} {MethodName}({param}) }}";
        }
    }

    /// <summary>
    /// A scenario where a type has a public property matching a trait's property.
    /// </summary>
    public record PropertySatisfiedScenario(
        string TypeName,
        string TraitName,
        ImplTypeKind TypeKind,
        string PropType,
        string PropName,
        bool TraitHasGet,
        bool TraitHasSet,
        bool TypeHasGet,
        bool TypeHasSet)
    {
        public override string ToString()
        {
            var kind = TypeKind == ImplTypeKind.Class ? "class" : "struct";
            var traitAccessors = $"{(TraitHasGet ? "get" : "")}{(TraitHasSet ? " set" : "")}".Trim();
            var typeAccessors = $"{(TypeHasGet ? "get" : "")}{(TypeHasSet ? " set" : "")}".Trim();
            return $"{kind} {TypeName}.{PropName} [{typeAccessors}] vs trait {TraitName}.{PropName} [{traitAccessors}]";
        }
    }

    /// <summary>
    /// A scenario where a trait field is satisfied by a public property on the type.
    /// </summary>
    public record FieldMatchedByPropertyScenario(
        string TypeName,
        string TraitName,
        string FieldType,
        string FieldName)
    {
        public override string ToString() =>
            $"class {TypeName} property {FieldType} {FieldName} satisfies trait {TraitName} field {FieldType} {FieldName}";
    }

#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a scenario where a type has all public fields matching a trait's fields.
    /// </summary>
    private static Gen<FieldSatisfiedScenario> GenFieldSatisfiedScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from shuffledFields in Gen.Shuffle(FieldNames)
               from types in Gen.Elements(PrimitiveTypes).ListOf()
                   .Where(l => l.Count >= fieldCount)
               let fields = shuffledFields.Take(fieldCount)
                   .Zip(types, (name, type) => (type, name))
                   .ToImmutableArray()
               select new FieldSatisfiedScenario(typeName, traitName, typeKind, fields);
    }

    /// <summary>
    /// Generates a scenario where a type is missing some trait field members.
    /// </summary>
    private static Gen<FieldMissingScenario> GenFieldMissingScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from totalCount in Gen.Choose(2, 4)
               from shuffledFields in Gen.Shuffle(FieldNames)
               from types in Gen.Elements(PrimitiveTypes).ListOf()
                   .Where(l => l.Count >= totalCount)
               let allFields = shuffledFields.Take(totalCount)
                   .Zip(types, (name, type) => (type, name))
                   .ToImmutableArray()
               from providedCount in Gen.Choose(0, totalCount - 1)
               let provided = allFields.Take(providedCount).ToImmutableArray()
               let missing = allFields.Skip(providedCount).ToImmutableArray()
               select new FieldMissingScenario(typeName, traitName, typeKind, provided, missing);
    }

    /// <summary>
    /// Generates a scenario where a type has private members matching trait member names.
    /// </summary>
    private static Gen<PrivateMemberScenario> GenPrivateMemberScenario()
    {
        return from typeName in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldCount in Gen.Choose(1, 3)
               from shuffledFields in Gen.Shuffle(FieldNames)
               from types in Gen.Elements(PrimitiveTypes).ListOf()
                   .Where(l => l.Count >= fieldCount)
               let fields = shuffledFields.Take(fieldCount)
                   .Zip(types, (name, type) => (type, name))
                   .ToImmutableArray()
               select new PrivateMemberScenario(typeName, traitName, fields);
    }

    /// <summary>
    /// Generates a scenario where a type has name matches but incompatible types.
    /// </summary>
    private static Gen<TypeMismatchScenario> GenTypeMismatchScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldName in Gen.Elements(FieldNames)
               from typePair in Gen.Elements(IncompatibleTypePairs)
               select new TypeMismatchScenario(
                   typeName, traitName, typeKind, fieldName, typePair.Source, typePair.Target);
    }

    /// <summary>
    /// Generates a scenario where a type has public methods matching a trait's methods.
    /// </summary>
    private static Gen<MethodSatisfiedScenario> GenMethodSatisfiedScenario()
    {
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from returnType in Gen.Elements("void", "int", "string", "bool")
               from methodName in Gen.Elements(MethodNames)
               from hasParam in Gen.Elements(true, false)
               from paramType in Gen.Elements(PrimitiveTypes)
               select new MethodSatisfiedScenario(
                   typeName, traitName, typeKind, returnType, methodName, hasParam, paramType);
    }

    /// <summary>
    /// Generates a scenario where a type has a public property matching a trait's property,
    /// with the type's property having at least the accessors declared in the trait.
    /// </summary>
    private static Gen<PropertySatisfiedScenario> GenPropertySatisfiedScenario()
    {
        // Generate trait accessor combos, then ensure type has at least those
        return from typeKind in Gen.Elements(ImplTypeKind.Class, ImplTypeKind.Struct)
               from typeName in typeKind == ImplTypeKind.Class
                   ? Gen.Elements(ClassNames)
                   : Gen.Elements(StructNames)
               from traitName in Gen.Elements(TraitNames)
               from propType in Gen.Elements(PrimitiveTypes)
               from propName in Gen.Elements(PropertyNames)
               from traitHasGet in Gen.Elements(true, false)
               from traitHasSet in Gen.Elements(true, false)
               where traitHasGet || traitHasSet // at least one accessor
               // Type must have at least the trait's accessors
               from typeExtraGet in Gen.Elements(true, false)
               from typeExtraSet in Gen.Elements(true, false)
               let typeHasGet = traitHasGet || typeExtraGet
               let typeHasSet = traitHasSet || typeExtraSet
               where typeHasGet || typeHasSet // at least one accessor
               select new PropertySatisfiedScenario(
                   typeName, traitName, typeKind, propType, propName,
                   traitHasGet, traitHasSet, typeHasGet, typeHasSet);
    }

    /// <summary>
    /// Generates a scenario where a trait field is satisfied by a public property on the type.
    /// </summary>
    private static Gen<FieldMatchedByPropertyScenario> GenFieldMatchedByPropertyScenario()
    {
        return from typeName in Gen.Elements(ClassNames)
               from traitName in Gen.Elements(TraitNames)
               from fieldType in Gen.Elements(PrimitiveTypes)
               from fieldName in Gen.Elements(FieldNames)
               select new FieldMatchedByPropertyScenario(typeName, traitName, fieldType, fieldName);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<FieldSatisfiedScenario> ArbitraryFieldSatisfied() =>
        GenFieldSatisfiedScenario().ToArbitrary();

    public static Arbitrary<FieldMissingScenario> ArbitraryFieldMissing() =>
        GenFieldMissingScenario().ToArbitrary();

    public static Arbitrary<PrivateMemberScenario> ArbitraryPrivateMember() =>
        GenPrivateMemberScenario().ToArbitrary();

    public static Arbitrary<TypeMismatchScenario> ArbitraryTypeMismatch() =>
        GenTypeMismatchScenario().ToArbitrary();

    public static Arbitrary<MethodSatisfiedScenario> ArbitraryMethodSatisfied() =>
        GenMethodSatisfiedScenario().ToArbitrary();

    public static Arbitrary<PropertySatisfiedScenario> ArbitraryPropertySatisfied() =>
        GenPropertySatisfiedScenario().ToArbitrary();

    public static Arbitrary<FieldMatchedByPropertyScenario> ArbitraryFieldMatchedByProperty() =>
        GenFieldMatchedByPropertyScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 3a: For any type (class or struct) that has public fields matching
    /// every trait field member by name and compatible type, the TraitSatisfactionMap
    /// SHALL report the type as satisfying the trait.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void FieldSatisfied_TypeSatisfiesTrait()
    {
        var property = Prop.ForAll(
            GenFieldSatisfiedScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with the same fields
                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    scenario.Fields.Select(f => (f.TypeName, f.FieldName)).ToArray());

                // Build type with matching public fields
                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicFields(
                        scenario.TypeName,
                        scenario.Fields.Select(f => (f.TypeName, f.FieldName)).ToArray());
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    var structDecl = MakeStructWithPublicFields(
                        scenario.TypeName,
                        scenario.Fields.Select(f => (f.TypeName, f.FieldName)).ToArray());
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return model.TraitSatisfactionMap.Satisfies(type, trait)
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} to satisfy {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 3b: For any type that is missing one or more trait field members,
    /// the TraitSatisfactionMap SHALL NOT report the type as satisfying the trait.
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Fact]
    public void FieldMissing_TypeDoesNotSatisfyTrait()
    {
        var property = Prop.ForAll(
            GenFieldMissingScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with ALL fields (provided + missing)
                var allFields = scenario.ProvidedFields.Concat(scenario.MissingFields).ToArray();
                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    allFields.Select(f => (f.TypeName, f.FieldName)).ToArray());

                // Build type with only the provided fields
                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicFields(
                        scenario.TypeName,
                        scenario.ProvidedFields.Select(f => (f.TypeName, f.FieldName)).ToArray());
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    var structDecl = MakeStructWithPublicFields(
                        scenario.TypeName,
                        scenario.ProvidedFields.Select(f => (f.TypeName, f.FieldName)).ToArray());
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return (!model.TraitSatisfactionMap.Satisfies(type, trait))
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} NOT to satisfy {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 3c: For any type that has private members matching trait member names,
    /// the TraitSatisfactionMap SHALL NOT report the type as satisfying the trait.
    /// Private members SHALL be excluded from matching.
    ///
    /// **Validates: Requirements 5.6**
    /// </summary>
    [Fact]
    public void PrivateMembers_TypeDoesNotSatisfyTrait()
    {
        var property = Prop.ForAll(
            GenPrivateMemberScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with fields
                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    scenario.PrivateFields.Select(f => (f.TypeName, f.FieldName)).ToArray());

                // Build class with PRIVATE fields matching the trait
                var classDecl = MakeClassWithPrivateFields(
                    scenario.TypeName,
                    scenario.PrivateFields.Select(f => (f.TypeName, f.FieldName)).ToArray());

                var ast = MakeCompilationUnit(
                    MakeClassTypeItem(classDecl),
                    MakeTraitTypeItem(traitDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, SymbolKind.Class);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return (!model.TraitSatisfactionMap.Satisfies(type, trait))
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} with private members NOT to satisfy {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 3d: For any type that has a member with the same name as a trait member
    /// but an incompatible type signature, the TraitSatisfactionMap SHALL NOT report
    /// the type as satisfying the trait.
    ///
    /// **Validates: Requirements 5.7**
    /// </summary>
    [Fact]
    public void TypeMismatch_TypeDoesNotSatisfyTrait()
    {
        var property = Prop.ForAll(
            GenTypeMismatchScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with a field of one type
                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    (scenario.TraitFieldType, scenario.FieldName));

                // Build type with a public field of an incompatible type
                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicFields(
                        scenario.TypeName,
                        (scenario.TypeFieldType, scenario.FieldName));
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    var structDecl = MakeStructWithPublicFields(
                        scenario.TypeName,
                        (scenario.TypeFieldType, scenario.FieldName));
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return (!model.TraitSatisfactionMap.Satisfies(type, trait))
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} with type mismatch NOT to satisfy {scenario.TraitName}");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 3e: For any type that has a public method matching a trait method member
    /// by name, return type, and parameter types, the TraitSatisfactionMap SHALL report
    /// the type as satisfying the trait.
    ///
    /// **Validates: Requirements 5.1, 5.4**
    /// </summary>
    [Fact]
    public void MethodSatisfied_TypeSatisfiesTrait()
    {
        var property = Prop.ForAll(
            GenMethodSatisfiedScenario().ToArbitrary(),
            scenario =>
            {
                var parameters = scenario.HasParam
                    ? new[] { (scenario.ParamType, "arg") }
                    : null;

                // Build trait with a method
                var traitDecl = MakeTraitWithMethods(
                    scenario.TraitName,
                    (scenario.ReturnType, scenario.MethodName, parameters));

                // Build type with a matching public method
                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicMethods(
                        scenario.TypeName,
                        (scenario.ReturnType, scenario.MethodName, parameters));
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    // Build struct with public method
                    var paramList = parameters is { Length: > 0 }
                        ? new ParameterListNode
                        {
                            Parameter = new ParameterNode
                            {
                                Type = MakePrimitiveTypeRef(parameters[0].Item1),
                                Name = Tok(parameters[0].Item2),
                            },
                        }
                        : null;

                    var methodMember = (MemberDeclarationNode)new MethodMemberMemberDeclarationNode
                    {
                        Method = new MethodDeclarationNode
                        {
                            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                            ReturnType = scenario.ReturnType == "void"
                                ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
                                : MakePrimitiveTypeRef(scenario.ReturnType),
                            Name = Tok(scenario.MethodName),
                            Parameters = paramList,
                            Body = new MethodBlockBodyMethodBodyNode
                            {
                                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                            },
                        },
                    };

                    var structDecl = new StructDeclarationNode
                    {
                        Name = Tok(scenario.TypeName),
                        Members = ImmutableArray.Create(methodMember),
                    };
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return model.TraitSatisfactionMap.Satisfies(type, trait)
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} to satisfy {scenario.TraitName} via method match");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 3f: For any type that has a public property matching a trait property member
    /// by name, compatible type, and at least the trait's accessors, the TraitSatisfactionMap
    /// SHALL report the type as satisfying the trait.
    ///
    /// **Validates: Requirements 5.1, 5.5**
    /// </summary>
    [Fact]
    public void PropertySatisfied_TypeSatisfiesTrait()
    {
        var property = Prop.ForAll(
            GenPropertySatisfiedScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with a property
                var traitDecl = MakeTraitWithProperties(
                    scenario.TraitName,
                    (scenario.PropType, scenario.PropName, scenario.TraitHasGet, scenario.TraitHasSet));

                // Build type with a matching public property
                TopLevelItemNode typeItem;
                SymbolKind typeSymbolKind;
                if (scenario.TypeKind == ImplTypeKind.Class)
                {
                    var classDecl = MakeClassWithPublicProperties(
                        scenario.TypeName,
                        (scenario.PropType, scenario.PropName, scenario.TypeHasGet, scenario.TypeHasSet));
                    typeItem = MakeClassTypeItem(classDecl);
                    typeSymbolKind = SymbolKind.Class;
                }
                else
                {
                    // Build struct with public property
                    var accessors = new List<AccessorDeclarationNode>();
                    if (scenario.TypeHasGet)
                        accessors.Add(new AutoGetAccessorAccessorDeclarationNode());
                    if (scenario.TypeHasSet)
                        accessors.Add(new AutoSetAccessorAccessorDeclarationNode());

                    var propMember = (MemberDeclarationNode)new PropertyMemberMemberDeclarationNode
                    {
                        Property = new BlockPropertyPropertyDeclarationNode
                        {
                            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                            Type = MakePrimitiveTypeRef(scenario.PropType),
                            Name = Tok(scenario.PropName),
                            Accessors = accessors.ToImmutableArray(),
                        },
                    };

                    var structDecl = new StructDeclarationNode
                    {
                        Name = Tok(scenario.TypeName),
                        Members = ImmutableArray.Create(propMember),
                    };
                    typeItem = MakeStructTypeItem(structDecl);
                    typeSymbolKind = SymbolKind.Struct;
                }

                var ast = MakeCompilationUnit(typeItem, MakeTraitTypeItem(traitDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, typeSymbolKind);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return model.TraitSatisfactionMap.Satisfies(type, trait)
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} to satisfy {scenario.TraitName} via property match");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }

    /// <summary>
    /// Property 3g: For any trait field member, a public property on the type with
    /// the same name and compatible type SHALL satisfy the trait field.
    ///
    /// **Validates: Requirements 5.3**
    /// </summary>
    [Fact]
    public void FieldMatchedByProperty_TypeSatisfiesTrait()
    {
        var property = Prop.ForAll(
            GenFieldMatchedByPropertyScenario().ToArbitrary(),
            scenario =>
            {
                // Build trait with a field member
                var traitDecl = MakeTraitWithFields(
                    scenario.TraitName,
                    (scenario.FieldType, scenario.FieldName));

                // Build class with a public PROPERTY (not field) matching the trait field
                var classDecl = MakeClassWithPublicProperties(
                    scenario.TypeName,
                    (scenario.FieldType, scenario.FieldName, true, true));

                var ast = MakeCompilationUnit(
                    MakeClassTypeItem(classDecl),
                    MakeTraitTypeItem(traitDecl));
                var (model, _) = BuildModelWithDiagnostics(ast);

                var type = FindType(model, scenario.TypeName, SymbolKind.Class);
                var trait = FindTrait(model, scenario.TraitName);

                if (type is null || trait is null)
                    return false.ToProperty().Label("Type or trait not found");

                return model.TraitSatisfactionMap.Satisfies(type, trait)
                    .ToProperty()
                    .Label($"Expected {scenario.TypeName} property to satisfy trait {scenario.TraitName} field");
            });

        property.Check(Config.Default.WithMaxTest(100));
    }
}
