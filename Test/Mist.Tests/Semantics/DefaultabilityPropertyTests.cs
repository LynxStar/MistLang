// Feature: default-keyword
// Property 4: Defaultability classification correctness — **Validates: Requirements 5.1, 5.2**
// Property 5: Non-defaultable type detection — **Validates: Requirements 5.3, 13.11**

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
/// Property-based tests for defaultability classification in the semantic model.
///
/// Property 4: For any class or struct where all fields have defaultable types and
/// no explicit default block is declared, the semantic model SHALL classify the type
/// as <see cref="DefaultabilityStatus.Inferred"/>. For any class or struct with an
/// explicit default block, the semantic model SHALL classify the type as
/// <see cref="DefaultabilityStatus.Explicit"/>.
///
/// Property 5: For any class or struct that has at least one field with an interface
/// type and no default block providing a type binding for that field, the semantic
/// model SHALL classify the type as <see cref="DefaultabilityStatus.NotDefaultable"/>
/// and SHALL emit a <c>MIST_DEFAULT_REQUIRES_BINDING</c> diagnostic.
/// </summary>
public class DefaultabilityPropertyTests
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
        return MakeField(fieldName, new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
        {
            PrimitiveType = MakePrimitiveType(typeName),
        } });
    }

    private static FieldDeclarationNode MakeNamedTypeField(string fieldName, string typeName)
    {
        return MakeField(fieldName, new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MakeToken(typeName),
            },
        } });
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
            DefaultBlock = new DefaultBlockDeclarationNode
            {
                Entries = entries,
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

    private static TypeDeclarationNode WrapInterface(InterfaceDeclarationNode interfaceDecl)
    {
        return new InterfaceTypeTypeDeclarationNode { InterfaceDeclaration = interfaceDecl };
    }

    // ─── Scenario records ────────────────────────────────────────────────

    /// <summary>
    /// Scenario for Property 4: a type with all-primitive fields.
    /// </summary>
#pragma warning disable CA1034
    public record InferredScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType)> Fields)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} {{ {fields} }}";
        }
    }

    /// <summary>
    /// Scenario for Property 4 (explicit): a type with an explicit default block.
    /// </summary>
    public record ExplicitScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType)> Fields,
        int DefaultBlockEntryCount)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} {{ {fields} }} default({DefaultBlockEntryCount} entries)";
        }
    }

    /// <summary>
    /// Scenario for Property 5: a type with at least one interface field and no default block.
    /// </summary>
    public record NotDefaultableScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType, bool IsInterface)> Fields,
        string InterfaceName)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f =>
                $"{(f.IsInterface ? "interface " : "")}{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} {{ {fields} }}";
        }
    }
