// Feature: optional-defaultability, Properties 1, 2: Classification
// Property 1: Constructor presence suppresses defaultability diagnostic — **Validates: Requirements 1.1, 1.2, 4.2**
// Property 2: Constructor-less types with non-defaultable fields produce an error — **Validates: Requirements 1.3, 11.1**

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for optional defaultability classification.
///
/// Property 1: For any class or struct type that has at least one user-defined constructor
/// and no explicit <c>default {}</c> block, the compiler SHALL classify it as
/// <see cref="DefaultabilityStatus.NotDefaultable"/> and SHALL NOT emit any diagnostic error
/// (regardless of whether its fields are defaultable).
///
/// Property 2: For any class or struct type that has no user-defined constructor and has at
/// least one field whose type is non-defaultable (abstract type or another non-defaultable
/// class/struct), the compiler SHALL emit a diagnostic error.
/// </summary>
public class OptionalDefaultabilityClassificationPropertyTests
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

    private static PrimitiveTypeNode MakePrimitiveType(string typeName) => typeName switch
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

    private static FieldDeclarationNode MakeField(string fieldName, TypeReferenceNode typeRef)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = typeRef,
            Modifiers = ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static FieldDeclarationNode MakePrimitiveField(string fieldName, string typeName)
    {
        return MakeField(fieldName, new TypeReferenceNode
        {
            First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = MakePrimitiveType(typeName),
            }
        });
    }

    private static FieldDeclarationNode MakeNamedTypeField(string fieldName, string typeName)
    {
        return MakeField(fieldName, new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MakeToken(typeName),
                },
            }
        });
    }

    private static MemberDeclarationNode MakeFieldMember(FieldDeclarationNode field)
    {
        return new FieldMemberMemberDeclarationNode { Field = field };
    }

    private static MemberDeclarationNode MakeConstructorMember(string className)
    {
        return new ConstructorMemberMemberDeclarationNode
        {
            Constructor = new ConstructorDeclarationNode
            {
                Name = MakeToken(className),
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode
                {
                    Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
                },
            },
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

    private static ClassDeclarationNode MakeClassDecl(
        string name, ImmutableArray<MemberDeclarationNode> members)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static StructDeclarationNode MakeStructDecl(
        string name, ImmutableArray<MemberDeclarationNode> members)
    {
        return new StructDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode classDecl)
    {
        return new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl };
    }

    private static TypeDeclarationNode WrapStruct(StructDeclarationNode structDecl)
    {
        return new StructTypeTypeDeclarationNode { StructDeclaration = structDecl };
    }

    private static TypeDeclarationNode WrapAbstractType(AbstractDeclarationNode abstractDecl)
    {
        return new AbstractTypeTypeDeclarationNode { AbstractDeclaration = abstractDecl };
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario for Property 1: a type with a constructor and no default block.
    /// Fields may be any mix of defaultable and non-defaultable types.
    /// The type should always be classified as NotDefaultable with no diagnostics.
    /// </summary>
    public record ConstructorSuppressesDiagnosticScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType, bool IsNonDefaultable)> Fields,
        string? NonDefaultableTypeName)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f =>
                $"{(f.IsNonDefaultable ? "[non-defaultable] " : "")}{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} + ctor {{ {fields} }}";
        }
    }

    /// <summary>
    /// Scenario for Property 2: a type with no constructor and at least one non-defaultable field.
    /// The compiler should emit a diagnostic error.
    /// </summary>
    public record ConstructorlessNonDefaultableScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType, bool IsNonDefaultable)> Fields,
        NonDefaultableKind FieldKind,
        string NonDefaultableTypeName)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f =>
                $"{(f.IsNonDefaultable ? $"[{FieldKind}] " : "")}{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} (no ctor) {{ {fields} }}";
        }
    }

    /// <summary>
    /// The kind of non-defaultable type used as a field.
    /// </summary>
    public enum NonDefaultableKind
    {
        AbstractType,
    }

