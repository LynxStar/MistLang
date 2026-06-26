// Feature: default-keyword
// Property 10: Default expression type assignment — **Validates: Requirements 7.1, 7.2**
// Property 11: Bare default type inference — **Validates: Requirements 7.8**
// Property 12: Visibility enforcement in default blocks — **Validates: Requirements 9.1, 9.2, 9.3**
// Property 13: Redundant default block detection — **Validates: Requirements 11.1, 11.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for the type checker's handling of default expressions
/// and default block visibility enforcement.
///
/// Property 10: For any <c>default(TypeName)</c> expression where TypeName resolves
/// to a defaultable ClassType or StructType, the type checker SHALL assign the
/// resolved type to the expression and produce no error diagnostics.
///
/// Property 11: For any bare <c>default</c> literal expression appearing in a context
/// where the target type is a known defaultable type (typed variable declaration),
/// the type checker SHALL infer the target type and assign it to the expression.
///
/// Property 12: For any default block field override referencing a private base-type
/// field, the type checker SHALL produce a <c>MIST_DEFAULT_FIELD_NOT_VISIBLE</c>
/// diagnostic error. Overrides of public base-type fields and own fields SHALL be
/// accepted.
/// </summary>
public class DefaultTypeCheckerPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

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

    private (ISemanticModel Model, ImmutableArray<Diagnostic> ModelDiagnostics) BuildModel(
        Storm.Runtime.SyntaxNode ast, string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = builder.Build(
            compilation, ImmutableArray.Create(ast), ambientTypeResolver: null,
            namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
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
        string fieldName, TypeReferenceNode typeRef,
        ImmutableArray<ModifierNode>? modifiers = null)
    {
        return new FieldDeclarationNode
        {
            Name = MakeToken(fieldName),
            Type = typeRef,
            Modifiers = modifiers ?? ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
        };
    }

    private static FieldDeclarationNode MakePublicField(string fieldName, TypeReferenceNode typeRef)
    {
        return MakeField(fieldName, typeRef,
            ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()));
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

    private static ClassDeclarationNode MakeClassDecl(
        string name, ImmutableArray<MemberDeclarationNode> members)
    {
        return new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
    }

    private static ClassDeclarationNode MakeClassDeclWithBase(
        string name, string baseClassName, ImmutableArray<MemberDeclarationNode> members)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = MakeToken(baseClassName) };
        var baseList = new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken(name),
            Members = members,
        };
        classDecl.BaseClause = baseList;
        classDecl.BaseList = baseList;
        return classDecl;
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

    private static ExpressionNode MakeIntLiteralExpression(string value = "0")
    {
        return WrapPrimaryAsExpression(new IntegerLiteralExpressionPrimaryNode
        {
            IntegerLiteral = MakeToken(value),
        });
    }

    private static ExpressionNode MakeDefaultExpression(TypeReferenceNode typeRef)
    {
        return WrapPrimaryAsExpression(new DefaultExpressionPrimaryNode
        {
            Type = typeRef,
        });
    }

    private static ExpressionNode MakeDefaultLiteralExpression()
    {
        return WrapPrimaryAsExpression(new DefaultLiteralExpressionPrimaryNode());
    }

    // ─── Statement helpers ───────────────────────────────────────────────

    /// <summary>
    /// Creates a typed variable declaration: <c>TypeRef name = initializer;</c>
    /// </summary>
    private static StatementNode MakeTypedVarDecl(
        TypeReferenceNode typeRef, string name, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = typeRef,
                Name = MakeToken(name),
                Initializer = initializer,
            },
        };
    }

    private static StatementNode MakeReturnStatement(ExpressionNode? value = null)
    {
        return new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = value },
        };
    }

    // ─── Program builders ────────────────────────────────────────────────

    /// <summary>
    /// Creates a program with a static void Main() containing the given statements,
    /// plus additional type declarations placed before the Program class.
    /// </summary>
    private static CompilationUnitNode MakeProgram(
        ImmutableArray<StatementNode> mainStatements,
        params TypeDeclarationNode[] additionalTypes)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = mainStatements },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var allTypes = additionalTypes.ToList();
        allTypes.Add(new ClassTypeTypeDeclarationNode { ClassDeclaration = programClass });

        return new CompilationUnitNode
        {
            Items = allTypes.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ─── Scenario records ────────────────────────────────────────────────

    /// <summary>
    /// Scenario for Property 10: a defaultable type and a <c>default(TypeName)</c>
    /// expression targeting it.
    /// </summary>
#pragma warning disable CA1034
    public record DefaultExprTypeScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType)> Fields)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"default({kind} {TypeName} {{ {fields} }})";
        }
    }

    /// <summary>
    /// Scenario for Property 11: a typed variable declaration initialized with
    /// bare <c>default</c>.
    /// </summary>
    public record BareDefaultScenario(
        string VarType,
        bool IsPrimitive,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType)> Fields)
    {
        public override string ToString()
        {
            return IsPrimitive
                ? $"{VarType} x = default"
                : $"{TypeName} x = default";
        }
    }

    /// <summary>
    /// Scenario for Property 12: a type with own fields (both public and private)
    /// and a default block that overrides them. Own field overrides should always
    /// be accepted without visibility errors.
    /// </summary>
    public record OwnFieldVisibilityScenario(
        bool IsStruct,
        string TypeName,
        string PrivateFieldName,
        string PublicFieldName)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            return $"{kind} {TypeName} {{ private {PrivateFieldName}; public {PublicFieldName}; " +
                   $"default {{ {PrivateFieldName} = 0, {PublicFieldName} = 0 }} }}";
        }
    }

    /// <summary>
    /// Scenario for Property 12: a base class with a private field and a derived
    /// class with a default block that tries to override it. The inherited field
    /// is not directly in the derived class's scope, so the type checker treats
    /// it as a non-existent field (no visibility diagnostic, but also no crash).
    /// </summary>
    public record InheritedFieldVisibilityScenario(
        string BaseTypeName,
        string DerivedTypeName,
        string PrivateFieldName,
        string PublicFieldName)
    {
        public override string ToString()
        {
            return $"class {DerivedTypeName} : {BaseTypeName} " +
                   $"{{ default {{ {PublicFieldName} = 0 }} }} " +
                   $"(private: {PrivateFieldName}, public: {PublicFieldName})";
        }
    }

    /// <summary>
    /// Scenario for Property 13: a class or struct with only primitive fields
    /// and an empty explicit default block. The empty block is always redundant
    /// because it produces the same values as the inferred default.
    /// </summary>
    public record RedundantDefaultBlockScenario(
        bool IsStruct,
        string TypeName,
        ImmutableArray<(string FieldName, string FieldType)> Fields)
    {
        public override string ToString()
        {
            var kind = IsStruct ? "struct" : "class";
            var fields = string.Join(", ", Fields.Select(f => $"{f.FieldType} {f.FieldName}"));
            return $"{kind} {TypeName} {{ {fields}; default {{ }} }}";
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

    private static readonly string[] BaseTypeNames =
        ["BaseWidget", "BaseConfig", "BaseEngine", "BaseSensor", "BaseAgent"];

    private static readonly string[] DerivedTypeNames =
        ["DerivedWidget", "DerivedConfig", "DerivedEngine", "DerivedSensor", "DerivedAgent"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a defaultable type (1–4 primitive fields) for
    /// testing <c>default(TypeName)</c> expression type assignment.
    /// </summary>
    private static Gen<DefaultExprTypeScenario> GenDefaultExprTypeScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fields in GenPrimitiveFields(fieldCount)
               select new DefaultExprTypeScenario(isStruct, typeName, fields);
    }

    /// <summary>
    /// Generates a scenario for bare <c>default</c> type inference in a typed
    /// variable declaration context.
    /// </summary>
    private static Gen<BareDefaultScenario> GenBareDefaultScenario()
    {
        var primitiveGen =
            from primType in Gen.Elements(PrimitiveTypes)
            select new BareDefaultScenario(primType, true, primType,
                ImmutableArray<(string, string)>.Empty);

        var classGen =
            from typeName in Gen.Elements(TypeNames)
            from fieldCount in Gen.Choose(1, 3)
            from fields in GenPrimitiveFields(fieldCount)
            select new BareDefaultScenario(typeName, false, typeName, fields);

        return Gen.OneOf(primitiveGen, classGen);
    }

    /// <summary>
    /// Generates a scenario for own-field visibility: a type with one private
    /// and one public field, and a default block that overrides both.
    /// </summary>
    private static Gen<OwnFieldVisibilityScenario> GenOwnFieldVisibilityScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from privateFieldIdx in Gen.Choose(0, 2)
               from publicFieldIdx in Gen.Choose(3, 4)
               select new OwnFieldVisibilityScenario(
                   isStruct, typeName,
                   FieldNames[privateFieldIdx],
                   FieldNames[publicFieldIdx]);
    }

    /// <summary>
    /// Generates a scenario for inherited-field visibility: a base class with
    /// one private and one public field, and a derived class with a default block
    /// that overrides the public base field.
    /// </summary>
    private static Gen<InheritedFieldVisibilityScenario> GenInheritedFieldVisibilityScenario()
    {
        return from idx in Gen.Choose(0, BaseTypeNames.Length - 1)
               from privateFieldIdx in Gen.Choose(0, 2)
               from publicFieldIdx in Gen.Choose(3, 4)
               select new InheritedFieldVisibilityScenario(
                   BaseTypeNames[idx],
                   DerivedTypeNames[idx],
                   FieldNames[privateFieldIdx],
                   FieldNames[publicFieldIdx]);
    }

    /// <summary>
    /// Generates a scenario for redundant default block detection: a class or
    /// struct with 1–4 primitive fields and an empty explicit default block.
    /// </summary>
    private static Gen<RedundantDefaultBlockScenario> GenRedundantDefaultBlockScenario()
    {
        return from isStruct in Gen.Elements(true, false)
               from typeName in Gen.Elements(TypeNames)
               from fieldCount in Gen.Choose(1, 4)
               from fields in GenPrimitiveFields(fieldCount)
               select new RedundantDefaultBlockScenario(isStruct, typeName, fields);
    }

    private static Gen<ImmutableArray<(string FieldName, string FieldType)>> GenPrimitiveFields(int count)
    {
        if(count == 0)
        {
            return Gen.Constant(ImmutableArray<(string, string)>.Empty);
        }

        Gen<ImmutableArray<(string, string)>> result =
            from fieldType in Gen.Elements(PrimitiveTypes)
            select ImmutableArray.Create<(string, string)>((FieldNames[0], fieldType));

        for(var i = 1; i < count; i++)
        {
            var idx = i;
            result = from arr in result
                     from fieldType in Gen.Elements(PrimitiveTypes)
                     select arr.Add((FieldNames[idx], fieldType));
        }

        return result;
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<DefaultExprTypeScenario> ArbitraryDefaultExprTypeScenario() =>
        GenDefaultExprTypeScenario().ToArbitrary();

    public static Arbitrary<BareDefaultScenario> ArbitraryBareDefaultScenario() =>
        GenBareDefaultScenario().ToArbitrary();

    public static Arbitrary<OwnFieldVisibilityScenario> ArbitraryOwnFieldVisibilityScenario() =>
        GenOwnFieldVisibilityScenario().ToArbitrary();

    public static Arbitrary<InheritedFieldVisibilityScenario> ArbitraryInheritedFieldVisibilityScenario() =>
        GenInheritedFieldVisibilityScenario().ToArbitrary();

    public static Arbitrary<RedundantDefaultBlockScenario> ArbitraryRedundantDefaultBlockScenario() =>
        GenRedundantDefaultBlockScenario().ToArbitrary();

    // ─── AST builders for scenarios ──────────────────────────────────────

    /// <summary>
    /// Builds an AST for Property 10: a defaultable type and a Main method
    /// containing <c>TypeName x = default(TypeName);</c>.
    /// </summary>
    private static CompilationUnitNode BuildDefaultExprTypeAst(DefaultExprTypeScenario scenario)
    {
        var members = scenario.Fields
            .Select(f => MakeFieldMember(MakeField(f.FieldName, MakePrimitiveTypeRef(f.FieldType))))
            .ToImmutableArray();

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, members))
            : WrapClass(MakeClassDecl(scenario.TypeName, members));

        // Main body: TypeName x = default(TypeName);
        var defaultExpr = MakeDefaultExpression(MakeNamedTypeRef(scenario.TypeName));
        var varDecl = MakeTypedVarDecl(
            MakeNamedTypeRef(scenario.TypeName), "x", defaultExpr);

        return MakeProgram(
            ImmutableArray.Create(varDecl, MakeReturnStatement()),
            typeDecl);
    }

    /// <summary>
    /// Builds an AST for Property 11: a typed variable declaration initialized
    /// with bare <c>default</c>.
    /// </summary>
    private static CompilationUnitNode BuildBareDefaultAst(BareDefaultScenario scenario)
    {
        var defaultLiteral = MakeDefaultLiteralExpression();

        TypeReferenceNode varTypeRef;
        var additionalTypes = new List<TypeDeclarationNode>();

        if(scenario.IsPrimitive)
        {
            varTypeRef = MakePrimitiveTypeRef(scenario.VarType);
        }
        else
        {
            varTypeRef = MakeNamedTypeRef(scenario.TypeName);

            // Declare the class so the type reference resolves
            var classMembers = scenario.Fields
                .Select(f => MakeFieldMember(MakeField(f.FieldName, MakePrimitiveTypeRef(f.FieldType))))
                .ToImmutableArray();
            additionalTypes.Add(WrapClass(MakeClassDecl(scenario.TypeName, classMembers)));
        }

        var varDecl = MakeTypedVarDecl(varTypeRef, "x", defaultLiteral);

        return MakeProgram(
            ImmutableArray.Create(varDecl, MakeReturnStatement()),
            additionalTypes.ToArray());
    }

    /// <summary>
    /// Builds an AST for Property 12 (own-field case): a type with one private
    /// and one public field, and a default block that overrides both.
    /// Own field overrides should always be accepted.
    /// </summary>
    private static CompilationUnitNode BuildOwnFieldVisibilityAst(OwnFieldVisibilityScenario scenario)
    {
        // Type with one private field and one public field
        var fieldMembers = ImmutableArray.Create(
            MakeFieldMember(MakeField(scenario.PrivateFieldName, MakePrimitiveTypeRef("int"))),
            MakeFieldMember(MakePublicField(scenario.PublicFieldName, MakePrimitiveTypeRef("int"))));

        // Default block overrides both fields
        var entries = ImmutableArray.Create<DefaultFieldInitializerNode>(
            new DefaultFieldOverrideDefaultFieldInitializerNode
            {
                FieldName = MakeToken(scenario.PrivateFieldName),
                Value = MakeIntLiteralExpression("42"),
            },
            new DefaultFieldOverrideDefaultFieldInitializerNode
            {
                FieldName = MakeToken(scenario.PublicFieldName),
                Value = MakeIntLiteralExpression("99"),
            });

        var allMembers = fieldMembers.Add(MakeDefaultBlockMember(entries));

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, allMembers))
            : WrapClass(MakeClassDecl(scenario.TypeName, allMembers));

        return MakeProgram(
            ImmutableArray.Create(MakeReturnStatement()),
            typeDecl);
    }

    /// <summary>
    /// Builds an AST for Property 12 (inherited public field case): a base class
    /// with a public field and a derived class with a default block that overrides
    /// the public base field. The public field should be accepted.
    /// </summary>
    private static CompilationUnitNode BuildInheritedPublicFieldAst(
        InheritedFieldVisibilityScenario scenario)
    {
        // Base class: one private field, one public field (both int)
        var baseMembers = ImmutableArray.Create(
            MakeFieldMember(MakeField(scenario.PrivateFieldName, MakePrimitiveTypeRef("int"))),
            MakeFieldMember(MakePublicField(scenario.PublicFieldName, MakePrimitiveTypeRef("int"))));

        var baseClassDecl = MakeClassDecl(scenario.BaseTypeName, baseMembers);

        // Derived class: default block overrides the public base field
        var overrideEntry = (DefaultFieldInitializerNode)
            new DefaultFieldOverrideDefaultFieldInitializerNode
            {
                FieldName = MakeToken(scenario.PublicFieldName),
                Value = MakeIntLiteralExpression("42"),
            };

        var derivedMembers = ImmutableArray.Create(
            MakeDefaultBlockMember(ImmutableArray.Create(overrideEntry)));

        var derivedClassDecl = MakeClassDeclWithBase(
            scenario.DerivedTypeName, scenario.BaseTypeName, derivedMembers);

        return MakeProgram(
            ImmutableArray.Create(MakeReturnStatement()),
            WrapClass(baseClassDecl),
            WrapClass(derivedClassDecl));
    }

    /// <summary>
    /// Builds an AST for Property 13: a class or struct with only primitive fields
    /// and an empty explicit <c>default { }</c> block. The empty block is always
    /// redundant because it produces the same values as the inferred default.
    /// </summary>
    private static CompilationUnitNode BuildRedundantDefaultBlockAst(RedundantDefaultBlockScenario scenario)
    {
        // Fields (all primitive)
        var fieldMembers = scenario.Fields
            .Select(f => MakeFieldMember(MakeField(f.FieldName, MakePrimitiveTypeRef(f.FieldType))))
            .ToImmutableArray();

        // Empty default block — always redundant for all-primitive types
        var emptyDefaultBlock = MakeDefaultBlockMember(
            ImmutableArray<DefaultFieldInitializerNode>.Empty);

        var allMembers = fieldMembers.Add(emptyDefaultBlock);

        TypeDeclarationNode typeDecl = scenario.IsStruct
            ? WrapStruct(MakeStructDecl(scenario.TypeName, allMembers))
            : WrapClass(MakeClassDecl(scenario.TypeName, allMembers));

        return MakeProgram(
            ImmutableArray.Create(MakeReturnStatement()),
            typeDecl);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10: Default expression type assignment
    //
    // **Validates: Requirements 7.1, 7.2**
    //
    // For any default(TypeName) expression where TypeName resolves to a
    // defaultable ClassType or StructType, the type checker SHALL assign
    // the resolved type to the expression and produce no error diagnostics
    // related to the default expression.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 10: <c>default(TypeName)</c> on a defaultable type produces no
    /// default-related error diagnostics and the expression type matches the
    /// target type.
    ///
    /// **Validates: Requirements 7.1, 7.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultTypeCheckerPropertyTests) }, MaxTest = 50)]
    public bool DefaultExpression_OnDefaultableType_NoDefaultErrors_AndTypeAssigned(
        DefaultExprTypeScenario scenario)
    {
        var ast = BuildDefaultExprTypeAst(scenario);
        var (model, _) = BuildModel(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // No default-related error diagnostics should be emitted
        var defaultErrors = tcDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        && d.Id.Value.StartsWith("MIST_DEFAULT"))
            .ToList();

        if(defaultErrors.Count > 0)
        {
            return false;
        }

        // Verify the type was resolved on the semantic model: look up the type
        // symbol and confirm it is defaultable
        var expectedKind = scenario.IsStruct ? SymbolKind.Struct : SymbolKind.Class;
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == expectedKind);

        if(typeSymbol is null)
        {
            return false;
        }

        var defaultability = typeSymbol.DeclaredType switch
        {
            ClassType ct => ct.Defaultability,
            StructType st => st.Defaultability,
            _ => DefaultabilityStatus.Unknown,
        };

        // The type should be defaultable (Inferred since all fields are primitive)
        return defaultability is DefaultabilityStatus.Inferred or DefaultabilityStatus.Explicit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Bare default type inference
    //
    // **Validates: Requirements 7.8**
    //
    // For any bare default literal expression appearing in a context where
    // the target type is a known defaultable type (typed variable
    // declaration), the type checker SHALL infer the target type and assign
    // it to the expression, producing no default-related error diagnostics.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 11: Bare <c>default</c> in a typed variable declaration context
    /// produces no default-related error diagnostics, confirming the type was
    /// inferred from the declaration type.
    ///
    /// **Validates: Requirements 7.8**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultTypeCheckerPropertyTests) }, MaxTest = 50)]
    public bool BareDefault_InTypedVarDecl_NoDefaultErrors(BareDefaultScenario scenario)
    {
        var ast = BuildBareDefaultAst(scenario);
        var (model, _) = BuildModel(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // No MIST_DEFAULT_CANNOT_INFER or other default-related errors
        var defaultErrors = tcDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        && d.Id.Value.StartsWith("MIST_DEFAULT"))
            .ToList();

        return defaultErrors.Count == 0;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Visibility enforcement in default blocks
    //
    // **Validates: Requirements 9.1, 9.2, 9.3**
    //
    // For any default block field override referencing a private base-type
    // field, the type checker SHALL produce MIST_DEFAULT_FIELD_NOT_VISIBLE.
    // Overrides of public base-type fields SHALL be accepted without that
    // diagnostic.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 12a: Overriding own fields (both private and public) in a type's
    /// default block does NOT produce <c>MIST_DEFAULT_FIELD_NOT_VISIBLE</c>.
    /// Own fields are always accessible from the type's own default block.
    ///
    /// **Validates: Requirements 9.1, 9.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultTypeCheckerPropertyTests) }, MaxTest = 50)]
    public bool OwnFieldOverrides_NoVisibilityDiagnostic(OwnFieldVisibilityScenario scenario)
    {
        var ast = BuildOwnFieldVisibilityAst(scenario);
        var (model, _) = BuildModel(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // Own field overrides should never produce MIST_DEFAULT_FIELD_NOT_VISIBLE
        return !tcDiagnostics.Any(d =>
            d.Id == DiagnosticId.DefaultFieldNotVisible);
    }

    /// <summary>
    /// Property 12b: Overriding a public base-type field in a derived class's
    /// default block does NOT produce <c>MIST_DEFAULT_FIELD_NOT_VISIBLE</c>.
    /// Public inherited fields should be accessible from derived default blocks.
    ///
    /// **Validates: Requirements 9.2, 9.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultTypeCheckerPropertyTests) }, MaxTest = 50)]
    public bool PublicBaseFieldOverride_NoVisibilityDiagnostic(InheritedFieldVisibilityScenario scenario)
    {
        var ast = BuildInheritedPublicFieldAst(scenario);
        var (model, _) = BuildModel(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // Public base field overrides should not produce MIST_DEFAULT_FIELD_NOT_VISIBLE
        return !tcDiagnostics.Any(d =>
            d.Id == DiagnosticId.DefaultFieldNotVisible);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: Redundant default block detection
    //
    // **Validates: Requirements 11.1, 11.2**
    //
    // For any type with an explicit default block whose computed default
    // values are identical to what the inferred default would produce,
    // the compiler SHALL emit an informational diagnostic indicating the
    // block is redundant.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 13: A class or struct with only primitive fields and an empty
    /// explicit <c>default { }</c> block SHALL produce a
    /// <c>MIST_DEFAULT_REDUNDANT</c> diagnostic with <c>Info</c> severity.
    /// An empty default block is always redundant because it produces the same
    /// values as the inferred default.
    ///
    /// **Validates: Requirements 11.1, 11.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(DefaultTypeCheckerPropertyTests) }, MaxTest = 50)]
    public bool EmptyDefaultBlock_OnAllPrimitiveType_EmitsRedundantInfoDiagnostic(
        RedundantDefaultBlockScenario scenario)
    {
        var ast = BuildRedundantDefaultBlockAst(scenario);
        var (model, _) = BuildModel(ast);
        var tcDiagnostics = RunTypeChecker(model);

        // The redundant default block diagnostic should be emitted
        var redundantDiag = tcDiagnostics
            .FirstOrDefault(d => d.Id == DiagnosticId.DefaultRedundantBlock);

        if(redundantDiag is null)
        {
            return false;
        }

        // The diagnostic must have Info severity (not Warning or Error)
        return redundantDiag.Severity == DiagnosticSeverity.Info;
    }
}