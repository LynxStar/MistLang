// Feature: default-keyword
// Property 6: Default value computation from field initializers and zero-initialization — **Validates: Requirements 6.1, 6.2**
// Property 7: Explicit override replaces inferred default — **Validates: Requirements 6.4**
// Property 8: Type binding resolution — **Validates: Requirements 6.5, 6.6**
// Property 9: Inheritance of default values — **Validates: Requirements 6.7, 6.8, 10.1, 10.2, 10.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for default value computation in the semantic model.
///
/// Property 6: For any defaultable type, each field's computed default value SHALL
/// equal the field's declared initializer value if one exists, or the zero-initialization
/// value for the field's type if no initializer is declared.
///
/// Property 7: For any type with an explicit default block containing a field override
/// <c>fieldName = value</c>, the computed default value for that field SHALL equal the
/// override value, regardless of any field initializer or zero-initialization value.
///
/// Property 8: For any type with an explicit default block containing a type binding
/// <c>fieldName: ConcreteType</c> (with or without nested overrides), the computed
/// default value for that field SHALL use the default value of ConcreteType, with any
/// nested overrides applied on top.
///
/// Property 9: For any derived class whose base class is defaultable, the derived
/// class's default values for inherited fields SHALL equal the base class's default
/// values for those fields, unless the derived class's explicit default block overrides
/// them.
///
/// Note: Hand-crafted ASTs cannot produce non-null ConstantValue for field initializers
/// or explicit overrides because <c>SemanticModelBuilder.UnwrapToPrimary</c> cannot
/// traverse the binary expression wrapper nodes (MultiplicativeNode, AdditiveNode, etc.)
/// when their Right child is null. Therefore, these tests verify:
/// - Correct <see cref="DefaultValueKind"/> classification
/// - Correct zero-initialization values (which are computed by type, not by AST traversal)
/// - Correct <see cref="DefaultFieldValue.ConcreteType"/> for type bindings
/// - Correct nested override structure for type bindings
/// </summary>
public class DefaultValueComputationPropertyTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private static (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(
            compilation, ImmutableArray.Create(ast), ambientTypeResolver: null,
            namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static PrimitiveTypeNode MakePrimitiveType(string typeName)
    {
        return typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "short" => new ShortTypePrimitiveTypeNode(),
            "byte" => new ByteTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        return new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = MakePrimitiveType(typeName),
            }
        };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string typeName)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = MakeToken(typeName) },
            }
        };
    }

    private static FieldDeclarationNode MakeField(
        string fieldName, TypeReferenceNode typeRef, ExpressionNode? initializer = null)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = typeRef,
            Initializer = initializer,
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static MemberDeclarationNode MakeFieldMember(FieldDeclarationNode field)
    {
        return new FieldMemberMemberDeclarationNode { Field = field };
    }

    private static MemberDeclarationNode MakeDefaultBlockMember(
        ImmutableArray<DefaultFieldInitializerNode> entries)
    {
        return new DefaultBlockMemberMemberDeclarationNode
        {
            DefaultBlock = new DefaultBlockDeclarationNode { Entries = entries },
        };
    }

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    private static TypeBodyDeclarationNode MakeClassDecl(
        string name, ImmutableArray<MemberDeclarationNode> members)
    {
        return new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static TypeBodyDeclarationNode MakeClassDeclWithBase(
        string name, string baseClassName, ImmutableArray<MemberDeclarationNode> members)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(baseClassName) };
        var baseList = new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members,
            BaseClause = baseList,
            BaseList = baseList
        };
        return classDecl;
    }

    private static TypeBodyDeclarationNode MakeStructDecl(
        string name, ImmutableArray<MemberDeclarationNode> members)
    {
        return new TypeBodyDeclarationNode
        {
            Keyword = new StructKeywordTypeKeywordNode(),
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static TypeDeclarationNode WrapClass(TypeBodyDeclarationNode classDecl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = classDecl };
    }

    private static TypeDeclarationNode WrapStruct(TypeBodyDeclarationNode structDecl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = structDecl };
    }

    private static TypeDeclarationNode WrapAbstractType(TypeBodyDeclarationNode abstractDecl)
    {
        return new BodyTypeTypeDeclarationNode { TypeBody = abstractDecl };
    }

    // ─── Expression helpers ──────────────────────────────────────────────

    private static ExpressionNode WrapPrimaryAsExpression(PrimaryNode primary)
    {
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static ExpressionNode MakeIntLiteralExpression(string value)
    {
        return WrapPrimaryAsExpression(new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        });
    }

    // ─── Zero-value lookup (mirrors SemanticModelBuilder.GetZeroValue) ───

    private static object? ExpectedZeroValue(string typeName)
    {
        return typeName switch
        {
            "int" => 0,
            "bool" => false,
            "string" => "",
            "double" => 0.0d,
            "float" => 0.0f,
            "char" => '\0',
            "long" => 0L,
            "short" => (short)0,
            "byte" => (byte)0,
            _ => null,
        };
    }

    // ─── Scenario records ────────────────────────────────────────────────

    /// <summary>
    /// Scenario for Property 6: a type with a mix of fields that have initializers
    /// and fields that are zero-initialized.
    /// </summary>