#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string", "double", "float", "char", "long", "short", "byte"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];

    private static readonly string[] TypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent", "Point", "Matrix", "Record"];

    private static readonly string[] AbstractNames =
        ["IRenderer", "ILogger", "IService", "IHandler", "IProcessor"];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<ImmutableArray<(string FieldName, string FieldType, bool IsNonDefaultable)>> GenMixedFields(
        int primitiveCount, int nonDefaultableCount, string nonDefaultableTypeName)
    {
        if(primitiveCount + nonDefaultableCount == 0)
        {
            return Gen.Constant(ImmutableArray<(string, string, bool)>.Empty);
        }

        Gen<ImmutableArray<(string, string, bool)>> result =
            Gen.Constant(ImmutableArray<(string, string, bool)>.Empty);

        for(var i = 0; i < primitiveCount; i++)
        {
            var idx = i;
            result = from arr in result
                     from fieldType in Gen.Elements(PrimitiveTypes)
                     select arr.Add((FieldNames[idx], fieldType, false));
        }

        for(var i = 0; i < nonDefaultableCount; i++)
        {
            var idx = primitiveCount + i;
            result = from arr in result
                     select arr.Add((FieldNames[idx], nonDefaultableTypeName, true));
        }

        return result;
    }

    /// <summary>
    /// Generates scenarios for Property 1: types with constructors.
    /// Includes both cases: fields that are all defaultable, and fields with non-defaultable types.
    /// In both cases, the presence of a constructor should suppress diagnostics.
    /// </summary>
    private static Gen<ConstructorSuppressesDiagnosticScenario> GenConstructorSuppressesScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from primitiveCount in Gen.Choose(0, 3)
               from hasNonDefaultableField in Gen.Elements(true, false)
               let nonDefaultableName = AbstractNames[0]
               let nonDefaultableCount = hasNonDefaultableField ? 1 : 0
               from fields in GenMixedFields(
                   Math.Max(primitiveCount, hasNonDefaultableField ? 0 : 1),
                   nonDefaultableCount,
                   nonDefaultableName)
               select new ConstructorSuppressesDiagnosticScenario(
                   isStruct, typeName, fields,
                   hasNonDefaultableField ? nonDefaultableName : null);
    }

    /// <summary>
    /// Generates scenarios for Property 2: types without constructors that have non-defaultable fields.
    /// </summary>
    private static Gen<ConstructorlessNonDefaultableScenario> GenConstructorlessNonDefaultableScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from nonDefaultableName in Gen.Elements(AbstractNames)
               from primitiveCount in Gen.Choose(0, 3)
               from fields in GenMixedFields(primitiveCount, 1, nonDefaultableName)
               select new ConstructorlessNonDefaultableScenario(
                   isStruct, typeName, fields, NonDefaultableKind.AbstractType, nonDefaultableName);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<ConstructorSuppressesDiagnosticScenario> ArbitraryConstructorSuppresses() =>
        GenConstructorSuppressesScenario().ToArbitrary();

    public static Arbitrary<ConstructorlessNonDefaultableScenario> ArbitraryConstructorlessNonDefaultable() =>
        GenConstructorlessNonDefaultableScenario().ToArbitrary();

    // ─── AST builders for scenarios ──────────────────────────────────────

    /// <summary>
    /// Builds an AST for Property 1: a type with a constructor and mixed fields.
    /// If the scenario includes a non-defaultable field, its backing type (abstract type) is declared.
    /// </summary>
    private static CompilationUnitNode BuildConstructorWithFieldsAst(
        ConstructorSuppressesDiagnosticScenario scenario)
    {
        var members = scenario.Fields
            .Select(f =>
            {
                FieldDeclarationNode field = f.IsNonDefaultable
                    ? MakeNamedTypeField(f.FieldName, f.FieldType)
                    : MakePrimitiveField(f.FieldName, f.FieldType);
                return MakeFieldMember(field);
            })
            .Append(MakeConstructorMember(scenario.TypeName))
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        // If there's a non-defaultable type reference, declare it
        if(scenario.NonDefaultableTypeName is not null)
        {
            var nonDefaultableDecl = WrapAbstractType(new AbstractDeclarationNode
            {
                Name = MakeToken(scenario.NonDefaultableTypeName),
                Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
            });

            return MakeCompilationUnit(nonDefaultableDecl, typeDecl);
        }

        return MakeCompilationUnit(typeDecl);
    }

    /// <summary>
    /// Builds an AST for Property 2: a type without a constructor that has non-defaultable fields.
    /// The non-defaultable type (abstract type) is also declared.
    /// </summary>
    private static CompilationUnitNode BuildConstructorlessNonDefaultableAst(
        ConstructorlessNonDefaultableScenario scenario)
    {
        var members = scenario.Fields
            .Select(f =>
            {
                FieldDeclarationNode field = f.IsNonDefaultable
                    ? MakeNamedTypeField(f.FieldName, f.FieldType)
                    : MakePrimitiveField(f.FieldName, f.FieldType);
                return MakeFieldMember(field);
            })
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        // Declare the non-defaultable type
        TypeDeclarationNode nonDefaultableDecl = WrapAbstractType(new AbstractDeclarationNode
        {
            Name = MakeToken(scenario.NonDefaultableTypeName),
            Members = ImmutableArray<AbstractMemberDeclarationNode>.Empty,
        });

        return MakeCompilationUnit(nonDefaultableDecl, typeDecl);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Constructor presence suppresses defaultability diagnostic
    //
    // **Validates: Requirements 1.1, 1.2, 4.2**
    //
    // For any class or struct type that has at least one user-defined constructor
    // and no explicit default {} block, the compiler SHALL classify it as
    // NotDefaultable and SHALL NOT emit any diagnostic error (regardless of
    // whether its fields are defaultable).
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1: Types with constructors and no default block are classified as
    /// <see cref="DefaultabilityStatus.NotDefaultable"/> and produce zero diagnostics.
    ///
    /// **Validates: Requirements 1.1, 1.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OptionalDefaultabilityClassificationPropertyTests) }, MaxTest = 100)]
    public bool ConstructorPresence_SuppressesDiagnostic_ClassifiedAsNotDefaultable(
        ConstructorSuppressesDiagnosticScenario scenario)
    {
        var ast = BuildConstructorWithFieldsAst(scenario);
        var (model, diagnostics) = BuildModel(ast);

        var expectedKind = scenario.IsStruct ? SymbolKind.Struct : SymbolKind.Class;
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == expectedKind);

        if(typeSymbol is null)
        {
            return false;
        }

        // Check defaultability status is NotDefaultable
        var status = typeSymbol.DeclaredType switch
        {
            ClassType ct => ct.Defaultability,
            StructType st => st.Defaultability,
            _ => DefaultabilityStatus.Unknown,
        };

        if(status != DefaultabilityStatus.NotDefaultable)
        {
            return false;
        }

        // Check that NO defaultability-related diagnostics are emitted for this type
        var hasDefaultabilityDiagnostic = diagnostics
            .Any(d => d.Id == DiagnosticId.DefaultRequiresTypeBinding);

        return !hasDefaultabilityDiagnostic;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Constructor-less types with non-defaultable fields produce an error
    //
    // **Validates: Requirements 1.3, 11.1**
    //
    // For any class or struct type that has no user-defined constructor and has
    // at least one field whose type is non-defaultable (abstract type or
    // another non-defaultable class/struct), the compiler SHALL emit a diagnostic
    // error.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 2: Types without constructors that have non-defaultable fields are classified
    /// as <see cref="DefaultabilityStatus.NotDefaultable"/> and emit
    /// <c>MIST_DEFAULT_REQUIRES_BINDING</c>.
    ///
    /// **Validates: Requirements 1.3, 11.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OptionalDefaultabilityClassificationPropertyTests) }, MaxTest = 100)]
    public bool ConstructorlessType_WithNonDefaultableField_EmitsDiagnostic(
        ConstructorlessNonDefaultableScenario scenario)
    {
        var ast = BuildConstructorlessNonDefaultableAst(scenario);
        var (model, diagnostics) = BuildModel(ast);

        var expectedKind = scenario.IsStruct ? SymbolKind.Struct : SymbolKind.Class;
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == expectedKind);

        if(typeSymbol is null)
        {
            return false;
        }

        // Check defaultability status is NotDefaultable
        var status = typeSymbol.DeclaredType switch
        {
            ClassType ct => ct.Defaultability,
            StructType st => st.Defaultability,
            _ => DefaultabilityStatus.Unknown,
        };

        if(status != DefaultabilityStatus.NotDefaultable)
        {
            return false;
        }

        // Check that MIST_DEFAULT_REQUIRES_BINDING diagnostic was emitted
        var hasRequiresBindingDiagnostic = diagnostics
            .Any(d => d.Id == DiagnosticId.DefaultRequiresTypeBinding);

        return hasRequiresBindingDiagnostic;
    }
}