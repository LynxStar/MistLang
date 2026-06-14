// Feature: optional-defaultability, Property 12: Inheritance defaultability rules
// Property 12: For any derived type with a `default {}` block whose base type is non-defaultable,
// the compiler SHALL emit MIST_DEFAULT_BASE_NOT_DEFAULTABLE. For any derived type with constructors
// (no default block) and a non-defaultable base, the compiler SHALL allow it without error.
// **Validates: Requirements 10.3, 10.4**

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
/// Property-based tests for inheritance defaultability rules (Property 12).
///
/// For any derived type with a <c>default {}</c> block whose base type is non-defaultable,
/// the compiler SHALL emit <c>MIST_DEFAULT_BASE_NOT_DEFAULTABLE</c>.
/// For any derived type with constructors (no default block) and a non-defaultable base,
/// the compiler SHALL allow it without error.
/// </summary>
public class InheritanceDefaultabilityPropertyTests
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
            },
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

    private static MemberDeclarationNode MakeDefaultBlockMember(
        ImmutableArray<DefaultFieldInitializerNode> entries)
    {
        return new DefaultBlockMemberMemberDeclarationNode
        {
            DefaultBlock = new DefaultBlockDeclarationNode { Entries = entries },
        };
    }

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
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static BaseListNode MakeBaseList(string baseClassName)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(baseClassName) };
        return new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
        };
    }

    private static ClassDeclarationNode MakeClassDecl(
        string name, ImmutableArray<MemberDeclarationNode> members, string? baseClassName = null)
    {
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };

        if (baseClassName is not null)
        {
            var baseList = MakeBaseList(baseClassName);
            classDecl.BaseClause = baseList;
            classDecl.BaseList = baseList;
        }

        return classDecl;
    }

    private static TypeDeclarationNode WrapClass(ClassDeclarationNode classDecl)
    {
        return new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl };
    }

    private static CompilationUnitNode MakeCompilationUnit(
        params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario for testing derived type with constructors and a non-defaultable base.
    /// Expected: NotDefaultable, no diagnostic.
    /// </summary>
    public record DerivedWithConstructorScenario(
        string BaseTypeName,
        string DerivedTypeName,
        ImmutableArray<(string FieldName, string FieldType)> DerivedFields)
    {
        public override string ToString()
        {
            var fields = string.Join(", ", DerivedFields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"class {DerivedTypeName} : {BaseTypeName} + ctor {{ {fields} }}";
        }
    }

    /// <summary>
    /// Scenario for testing derived type with a default block and a non-defaultable base.
    /// Expected: MIST_DEFAULT_BASE_NOT_DEFAULTABLE diagnostic.
    /// </summary>
    public record DerivedWithDefaultBlockScenario(
        string BaseTypeName,
        string DerivedTypeName,
        ImmutableArray<(string FieldName, string FieldType)> DerivedFields,
        bool HasConstructor)
    {
        public override string ToString()
        {
            var ctor = HasConstructor ? " + ctor" : "";
            var fields = string.Join(", ", DerivedFields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"class {DerivedTypeName} : {BaseTypeName}{ctor} + default {{ }} {{ {fields} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "bool", "string"];

    private static readonly string[] FieldNames =
        ["alpha", "beta", "gamma", "delta"];

    private static readonly string[] BaseTypeNames =
        ["Engine", "Connection", "Handler", "Processor", "Service"];

    private static readonly string[] DerivedTypeNames =
        ["SubEngine", "SubConnection", "SubHandler", "SubProcessor", "SubService"];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<ImmutableArray<(string FieldName, string FieldType)>> GenPrimitiveFields(
        int count)
    {
        if (count == 0)
            return Gen.Constant(ImmutableArray<(string, string)>.Empty);

        Gen<ImmutableArray<(string, string)>> result =
            Gen.Constant(ImmutableArray<(string, string)>.Empty);

        for (var i = 0; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from fieldType in Gen.Elements(PrimitiveTypes)
                     select arr.Add((FieldNames[idx], fieldType));
        }

        return result;
    }

    /// <summary>
    /// Generates scenarios for derived types with constructors and a non-defaultable base.
    /// </summary>
    private static Gen<DerivedWithConstructorScenario> GenDerivedWithConstructorScenario()
    {
        return from baseName in Gen.Elements(BaseTypeNames)
               from derivedName in Gen.Elements(DerivedTypeNames)
               from fieldCount in Gen.Choose(0, 3)
               from fields in GenPrimitiveFields(fieldCount)
               select new DerivedWithConstructorScenario(baseName, derivedName, fields);
    }

    /// <summary>
    /// Generates scenarios for derived types with a default block and a non-defaultable base.
    /// Includes both: with constructors + default block, and without constructors + non-defaultable base.
    /// </summary>
    private static Gen<DerivedWithDefaultBlockScenario> GenDerivedWithDefaultBlockScenario()
    {
        return from baseName in Gen.Elements(BaseTypeNames)
               from derivedName in Gen.Elements(DerivedTypeNames)
               from fieldCount in Gen.Choose(0, 3)
               from fields in GenPrimitiveFields(fieldCount)
               from hasConstructor in Gen.Elements(true, false)
               select new DerivedWithDefaultBlockScenario(baseName, derivedName, fields, hasConstructor);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<DerivedWithConstructorScenario> ArbitraryDerivedWithConstructor() =>
        GenDerivedWithConstructorScenario().ToArbitrary();

    public static Arbitrary<DerivedWithDefaultBlockScenario> ArbitraryDerivedWithDefaultBlock() =>
        GenDerivedWithDefaultBlockScenario().ToArbitrary();

    // ─── AST builders for scenarios ──────────────────────────────────────

    /// <summary>
    /// Builds a non-defaultable base class: has a constructor and no default block.
    /// </summary>
    private static ClassDeclarationNode BuildNonDefaultableBase(string baseTypeName)
    {
        var members = ImmutableArray.Create(
            MakeFieldMember(MakePrimitiveField("baseField", "int")),
            MakeConstructorMember(baseTypeName));

        return MakeClassDecl(baseTypeName, members);
    }

    /// <summary>
    /// Builds the AST for a derived type with a constructor inheriting from a non-defaultable base.
    /// No default block on the derived type.
    /// </summary>
    private static CompilationUnitNode BuildDerivedWithConstructorAst(
        DerivedWithConstructorScenario scenario)
    {
        var baseDecl = BuildNonDefaultableBase(scenario.BaseTypeName);

        var derivedMembers = scenario.DerivedFields
            .Select(f => MakeFieldMember(MakePrimitiveField(f.FieldName, f.FieldType)))
            .Append(MakeConstructorMember(scenario.DerivedTypeName))
            .ToImmutableArray();

        var derivedDecl = MakeClassDecl(
            scenario.DerivedTypeName, derivedMembers, scenario.BaseTypeName);

        return MakeCompilationUnit(WrapClass(baseDecl), WrapClass(derivedDecl));
    }

    /// <summary>
    /// Builds the AST for a derived type with a default block inheriting from a non-defaultable base.
    /// The derived type may or may not have a constructor.
    /// </summary>
    private static CompilationUnitNode BuildDerivedWithDefaultBlockAst(
        DerivedWithDefaultBlockScenario scenario)
    {
        var baseDecl = BuildNonDefaultableBase(scenario.BaseTypeName);

        // Build default block entries for the derived fields
        var defaultEntries = scenario.DerivedFields
            .Select(f => (DefaultFieldInitializerNode)new DefaultFieldOverrideDefaultFieldInitializerNode
            {
                FieldName = MakeToken(f.FieldName),
                Value = MakeIntLiteralExpression("0"),
            })
            .ToImmutableArray();

        var derivedMembers = scenario.DerivedFields
            .Select(f => MakeFieldMember(MakePrimitiveField(f.FieldName, f.FieldType)))
            .Cast<MemberDeclarationNode>()
            .Append(MakeDefaultBlockMember(defaultEntries))
            .ToList();

        if (scenario.HasConstructor)
        {
            derivedMembers.Add(MakeConstructorMember(scenario.DerivedTypeName));
        }

        var derivedDecl = MakeClassDecl(
            scenario.DerivedTypeName, derivedMembers.ToImmutableArray(), scenario.BaseTypeName);

        return MakeCompilationUnit(WrapClass(baseDecl), WrapClass(derivedDecl));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12a: Derived with constructors + non-defaultable base → no error
    //
    // **Validates: Requirement 10.3**
    //
    // For any derived type with constructors (no default block) and a non-defaultable
    // base, the compiler SHALL allow it without error. The derived type is classified
    // as NotDefaultable silently.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 12a: Derived types with constructors and a non-defaultable base produce no
    /// <c>MIST_DEFAULT_BASE_NOT_DEFAULTABLE</c> diagnostic and are classified as
    /// <see cref="DefaultabilityStatus.NotDefaultable"/>.
    ///
    /// **Validates: Requirements 10.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InheritanceDefaultabilityPropertyTests) }, MaxTest = 100)]
    public bool DerivedWithConstructor_NonDefaultableBase_NoDiagnostic(
        DerivedWithConstructorScenario scenario)
    {
        var ast = BuildDerivedWithConstructorAst(scenario);
        var (model, diagnostics) = BuildModel(ast);

        var derivedSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.DerivedTypeName && s.Kind == SymbolKind.Class);

        if (derivedSymbol is null)
            return false;

        // The derived type should be NotDefaultable
        var status = (derivedSymbol.DeclaredType as ClassType)?.Defaultability;
        if (status != DefaultabilityStatus.NotDefaultable)
            return false;

        // No MIST_DEFAULT_BASE_NOT_DEFAULTABLE diagnostic should be emitted
        var hasBaseNotDefaultableDiagnostic = diagnostics
            .Any(d => d.Id == DiagnosticId.DefaultBaseNotDefaultable);

        return !hasBaseNotDefaultableDiagnostic;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12b: Derived with default block + non-defaultable base → error
    //
    // **Validates: Requirement 10.4**
    //
    // For any derived type with a `default {}` block whose base type is non-defaultable,
    // the compiler SHALL emit MIST_DEFAULT_BASE_NOT_DEFAULTABLE. This includes both:
    // - Derived with constructors + default block + non-defaultable base
    // - Derived with no constructors + non-defaultable base (since implicit ctor needs base default)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 12b: Derived types with a default block (with or without constructors) and a
    /// non-defaultable base emit <c>MIST_DEFAULT_BASE_NOT_DEFAULTABLE</c> and are classified
    /// as <see cref="DefaultabilityStatus.NotDefaultable"/>.
    ///
    /// **Validates: Requirements 10.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InheritanceDefaultabilityPropertyTests) }, MaxTest = 100)]
    public bool DerivedWithDefaultBlock_NonDefaultableBase_EmitsDiagnostic(
        DerivedWithDefaultBlockScenario scenario)
    {
        var ast = BuildDerivedWithDefaultBlockAst(scenario);
        var (model, diagnostics) = BuildModel(ast);

        var derivedSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.DerivedTypeName && s.Kind == SymbolKind.Class);

        if (derivedSymbol is null)
            return false;

        // The derived type should be NotDefaultable
        var status = (derivedSymbol.DeclaredType as ClassType)?.Defaultability;
        if (status != DefaultabilityStatus.NotDefaultable)
            return false;

        // MIST_DEFAULT_BASE_NOT_DEFAULTABLE diagnostic SHOULD be emitted
        var hasBaseNotDefaultableDiagnostic = diagnostics
            .Any(d => d.Id == DiagnosticId.DefaultBaseNotDefaultable);

        return hasBaseNotDefaultableDiagnostic;
    }
}