#pragma warning disable CA1034
    public record FieldInitAndZeroScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<FieldSpec> Fields)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f =>
                f.HasInitializer ? $"{f.FieldType} {f.FieldName} = ..." : $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} {{ {fields} }}";
        }
    }

    /// <summary>
    /// Describes a single field in a scenario: its name, type, and whether it has an initializer.
    /// </summary>
    public record FieldSpec(
        string FieldName,
        string FieldType,
        bool HasInitializer);

    /// <summary>
    /// Scenario for Property 7: a type with fields and an explicit default block
    /// that overrides some of them.
    /// </summary>
    public record ExplicitOverrideScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<FieldSpec> Fields,
        ImmutableArray<string> OverriddenFieldNames)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f =>
                f.HasInitializer ? $"int {f.FieldName} = ..." : $"int {f.FieldName}"));
            var overrides = string.Join(", ", OverriddenFieldNames.Select(n => $"{n} = ..."));
            return $"{kind} {TypeName} {{ {fields} }} default {{ {overrides} }}";
        }
    }

    /// <summary>
    /// Scenario for Property 8: a type with an abstract-typed field and a default block
    /// that provides a type binding.
    /// </summary>
    public record TypeBindingScenario(
        string TypeName,
        string AbstractName,
        string AbstractFieldName,
        string ConcreteTypeName,
        int ConcreteFieldCount,
        bool HasNestedOverrides)
    {
        public override string ToString()
        {
            var nested = HasNestedOverrides ? " { alpha = ... }" : "";
            return $"class {TypeName} {{ {AbstractName} {AbstractFieldName}; " +
                   $"default {{ {AbstractFieldName}: {ConcreteTypeName}{nested} }} }}";
        }
    }

    /// <summary>
    /// Scenario for Property 9: a base class with primitive fields and a derived class
    /// that inherits from it, optionally overriding some base fields via an explicit
    /// default block.
    /// </summary>
    public record InheritanceScenario(
        string BaseTypeName,
        ImmutableArray<FieldSpec> BaseFields,
        string DerivedTypeName,
        ImmutableArray<FieldSpec> DerivedFields,
        ImmutableArray<string> OverriddenBaseFieldNames)
    {
        public override string ToString()
        {
            var baseFields = string.Join(", ", BaseFields.Select(f =>
                f.HasInitializer ? $"{f.FieldType} {f.FieldName} = ..." : $"{f.FieldType} {f.FieldName}"));
            var derivedFields = string.Join(", ", DerivedFields.Select(f =>
                f.HasInitializer ? $"{f.FieldType} {f.FieldName} = ..." : $"{f.FieldType} {f.FieldName}"));
            var overrides = OverriddenBaseFieldNames.Length > 0
                ? $" default {{ {string.Join(", ", OverriddenBaseFieldNames.Select(n => $"{n} = ..."))} }}"
                : "";
            return $"class {BaseTypeName} {{ {baseFields} }} " +
                   $"class {DerivedTypeName} : {BaseTypeName} {{ {derivedFields} }}{overrides}";
        }
    }