#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string", "double", "float", "char", "long", "short", "byte"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];

    private static readonly string[] TypeNames =
        ["Widget", "Config", "Engine", "Sensor", "Agent", "Point", "Matrix", "Record"];

    private static readonly string[] InterfaceNames =
        ["IRenderer", "ILogger", "IService", "IHandler", "IProcessor"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with 1–5 primitive fields and no default block (→ Inferred).
    /// </summary>
    private static Gen<InferredScenario> GenInferredScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 5)
               from fields in GenPrimitiveFields(fieldCount)
               select new InferredScenario(isStruct, typeName, fields);
    }

    /// <summary>
    /// Generates a scenario with 1–5 primitive fields and an explicit default block (→ Explicit).
    /// The default block contains 0–fieldCount field override entries.
    /// </summary>
    private static Gen<ExplicitScenario> GenExplicitScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 5)
               from fields in GenPrimitiveFields(fieldCount)
               from entryCount in Gen.Choose(0, fieldCount)
               select new ExplicitScenario(isStruct, typeName, fields, entryCount);
    }

    /// <summary>
    /// Generates a scenario with at least one interface field and no default block (→ NotDefaultable).
    /// </summary>
    private static Gen<NotDefaultableScenario> GenNotDefaultableScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from interfaceName in Gen.Elements(InterfaceNames)
               from primitiveCount in Gen.Choose(0, 3)
               from primitiveFields in GenPrimitiveFields(primitiveCount)
               from interfaceFieldName in Gen.Elements(
                   FieldNames.Skip(primitiveCount).Take(Math.Max(1, FieldNames.Length - primitiveCount)).ToArray())
               let allFields = primitiveFields
                   .Select(f => (f.FieldName, f.FieldType, IsInterface: false))
                   .Append((interfaceFieldName, interfaceName, IsInterface: true))
                   .ToImmutableArray()
               select new NotDefaultableScenario(isStruct, typeName, allFields, interfaceName);
    }

    private static Gen<ImmutableArray<(string FieldName, string FieldType)>> GenPrimitiveFields(int count)
    {
        if (count == 0)
            return Gen.Constant(ImmutableArray<(string, string)>.Empty);

        Gen<ImmutableArray<(string, string)>> result =
            from fieldType in Gen.Elements(PrimitiveTypes)
            select ImmutableArray.Create<(string, string)>((FieldNames[0], fieldType));

        for (var i = 1; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from fieldType in Gen.Elements(PrimitiveTypes)
                     select arr.Add((FieldNames[idx], fieldType));
        }

        return result;
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<InferredScenario> ArbitraryInferredScenario() =>
        GenInferredScenario().ToArbitrary();

    public static Arbitrary<ExplicitScenario> ArbitraryExplicitScenario() =>
        GenExplicitScenario().ToArbitrary();

    public static Arbitrary<NotDefaultableScenario> ArbitraryNotDefaultableScenario() =>
        GenNotDefaultableScenario().ToArbitrary();

    // ─── AST builders for scenarios ──────────────────────────────────────

    /// <summary>
    /// Builds an AST for an inferred-default scenario: a type with only primitive fields
    /// and no default block.
    /// </summary>
    private static CompilationUnitNode BuildInferredAst(InferredScenario scenario)
    {
        var members = scenario.Fields
            .Select(f => MakeFieldMember(MakePrimitiveField(f.FieldName, f.FieldType)))
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        return MakeCompilationUnit(typeDecl);
    }

    /// <summary>
    /// Builds an AST for an explicit-default scenario: a type with primitive fields
    /// and an explicit default block containing field overrides.
    /// </summary>
    private static CompilationUnitNode BuildExplicitAst(ExplicitScenario scenario)
    {
        var fieldMembers = scenario.Fields
            .Select(f => MakeFieldMember(MakePrimitiveField(f.FieldName, f.FieldType)))
            .ToList();

        // Build default block entries — simple field overrides with integer literal 0
        var entries = scenario.Fields
            .Take(scenario.DefaultBlockEntryCount)
            .Select(f => (DefaultFieldInitializerNode)new DefaultFieldOverrideDefaultFieldInitializerNode
            {
                FieldName = MakeToken(f.FieldName),
                Value = MakeIntLiteralExpression("0"),
            })
            .ToImmutableArray();

        var allMembers = fieldMembers
            .Cast<MemberDeclarationNode>()
            .Append(MakeDefaultBlockMember(entries))
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, allMembers))
            : WrapClass(MakeClassDecl(scenario.TypeName, allMembers));

        return MakeCompilationUnit(typeDecl);
    }

    /// <summary>
    /// Builds an AST for a not-defaultable scenario: a type with at least one interface
    /// field and no default block. The interface type is also declared so it resolves.
    /// </summary>
    private static CompilationUnitNode BuildNotDefaultableAst(NotDefaultableScenario scenario)
    {
        var members = scenario.Fields
            .Select(f =>
            {
                FieldDeclarationNode field = f.IsInterface
                    ? MakeNamedTypeField(f.FieldName, f.FieldType)
                    : MakePrimitiveField(f.FieldName, f.FieldType);
                return MakeFieldMember(field);
            })
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        // Declare the interface so the type reference resolves
        var interfaceDecl = new InterfaceDeclarationNode
        {
            Name = MakeToken(scenario.InterfaceName),
            Members = ImmutableArray<InterfaceMemberDeclarationNode>.Empty,
        };

        return MakeCompilationUnit(WrapInterface(interfaceDecl), typeDecl);
    }

    // ─── Expression helper ───────────────────────────────────────────────

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        var primary = new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        };
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
        return new ExpressionNode { Assignment = assign };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Defaultability classification correctness
    //
    // **Validates: Requirements 5.1, 5.2**
    //
    // For any class or struct where all fields have defaultable types and no
    // explicit default block is declared, the semantic model SHALL classify
    // the type as Inferred. For any class or struct with an explicit default
    // block, the semantic model SHALL classify the type as Explicit.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 4a: Types with only primitive fields and no default block
    /// are classified as <see cref="DefaultabilityStatus.Inferred"/>.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultabilityPropertyTests) }, MaxTest = 50)]
    public bool AllPrimitiveFields_NoDefaultBlock_ClassifiedAsInferred(InferredScenario scenario)
    {
        var ast = BuildInferredAst(scenario);
        var (model, _) = BuildModel(ast);

        var expectedKind = scenario.IsStruct ? SymbolKind.Struct : SymbolKind.Class;
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == expectedKind);

        if (typeSymbol is null)
            return false;

        var status = typeSymbol.DeclaredType switch
        {
            ClassType ct => ct.Defaultability,
            StructType st => st.Defaultability,
            _ => DefaultabilityStatus.Unknown,
        };

        return status == DefaultabilityStatus.Inferred;
    }

    /// <summary>
    /// Property 4b: Types with an explicit default block are classified as
    /// <see cref="DefaultabilityStatus.Explicit"/>.
    ///
    /// **Validates: Requirements 5.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultabilityPropertyTests) }, MaxTest = 50)]
    public bool ExplicitDefaultBlock_ClassifiedAsExplicit(ExplicitScenario scenario)
    {
        var ast = BuildExplicitAst(scenario);
        var (model, _) = BuildModel(ast);

        var expectedKind = scenario.IsStruct ? SymbolKind.Struct : SymbolKind.Class;
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == expectedKind);

        if (typeSymbol is null)
            return false;

        var status = typeSymbol.DeclaredType switch
        {
            ClassType ct => ct.Defaultability,
            StructType st => st.Defaultability,
            _ => DefaultabilityStatus.Unknown,
        };

        return status == DefaultabilityStatus.Explicit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Non-defaultable type detection
    //
    // **Validates: Requirements 5.3, 13.11**
    //
    // For any class or struct that has at least one field with an interface
    // type and no default block providing a type binding for that field, the
    // semantic model SHALL classify the type as NotDefaultable and SHALL emit
    // a MIST_DEFAULT_REQUIRES_BINDING diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 5: Types with interface fields and no default block are classified
    /// as <see cref="DefaultabilityStatus.NotDefaultable"/> and emit
    /// <c>MIST_DEFAULT_REQUIRES_BINDING</c>.
    ///
    /// **Validates: Requirements 5.3, 13.11**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultabilityPropertyTests) }, MaxTest = 50)]
    public bool InterfaceFieldWithoutBinding_ClassifiedAsNotDefaultable_AndDiagnosticEmitted(
        NotDefaultableScenario scenario)
    {
        var ast = BuildNotDefaultableAst(scenario);
        var (model, diagnostics) = BuildModel(ast);

        var expectedKind = scenario.IsStruct ? SymbolKind.Struct : SymbolKind.Class;
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == expectedKind);

        if (typeSymbol is null)
            return false;

        // Check defaultability status
        var status = typeSymbol.DeclaredType switch
        {
            ClassType ct => ct.Defaultability,
            StructType st => st.Defaultability,
            _ => DefaultabilityStatus.Unknown,
        };

        if (status != DefaultabilityStatus.NotDefaultable)
            return false;

        // Check that MIST_DEFAULT_REQUIRES_BINDING diagnostic was emitted
        var hasRequiresBindingDiagnostic = diagnostics
            .Any(d => d.Id == DiagnosticId.DefaultRequiresTypeBinding);

        return hasRequiresBindingDiagnostic;
    }
}