#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string", "double", "float", "char", "long", "short", "byte"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon"];

    private static readonly string[] TypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent"];

    private static readonly string[] AbstractNames =
        ["IRenderer", "ILogger", "IService"];

    private static readonly string[] ConcreteTypeNames =
        ["OpenGLRenderer", "ConsoleLogger", "HttpService"];

    private static readonly string[] BaseTypeNames =
        ["BaseWidget", "BaseConfig", "BaseEngine"];

    private static readonly string[] DerivedTypeNames =
        ["DerivedWidget", "DerivedConfig", "DerivedEngine"];

    /// <summary>
    /// Field names reserved for derived class's own fields (distinct from base field names).
    /// </summary>
    private static readonly string[] DerivedFieldNames =
        ["zeta", "eta", "theta"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with 2–4 fields, some with initializers and some without.
    /// At least one field has an initializer and at least one does not.
    /// </summary>
    private static Gen<FieldInitAndZeroScenario> GenFieldInitAndZeroScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(2, 4)
               from fields in GenMixedFields(fieldCount)
               select new FieldInitAndZeroScenario(isStruct, typeName, fields);
    }

    private static Gen<ImmutableArray<FieldSpec>> GenMixedFields(int count)
    {
        // Build fields one at a time, then ensure at least one has an initializer
        // and at least one does not.
        var result =
            from ft in Gen.Elements(PrimitiveTypes)
            from hasInit in Gen.Elements(true, false)
            select ImmutableArray.Create(new FieldSpec(FieldNames[0], ft, hasInit));

        for(var i = 1; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from ft in Gen.Elements(PrimitiveTypes)
                     from hasInit in Gen.Elements(true, false)
                     select arr.Add(new FieldSpec(FieldNames[idx], ft, hasInit));
        }

        // Ensure at least one true and one false in HasInitializer
        return from fields in result
               let fixed_ = EnsureMixed(fields)
               select fixed_;
    }

    private static ImmutableArray<FieldSpec> EnsureMixed(ImmutableArray<FieldSpec> fields)
    {
        if(fields.All(f => f.HasInitializer))
        {
            return fields.SetItem(0, fields[0] with { HasInitializer = false });
        }

        if(fields.All(f => !f.HasInitializer))
        {
            return fields.SetItem(0, fields[0] with { HasInitializer = true });
        }

        return fields;
    }

    /// <summary>
    /// Generates a scenario with 2–4 int fields and an explicit default block
    /// that overrides 1–2 of them.
    /// </summary>
    private static Gen<ExplicitOverrideScenario> GenExplicitOverrideScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(2, 4)
               from overrideCount in Gen.Choose(1, 2)
               let effectiveOverrideCount = Math.Min(overrideCount, fieldCount)
               from fields in GenIntFieldSpecs(fieldCount)
               let overriddenNames = fields.Take(effectiveOverrideCount)
                   .Select(f => f.FieldName).ToImmutableArray()
               select new ExplicitOverrideScenario(isStruct, typeName, fields, overriddenNames);
    }

    private static Gen<ImmutableArray<FieldSpec>> GenIntFieldSpecs(int count)
    {
        var result =
            from hasInit in Gen.Elements(true, false)
            select ImmutableArray.Create(new FieldSpec(FieldNames[0], "int", hasInit));

        for(var i = 1; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from hasInit in Gen.Elements(true, false)
                     select arr.Add(new FieldSpec(FieldNames[idx], "int", hasInit));
        }

        return result;
    }

    /// <summary>
    /// Generates a scenario with an abstract-typed field and a type binding to a concrete class.
    /// </summary>
    private static Gen<TypeBindingScenario> GenTypeBindingScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from abstractIdx in Gen.Choose(0, AbstractNames.Length - 1)
               from concreteFieldCount in Gen.Choose(1, 3)
               from hasNestedOverrides in Gen.Elements(true, false)
               select new TypeBindingScenario(
                   typeName,
                   AbstractNames[abstractIdx],
                   "target",
                   ConcreteTypeNames[abstractIdx],
                   concreteFieldCount,
                   hasNestedOverrides);
    }

    /// <summary>
    /// Generates a scenario with a base class (1–3 primitive fields) and a derived class
    /// (1–2 own primitive fields) that inherits from the base. Optionally, the derived
    /// class has an explicit default block that overrides 0–2 of the base fields.
    /// </summary>
    private static Gen<InheritanceScenario> GenInheritanceScenario()
    {
        return from typeIdx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from baseFieldCount in Gen.Choose(1, 3)
               from derivedFieldCount in Gen.Choose(1, 2)
               from overrideCount in Gen.Choose(0, 2)
               from baseFields in GenPrimitiveFieldSpecs(baseFieldCount, FieldNames)
               from derivedFields in GenPrimitiveFieldSpecs(derivedFieldCount, DerivedFieldNames)
               let effectiveOverrideCount = Math.Min(overrideCount, baseFieldCount)
               let overriddenNames = effectiveOverrideCount > 0
                   ? baseFields.Take(effectiveOverrideCount)
                       .Select(f => f.FieldName).ToImmutableArray()
                   : ImmutableArray<string>.Empty
               select new InheritanceScenario(
                   BaseTypeNames[typeIdx],
                   baseFields,
                   DerivedTypeNames[typeIdx],
                   derivedFields,
                   overriddenNames);
    }

    private static Gen<ImmutableArray<FieldSpec>> GenPrimitiveFieldSpecs(
        int count, string[] namePool)
    {
        var result =
            from ft in Gen.Elements(PrimitiveTypes)
            from hasInit in Gen.Elements(true, false)
            select ImmutableArray.Create(new FieldSpec(namePool[0], ft, hasInit));

        for(var i = 1; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from ft in Gen.Elements(PrimitiveTypes)
                     from hasInit in Gen.Elements(true, false)
                     select arr.Add(new FieldSpec(namePool[idx], ft, hasInit));
        }

        return result;
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<FieldInitAndZeroScenario> ArbitraryFieldInitAndZeroScenario()
    {
        return GenFieldInitAndZeroScenario().ToArbitrary();
    }

    public static Arbitrary<ExplicitOverrideScenario> ArbitraryExplicitOverrideScenario()
    {
        return GenExplicitOverrideScenario().ToArbitrary();
    }

    public static Arbitrary<TypeBindingScenario> ArbitraryTypeBindingScenario()
    {
        return GenTypeBindingScenario().ToArbitrary();
    }

    public static Arbitrary<InheritanceScenario> ArbitraryInheritanceScenario()
    {
        return GenInheritanceScenario().ToArbitrary();
    }

    // ─── AST builders for scenarios ──────────────────────────────────────

    /// <summary>
    /// Builds an AST for a Property 6 scenario: a type with a mix of fields
    /// that have initializers and fields that are zero-initialized.
    /// </summary>
    private static CompilationUnitNode BuildFieldInitAndZeroAst(FieldInitAndZeroScenario scenario)
    {
        var members = scenario.Fields
            .Select(f =>
            {
                var typeRef = MakePrimitiveTypeRef(f.FieldType);
                var initializer = f.HasInitializer
                    ? MakeIntLiteralExpression("0") // Placeholder; ConstantValue extraction is limited
                    : null;
                return MakeFieldMember(MakeField(f.FieldName, typeRef, initializer));
            })
            .ToImmutableArray();

        var typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        return MakeCompilationUnit(typeDecl);
    }

    /// <summary>
    /// Builds an AST for a Property 7 scenario: a type with int fields and an
    /// explicit default block that overrides some of them.
    /// </summary>
    private static CompilationUnitNode BuildExplicitOverrideAst(ExplicitOverrideScenario scenario)
    {
        var fieldMembers = scenario.Fields
            .Select(f =>
            {
                var typeRef = MakePrimitiveTypeRef(f.FieldType);
                var initializer = f.HasInitializer
                    ? MakeIntLiteralExpression("0")
                    : null;
                return MakeFieldMember(MakeField(f.FieldName, typeRef, initializer));
            })
            .ToList();

        // Build default block entries from the overrides
        var entries = scenario.OverriddenFieldNames
            .Select(name => (DefaultFieldInitializerNode)new DefaultFieldOverrideDefaultFieldInitializerNode
            {
                FieldName = MakeToken(name),
                Value = MakeIntLiteralExpression("99"),
            })
            .ToImmutableArray();

        var allMembers = fieldMembers
            .Cast<MemberDeclarationNode>()
            .Append(MakeDefaultBlockMember(entries))
            .ToImmutableArray();

        var typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, allMembers))
            : WrapClass(MakeClassDecl(scenario.TypeName, allMembers));

        return MakeCompilationUnit(typeDecl);
    }

    /// <summary>
    /// Builds an AST for a Property 8 scenario: a type with an abstract-typed field
    /// and a default block with a type binding to a concrete class.
    /// </summary>
    private static CompilationUnitNode BuildTypeBindingAst(TypeBindingScenario scenario)
    {
        // 1. Declare the abstract type
        var abstractDecl = new TypeBodyDeclarationNode
        {
            Keyword = new AbstractKeywordTypeKeywordNode(),
            Name = MakeToken(scenario.AbstractName),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };

        // 2. Declare the concrete class with int fields
        var concreteMembers = Enumerable.Range(0, scenario.ConcreteFieldCount)
            .Select(i => MakeFieldMember(MakeField(FieldNames[i], MakePrimitiveTypeRef("int"))))
            .ToImmutableArray();

        var concreteClassDecl = MakeClassDecl(scenario.ConcreteTypeName, concreteMembers);

        // 3. Declare the main type with the abstract-typed field and default block
        var abstractFieldMember = MakeFieldMember(
            MakeField(scenario.AbstractFieldName, MakeNamedTypeRef(scenario.AbstractName)));

        // Build the type binding entry
        var nestedOverrideNodes = scenario.HasNestedOverrides
            ? ImmutableArray.Create(new DefaultNestedOverrideNode
            {
                FieldName = MakeToken(FieldNames[0]), // Override the first concrete field
                Value = MakeIntLiteralExpression("99"),
            })
            : ImmutableArray<DefaultNestedOverrideNode>.Empty;

        var typeBindingEntry = (DefaultFieldInitializerNode)new DefaultTypeBindingDefaultFieldInitializerNode
        {
            FieldName = MakeToken(scenario.AbstractFieldName),
            TypeName = MakeNamedTypeRef(scenario.ConcreteTypeName),
            NestedEntries = nestedOverrideNodes,
        };

        var defaultBlockMember = MakeDefaultBlockMember(ImmutableArray.Create(typeBindingEntry));

        var mainMembers = ImmutableArray.Create(abstractFieldMember, defaultBlockMember);
        var mainClassDecl = MakeClassDecl(scenario.TypeName, mainMembers);

        return MakeCompilationUnit(
            WrapAbstractType(abstractDecl),
            WrapClass(concreteClassDecl),
            WrapClass(mainClassDecl));
    }

    /// <summary>
    /// Builds an AST for a Property 9 scenario: a base class with primitive fields
    /// and a derived class that inherits from it, optionally with an explicit default
    /// block that overrides some base fields.
    /// </summary>
    private static CompilationUnitNode BuildInheritanceAst(InheritanceScenario scenario)
    {
        // 1. Build the base class with its fields
        var baseMembers = scenario.BaseFields
            .Select(f =>
            {
                var typeRef = MakePrimitiveTypeRef(f.FieldType);
                var initializer = f.HasInitializer
                    ? MakeIntLiteralExpression("0")
                    : null;
                return MakeFieldMember(MakeField(f.FieldName, typeRef, initializer));
            })
            .ToImmutableArray();

        var baseClassDecl = MakeClassDecl(scenario.BaseTypeName, baseMembers);

        // 2. Build the derived class with its own fields
        var derivedFieldMembers = scenario.DerivedFields
            .Select(f =>
            {
                var typeRef = MakePrimitiveTypeRef(f.FieldType);
                var initializer = f.HasInitializer
                    ? MakeIntLiteralExpression("0")
                    : null;
                return MakeFieldMember(MakeField(f.FieldName, typeRef, initializer));
            })
            .ToList();

        // 3. Optionally add an explicit default block that overrides base fields
        ImmutableArray<MemberDeclarationNode> derivedMembers;
        if(scenario.OverriddenBaseFieldNames.Length > 0)
        {
            var entries = scenario.OverriddenBaseFieldNames
                .Select(name => (DefaultFieldInitializerNode)
                    new DefaultFieldOverrideDefaultFieldInitializerNode
                    {
                        FieldName = MakeToken(name),
                        Value = MakeIntLiteralExpression("42"),
                    })
                .ToImmutableArray();

            derivedMembers = derivedFieldMembers
                .Cast<MemberDeclarationNode>()
                .Append(MakeDefaultBlockMember(entries))
                .ToImmutableArray();
        }
        else
        {
            derivedMembers = derivedFieldMembers
                .Cast<MemberDeclarationNode>()
                .ToImmutableArray();
        }

        var derivedClassDecl = MakeClassDeclWithBase(
            scenario.DerivedTypeName, scenario.BaseTypeName, derivedMembers);

        return MakeCompilationUnit(
            WrapClass(baseClassDecl),
            WrapClass(derivedClassDecl));
    }

    // ─── Helper to extract DefaultValues from a type symbol ──────────────

    private static IReadOnlyDictionary<string, DefaultFieldValue>? GetDefaultValues(
        ISemanticModel model, string typeName)
    {
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == typeName &&
                                 s.Kind is SymbolKind.Class or SymbolKind.Struct);

        return typeSymbol?.DeclaredType switch
        {
            ClassType ct => ct.DefaultValues,
            StructType st => st.DefaultValues,
            _ => null,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Default value computation from field initializers and
    //             zero-initialization
    //
    // **Validates: Requirements 6.1, 6.2**
    //
    // For any defaultable type, each field's computed default value SHALL
    // equal the field's declared initializer value if one exists, or the
    // zero-initialization value for the field's type if no initializer is
    // declared.
    //
    // Verified aspects:
    // - Fields WITH initializers → ValueKind == FieldInitializer
    // - Fields WITHOUT initializers → ValueKind == ZeroInitialized
    //   AND ConstantValue matches the zero value for the field's type
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6: Fields with initializers are classified as FieldInitializer;
    /// fields without initializers are classified as ZeroInitialized with the
    /// correct zero value for their type.
    ///
    /// **Validates: Requirements 6.1, 6.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultValueComputationPropertyTests) }, MaxTest = 100)]
    public bool FieldInitializersAndZeroInit_ProduceCorrectValueKinds(
        FieldInitAndZeroScenario scenario)
    {
        var ast = BuildFieldInitAndZeroAst(scenario);
        var (model, _) = BuildModel(ast);

        var defaultValues = GetDefaultValues(model, scenario.TypeName);
        if(defaultValues is null)
        {
            return false;
        }

        foreach(var field in scenario.Fields)
        {
            if(!defaultValues.TryGetValue(field.FieldName, out var dfv))
            {
                return false;
            }

            if(field.HasInitializer)
            {
                // Field with initializer → FieldInitializer kind
                if(dfv.ValueKind != DefaultValueKind.FieldInitializer)
                {
                    return false;
                }
            }
            else
            {
                // Field without initializer → ZeroInitialized kind with correct zero value
                if(dfv.ValueKind != DefaultValueKind.ZeroInitialized)
                {
                    return false;
                }

                var expectedZero = ExpectedZeroValue(field.FieldType);
                if(!Equals(dfv.ConstantValue, expectedZero))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Explicit override replaces inferred default
    //
    // **Validates: Requirements 6.4**
    //
    // For any type with an explicit default block containing a field override
    // fieldName = value, the computed default value for that field SHALL equal
    // the override value, regardless of any field initializer or
    // zero-initialization value.
    //
    // Verified aspects:
    // - Overridden fields → ValueKind == ExplicitOverride
    // - Non-overridden fields with initializers → ValueKind == FieldInitializer
    // - Non-overridden fields without initializers → ValueKind == ZeroInitialized
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7: Overridden fields have ValueKind == ExplicitOverride.
    /// Non-overridden fields retain their original ValueKind.
    ///
    /// **Validates: Requirements 6.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultValueComputationPropertyTests) }, MaxTest = 100)]
    public bool ExplicitOverride_ReplacesInferredDefault(ExplicitOverrideScenario scenario)
    {
        var ast = BuildExplicitOverrideAst(scenario);
        var (model, _) = BuildModel(ast);

        var defaultValues = GetDefaultValues(model, scenario.TypeName);
        if(defaultValues is null)
        {
            return false;
        }

        var overriddenSet = scenario.OverriddenFieldNames.ToHashSet();

        foreach(var field in scenario.Fields)
        {
            if(!defaultValues.TryGetValue(field.FieldName, out var dfv))
            {
                return false;
            }

            if(overriddenSet.Contains(field.FieldName))
            {
                // This field was overridden → ExplicitOverride kind
                if(dfv.ValueKind != DefaultValueKind.ExplicitOverride)
                {
                    return false;
                }
            }
            else
            {
                // Not overridden → should retain original value kind
                if(field.HasInitializer)
                {
                    if(dfv.ValueKind != DefaultValueKind.FieldInitializer)
                    {
                        return false;
                    }
                }
                else
                {
                    if(dfv.ValueKind != DefaultValueKind.ZeroInitialized)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Type binding resolution
    //
    // **Validates: Requirements 6.5, 6.6**
    //
    // For any type with an explicit default block containing a type binding
    // fieldName: ConcreteType (with or without nested overrides), the computed
    // default value for that field SHALL use the default value of ConcreteType,
    // with any nested overrides applied on top.
    //
    // Verified aspects:
    // - Type-bound fields → ValueKind == TypeBinding
    // - ConcreteType matches the bound type name
    // - Nested overrides are present when specified, with ExplicitOverride kind
    // - No nested overrides when none specified
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 8: Type-bound fields have ValueKind == TypeBinding,
    /// ConcreteType matches the bound type, and nested overrides are applied.
    ///
    /// **Validates: Requirements 6.5, 6.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultValueComputationPropertyTests) }, MaxTest = 100)]
    public bool TypeBinding_ResolvesToConcreteTypeDefault_WithNestedOverrides(
        TypeBindingScenario scenario)
    {
        var ast = BuildTypeBindingAst(scenario);
        var (model, _) = BuildModel(ast);

        var defaultValues = GetDefaultValues(model, scenario.TypeName);
        if(defaultValues is null)
        {
            return false;
        }

        // The abstract-typed field should have a TypeBinding default value
        if(!defaultValues.TryGetValue(scenario.AbstractFieldName, out var dfv))
        {
            return false;
        }

        if(dfv.ValueKind != DefaultValueKind.TypeBinding)
        {
            return false;
        }

        // ConcreteType should match the bound type
        if(dfv.ConcreteType is null || dfv.ConcreteType.DisplayName != scenario.ConcreteTypeName)
        {
            return false;
        }

        // Verify nested overrides
        if(scenario.HasNestedOverrides)
        {
            if(dfv.NestedOverrides is null || dfv.NestedOverrides.Count == 0)
            {
                return false;
            }

            // The first concrete field should be overridden
            var overriddenFieldName = FieldNames[0];
            if(!dfv.NestedOverrides.TryGetValue(overriddenFieldName, out var nestedDfv))
            {
                return false;
            }

            if(nestedDfv.ValueKind != DefaultValueKind.ExplicitOverride)
            {
                return false;
            }
        }
        else
        {
            // No nested overrides — NestedOverrides should be null or empty
            if(dfv.NestedOverrides is not null && dfv.NestedOverrides.Count > 0)
            {
                return false;
            }
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Inheritance of default values
    //
    // **Validates: Requirements 6.7, 6.8, 10.1, 10.2, 10.3**
    //
    // For any derived class whose base class is defaultable, the derived
    // class's default values for inherited fields SHALL equal the base
    // class's default values for those fields, unless the derived class's
    // explicit default block overrides them.
    //
    // Verified aspects:
    // - Inherited base fields with no override → ValueKind == InheritedDefault
    // - Derived class's own new fields → appropriate ValueKind
    //   (FieldInitializer or ZeroInitialized)
    // - Overridden base fields → ValueKind == ExplicitOverride
    // - Derived class DefaultValues contains entries for all fields
    //   (inherited + own)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 9: Inherited base fields have ValueKind == InheritedDefault
    /// (unless overridden), derived class's own fields have appropriate
    /// ValueKinds, and overridden base fields have ValueKind == ExplicitOverride.
    ///
    /// **Validates: Requirements 6.7, 6.8, 10.1, 10.2, 10.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultValueComputationPropertyTests) }, MaxTest = 100)]
    public bool InheritedDefaults_MatchBaseClassDefaults_UnlessOverridden(
        InheritanceScenario scenario)
    {
        var ast = BuildInheritanceAst(scenario);
        var (model, _) = BuildModel(ast);

        // The derived class must have default values
        var derivedDefaults = GetDefaultValues(model, scenario.DerivedTypeName);
        if(derivedDefaults is null)
        {
            return false;
        }

        var overriddenSet = scenario.OverriddenBaseFieldNames.ToHashSet();

        // 1. Verify inherited base fields
        foreach(var baseField in scenario.BaseFields)
        {
            if(!derivedDefaults.TryGetValue(baseField.FieldName, out var dfv))
            {
                return false;
            }

            if(overriddenSet.Contains(baseField.FieldName))
            {
                // Overridden base field → ExplicitOverride
                if(dfv.ValueKind != DefaultValueKind.ExplicitOverride)
                {
                    return false;
                }
            }
            else
            {
                // Non-overridden inherited field → InheritedDefault
                if(dfv.ValueKind != DefaultValueKind.InheritedDefault)
                {
                    return false;
                }
            }
        }

        // 2. Verify derived class's own new fields
        foreach(var derivedField in scenario.DerivedFields)
        {
            if(!derivedDefaults.TryGetValue(derivedField.FieldName, out var dfv))
            {
                return false;
            }

            if(derivedField.HasInitializer)
            {
                if(dfv.ValueKind != DefaultValueKind.FieldInitializer)
                {
                    return false;
                }
            }
            else
            {
                if(dfv.ValueKind != DefaultValueKind.ZeroInitialized)
                {
                    return false;
                }

                var expectedZero = ExpectedZeroValue(derivedField.FieldType);
                if(!Equals(dfv.ConstantValue, expectedZero))
                {
                    return false;
                }
            }
        }

        // 3. Verify total count: all base fields + all derived fields
        var expectedCount = scenario.BaseFields.Length + scenario.DerivedFields.Length;
        if(derivedDefaults.Count != expectedCount)
        {
            return false;
        }

        return true;
    }
}