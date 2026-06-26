// Feature: cast-operators, Property 6: Explicit cast expression compile-time safety
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 9.1, 9.2, 9.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for explicit cast expression compile-time safety.
///
/// Property 6: For any cast expression (TargetType)expr where the compile-time type
/// of expr declares (directly, not via inheritance) an explicit or implicit cast operator
/// to TargetType, or where TargetType declares a cast operator from the compile-time type
/// of expr, the type checker SHALL accept the cast and set the expression type to TargetType.
/// For any cast expression where the cast operator is defined only on a derived type of the
/// compile-time type (not on the compile-time type itself), the type checker SHALL reject
/// the cast with a diagnostic naming the compile-time type. For any cast expression where
/// the compile-time type is already assignable to the target type, the cast SHALL be accepted
/// without requiring a cast operator.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 9.1, 9.2, 9.3**
/// </summary>
public class ExplicitCastSafetyPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    private const string NoCastOperatorId = "MIST_TC_0042";
    private const string AmbiguousCastId = "MIST_TC_0043";

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Describes where the cast operator is defined and what kind it is.
    /// </summary>
#pragma warning disable CA1034
    public enum OperatorPlacement
    {
        /// <summary>Cast operator defined on the source type.</summary>
        OnSourceType,
        /// <summary>Cast operator defined on the target type.</summary>
        OnTargetType,
    }

    public enum CastOperatorKind
    {
        /// <summary>explicit operator TargetType(SourceType value)</summary>
        Explicit,
        /// <summary>implicit operator TargetType(SourceType value)</summary>
        Implicit,
    }

    /// <summary>
    /// A scenario where a cast operator exists and the explicit cast should be accepted.
    /// Covers: source declares explicit op, target declares explicit op, source declares implicit op.
    /// </summary>
    public record ValidExplicitCastScenario(
        string SourceTypeName,
        string TargetTypeName,
        OperatorPlacement Placement,
        CastOperatorKind Kind)
    {
        public override string ToString()
        {
            return $"({TargetTypeName}){SourceTypeName} — {Kind} operator on {Placement}";
        }
    }

    /// <summary>
    /// A scenario where no cast operator exists and MIST_TC_0042 should be emitted.
    /// </summary>
    public record NoCastOperatorScenario(
        string SourceTypeName,
        string TargetTypeName)
    {
        public override string ToString()
        {
            return $"({TargetTypeName}){SourceTypeName} — no operator";
        }
    }

    /// <summary>
    /// A scenario where the source type is already assignable to the target type
    /// (identity or subtype), so the cast should be accepted without an operator.
    /// </summary>
    public record DirectlyAssignableCastScenario(
        string SourceTypeName,
        string TargetTypeName,
        bool IsIdentity)
    {
        public override string ToString()
        {
            return IsIdentity
                ? $"({TargetTypeName}){SourceTypeName} — identity cast"
                : $"({TargetTypeName}){SourceTypeName} — subtype cast";
        }
    }

    /// <summary>
    /// A scenario where the cast operator is defined only on a derived type,
    /// not on the compile-time type (base type). The cast should be rejected.
    /// </summary>
    public record DerivedOnlyCastScenario(
        string BaseTypeName,
        string DerivedTypeName,
        string TargetTypeName)
    {
        public override string ToString()
        {
            return $"({TargetTypeName})baseVar — operator only on {DerivedTypeName}, " +
            $"compile-time type is {BaseTypeName}";
        }
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];

    private static Gen<ValidExplicitCastScenario> GenValidScenario()
    {
        return from source in Gen.Elements(TypeNames)
               from target in Gen.Elements(TypeNames).Where(t => t != source)
               from placement in Gen.Elements(
                   OperatorPlacement.OnSourceType, OperatorPlacement.OnTargetType)
               from kind in Gen.Elements(
                   CastOperatorKind.Explicit, CastOperatorKind.Implicit)
               select new ValidExplicitCastScenario(source, target, placement, kind);
    }

    private static Gen<NoCastOperatorScenario> GenNoCastOperatorScenario()
    {
        return from source in Gen.Elements(TypeNames)
               from target in Gen.Elements(TypeNames).Where(t => t != source)
               select new NoCastOperatorScenario(source, target);
    }

    private static Gen<DirectlyAssignableCastScenario> GenDirectlyAssignableScenario()
    {
        // Identity: cast to the same type
        var identityGen =
            from name in Gen.Elements(TypeNames)
            select new DirectlyAssignableCastScenario(name, name, IsIdentity: true);

        // Subtype: source extends target (source is derived, target is base)
        var subtypeGen =
            from baseName in Gen.Elements(TypeNames)
            from derivedName in Gen.Elements(TypeNames).Where(n => n != baseName)
            select new DirectlyAssignableCastScenario(derivedName, baseName, IsIdentity: false);

        return Gen.OneOf(identityGen, subtypeGen);
    }

    private static Gen<DerivedOnlyCastScenario> GenDerivedOnlyScenario()
    {
        return from names in Gen.Shuffle(TypeNames).Select(a => a.Take(3).ToArray())
                   // names[0] = base, names[1] = derived, names[2] = target
               select new DerivedOnlyCastScenario(names[0], names[1], names[2]);
    }

    public static Arbitrary<ValidExplicitCastScenario> ArbitraryValidExplicitCastScenario()
    {
        return GenValidScenario().ToArbitrary();
    }

    public static Arbitrary<NoCastOperatorScenario> ArbitraryNoCastOperatorScenario()
    {
        return GenNoCastOperatorScenario().ToArbitrary();
    }

    public static Arbitrary<DirectlyAssignableCastScenario> ArbitraryDirectlyAssignableCastScenario()
    {
        return GenDirectlyAssignableScenario().ToArbitrary();
    }

    public static Arbitrary<DerivedOnlyCastScenario> ArbitraryDerivedOnlyCastScenario()
    {
        return GenDerivedOnlyScenario().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
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

    private static ExpressionNode MakeNewObjectExpression(string typeName)
    {
        var primary = new ObjectCreationExpressionPrimaryNode
        {
            Type = new QualifiedIdentifierNode
            {
                IDENTIFIER = Tok(typeName),
                Span = new Storm.Runtime.SourceSpan(0, typeName.Length),
            },
            Arguments = null,
        };
        return WrapPrimaryAsExpression(primary);
    }

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

    private static TypeReferenceNode MakeNamedTypeRef(string name)
    {
        return new TypeReferenceNode
        {
            First = new NamedSingleTypeRefSingleTypeRefNode
            {
                Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
            }
        };
    }

    private static ParameterNode MakeParameter(string typeName, string paramName)
    {
        return new()
        {
            Type = MakeNamedTypeRef(typeName),
            Name = Tok(paramName),
        };
    }

    private static CastOperatorKindNode MakeCastKindNode(CastOperatorKind kind)
    {
        return kind == CastOperatorKind.Implicit
            ? new ImplicitCastCastOperatorKindNode()
            : new ExplicitCastCastOperatorKindNode();
    }

    /// <summary>
    /// Builds a cast operator declaration with exactly one parameter.
    /// <c>public static {kind} operator targetType(sourceType value) { return value; }</c>
    /// </summary>
    private static CastOperatorDeclarationNode MakeCastOperatorDecl(
        CastOperatorKind kind, string targetTypeName, string sourceTypeName)
    {
        return new CastOperatorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            CastKind = MakeCastKindNode(kind),
            TargetType = MakeNamedTypeRef(targetTypeName),
            Parameters = new ParameterListNode
            {
                Parameter = MakeParameter(sourceTypeName, "value"),
            },
            Body = new OperatorBlockBodyOperatorBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new ReturnStatementStatementNode
                        {
                            ReturnStatement = new ReturnStatementNode
                            {
                                Value = MakeIdentifierExpression("value"),
                            },
                        }),
                },
            },
        };
    }

    private static MemberDeclarationNode MakeCastOperatorMember(
        CastOperatorKind kind, string targetTypeName, string sourceTypeName)
    {
        return new CastOperatorMemberMemberDeclarationNode
        {
            CastOperator = MakeCastOperatorDecl(kind, targetTypeName, sourceTypeName),
        };
    }

    private static TypeDeclarationNode MakeEmptyClassDecl(string name)
    {
        return new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = new ClassDeclarationNode
            {
                Name = Tok(name),
                Members = ImmutableArray<MemberDeclarationNode>.Empty,
            },
        };
    }

    private static TypeDeclarationNode MakeClassDeclWithMembers(
        string name, ImmutableArray<MemberDeclarationNode> members)
    {
        return new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = new ClassDeclarationNode
            {
                Name = Tok(name),
                Members = members,
            },
        };
    }

    /// <summary>
    /// Creates a class that extends a base class, with optional members.
    /// </summary>
    private static TypeDeclarationNode MakeDerivedClassDecl(
        string name, string baseClassName, ImmutableArray<MemberDeclarationNode> members)
    {
        var baseTypeRef = new BaseTypeRefNode { Name = Tok(baseClassName) };
        var baseList = new BaseListNode
        {
            First = baseTypeRef,
            Rest = baseTypeRef,
            BaseTypeRefs = ImmutableArray<BaseTypeRefNode>.Empty,
        };

        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(name),
            Members = members,
            BaseClause = baseList,
            BaseList = baseList
        };

        return new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl };
    }

    /// <summary>
    /// Creates a cast expression wrapped in a variable declaration:
    /// <c>TargetType result = (TargetType)sourceVar;</c>
    /// </summary>
    private static StatementNode MakeCastExpressionVarDecl(
        string targetTypeName, string sourceVarName)
    {
        // Build the cast expression: (TargetType)sourceVar
        var operandPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(sourceVarName) };
        var operandPostfix = new PostfixNode
        {
            Target = operandPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var operandUnary = (UnaryNode)new PostfixExpressionUnaryNode { Primary = operandPostfix };

        var castExpr = new CastExpressionUnaryNode
        {
            TargetType = MakeNamedTypeRef(targetTypeName),
            Operand = operandUnary,
        };

        // Wrap into expression chain
        var mult = new MultiplicativeNode { Left = castExpr };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assignNode = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assignNode };

        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeNamedTypeRef(targetTypeName),
                Name = Tok("result"),
                Initializer = expr,
            },
        };
    }

    /// <summary>
    /// Creates a variable declaration: <c>DeclaredType varName = new InitType();</c>
    /// </summary>
    private static StatementNode MakeTypedVarDecl(
        string declaredTypeName, string varName, string initializedTypeName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeNamedTypeRef(declaredTypeName),
                Name = Tok(varName),
                Initializer = MakeNewObjectExpression(initializedTypeName),
            },
        };
    }

    // ─── AST builders for each scenario ──────────────────────────────────

    /// <summary>
    /// Builds a compilation unit for a valid explicit cast scenario.
    /// Two unrelated classes, one of which has a cast operator (explicit or implicit).
    /// A Program class contains: SourceType src = new SourceType(); TargetType result = (TargetType)src;
    /// </summary>
    private static CompilationUnitNode BuildValidExplicitCastAst(ValidExplicitCastScenario scenario)
    {
        var sourceMembers = new List<MemberDeclarationNode>();
        var targetMembers = new List<MemberDeclarationNode>();

        var castMember = MakeCastOperatorMember(
            scenario.Kind, scenario.TargetTypeName, scenario.SourceTypeName);

        if(scenario.Placement == OperatorPlacement.OnSourceType)
        {
            sourceMembers.Add(castMember);
        }
        else
        {
            targetMembers.Add(castMember);
        }

        var mainStatements = ImmutableArray.Create(
            MakeTypedVarDecl(scenario.SourceTypeName, "src", scenario.SourceTypeName),
            MakeCastExpressionVarDecl(scenario.TargetTypeName, "src"));

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
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

        var typeDecls = new List<TypeDeclarationNode>
        {
            MakeClassDeclWithMembers(scenario.SourceTypeName, sourceMembers.ToImmutableArray()),
            MakeClassDeclWithMembers(scenario.TargetTypeName, targetMembers.ToImmutableArray()),
            MakeClassDeclWithMembers("Program", ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod })),
        };

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a compilation unit for a scenario with no cast operator.
    /// Two unrelated classes, and a cast expression from source to target.
    /// </summary>
    private static CompilationUnitNode BuildNoCastOperatorAst(NoCastOperatorScenario scenario)
    {
        var mainStatements = ImmutableArray.Create(
            MakeTypedVarDecl(scenario.SourceTypeName, "src", scenario.SourceTypeName),
            MakeCastExpressionVarDecl(scenario.TargetTypeName, "src"));

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
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

        var typeDecls = new List<TypeDeclarationNode>
        {
            MakeEmptyClassDecl(scenario.SourceTypeName),
            MakeEmptyClassDecl(scenario.TargetTypeName),
            MakeClassDeclWithMembers("Program", ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod })),
        };

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a compilation unit for a directly assignable cast scenario.
    /// For identity: same type cast. For subtype: source extends target.
    /// </summary>
    private static CompilationUnitNode BuildDirectlyAssignableCastAst(
        DirectlyAssignableCastScenario scenario)
    {
        var mainStatements = ImmutableArray.Create(
            MakeTypedVarDecl(scenario.SourceTypeName, "src", scenario.SourceTypeName),
            MakeCastExpressionVarDecl(scenario.TargetTypeName, "src"));

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
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

        var typeDecls = new List<TypeDeclarationNode>();

        if(scenario.IsIdentity)
        {
            // Identity cast: (Alpha)alpha — same type
            typeDecls.Add(MakeEmptyClassDecl(scenario.SourceTypeName));
        }
        else
        {
            // Subtype cast: source extends target (source is derived, target is base)
            typeDecls.Add(MakeEmptyClassDecl(scenario.TargetTypeName));
            typeDecls.Add(MakeDerivedClassDecl(
                scenario.SourceTypeName,
                scenario.TargetTypeName,
                ImmutableArray<MemberDeclarationNode>.Empty));
        }

        typeDecls.Add(MakeClassDeclWithMembers("Program", ImmutableArray.Create<MemberDeclarationNode>(
            new MethodMemberMemberDeclarationNode { Method = mainMethod })));

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a compilation unit for a derived-only cast scenario.
    /// The cast operator is defined on the derived type, but the variable is typed
    /// as the base type. The cast should be rejected because the compile-time type
    /// (base) does not have the operator.
    ///
    /// BaseType base class (no cast operator)
    /// DerivedType : BaseType (has explicit operator TargetType)
    /// Program: BaseType b = new DerivedType(); TargetType result = (TargetType)b;
    /// </summary>
    private static CompilationUnitNode BuildDerivedOnlyCastAst(DerivedOnlyCastScenario scenario)
    {
        // Derived type has the explicit cast operator
        var derivedMembers = ImmutableArray.Create(
            MakeCastOperatorMember(
                CastOperatorKind.Explicit, scenario.TargetTypeName, scenario.DerivedTypeName));

        var mainStatements = ImmutableArray.Create(
            // BaseType b = new DerivedType();
            MakeTypedVarDecl(scenario.BaseTypeName, "b", scenario.DerivedTypeName),
            // TargetType result = (TargetType)b;  ← compile-time type of b is BaseType
            MakeCastExpressionVarDecl(scenario.TargetTypeName, "b"));

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
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

        var typeDecls = new List<TypeDeclarationNode>
        {
            // Base type — no cast operator
            MakeEmptyClassDecl(scenario.BaseTypeName),
            // Target type — no cast operator
            MakeEmptyClassDecl(scenario.TargetTypeName),
            // Derived type extends base, has the cast operator
            MakeDerivedClassDecl(scenario.DerivedTypeName, scenario.BaseTypeName, derivedMembers),
            // Program class
            MakeClassDeclWithMembers("Program", ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod })),
        };

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Explicit cast expression compile-time safety
    //
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 9.1, 9.2, 9.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any cast expression (TargetType)expr where the compile-time type of expr
    /// declares an explicit or implicit cast operator to TargetType (on either the
    /// source type or the target type), the type checker SHALL accept the cast without
    /// emitting MIST_TC_0042 or MIST_TC_0043.
    ///
    /// This covers scenarios 1, 2, and 3 from the task:
    /// 1. Source type declares explicit operator to target → accepted
    /// 2. Target type declares explicit operator from source → accepted
    /// 3. Source type declares implicit operator to target → accepted (implicit works with explicit cast syntax)
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExplicitCastSafetyPropertyTests) },
        MaxTest = 100)]
    public Property ExplicitCast_WithOperator_AcceptsCastWithoutDiagnostic(
        ValidExplicitCastScenario scenario)
    {
        var ast = BuildValidExplicitCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var noCastDiags = diagnostics
            .Where(d => d.Id.Value == NoCastOperatorId)
            .ToArray();

        var ambiguousDiags = diagnostics
            .Where(d => d.Id.Value == AmbiguousCastId)
            .ToArray();

        return (noCastDiags.Length == 0 && ambiguousDiags.Length == 0)
            .ToProperty()
            .Label($"Expected no {NoCastOperatorId} or {AmbiguousCastId} diagnostics for " +
                   $"explicit cast ({scenario.TargetTypeName}){scenario.SourceTypeName} " +
                   $"with {scenario.Kind} operator on {scenario.Placement}, " +
                   $"but found {noCastDiags.Length} no-cast and {ambiguousDiags.Length} ambiguous. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// For any cast expression (TargetType)expr where no cast operator exists on
    /// either the compile-time type of expr or the target type, and the types are
    /// not directly assignable, the type checker SHALL emit MIST_TC_0042.
    ///
    /// Covers scenario 4: no operator exists → MIST_TC_0042 emitted.
    ///
    /// **Validates: Requirements 8.6, 8.7**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExplicitCastSafetyPropertyTests) },
        MaxTest = 100)]
    public Property ExplicitCast_WithNoOperator_EmitsNoCastOperatorDiagnostic(
        NoCastOperatorScenario scenario)
    {
        var ast = BuildNoCastOperatorAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var noCastDiags = diagnostics
            .Where(d => d.Id.Value == NoCastOperatorId)
            .ToArray();

        return (noCastDiags.Length > 0)
            .ToProperty()
            .Label($"Expected at least one {NoCastOperatorId} diagnostic for " +
                   $"explicit cast ({scenario.TargetTypeName}){scenario.SourceTypeName} " +
                   $"with no operator, but found none. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// For any cast expression (TargetType)expr where the compile-time type of expr
    /// is already assignable to the target type (identity or subtype), the type checker
    /// SHALL accept the cast without requiring a cast operator and without emitting
    /// any cast-related diagnostics.
    ///
    /// Covers scenario 5: source is already assignable to target → accepted without operator.
    ///
    /// **Validates: Requirements 8.8**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExplicitCastSafetyPropertyTests) },
        MaxTest = 100)]
    public Property ExplicitCast_WithDirectAssignability_AcceptsCastWithoutOperator(
        DirectlyAssignableCastScenario scenario)
    {
        var ast = BuildDirectlyAssignableCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var noCastDiags = diagnostics
            .Where(d => d.Id.Value == NoCastOperatorId)
            .ToArray();

        var ambiguousDiags = diagnostics
            .Where(d => d.Id.Value == AmbiguousCastId)
            .ToArray();

        return (noCastDiags.Length == 0 && ambiguousDiags.Length == 0)
            .ToProperty()
            .Label($"Expected no cast diagnostics for " +
                   $"({scenario.TargetTypeName}){scenario.SourceTypeName} " +
                   $"({(scenario.IsIdentity ? "identity" : "subtype")} cast), " +
                   $"but found {noCastDiags.Length} no-cast and {ambiguousDiags.Length} ambiguous. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// For any cast expression (TargetType)expr where the cast operator is defined
    /// only on a derived type of the compile-time type (not on the compile-time type
    /// itself), the type checker SHALL reject the cast with MIST_TC_0042. This validates
    /// the compile-time-only safety guarantee: operators on derived types are not visible
    /// through base-typed variables.
    ///
    /// **Validates: Requirements 8.7, 9.1, 9.2, 9.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExplicitCastSafetyPropertyTests) },
        MaxTest = 100)]
    public Property ExplicitCast_WithOperatorOnDerivedTypeOnly_RejectsCast(
        DerivedOnlyCastScenario scenario)
    {
        var ast = BuildDerivedOnlyCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var noCastDiags = diagnostics
            .Where(d => d.Id.Value == NoCastOperatorId)
            .ToArray();

        return (noCastDiags.Length > 0)
            .ToProperty()
            .Label($"Expected at least one {NoCastOperatorId} diagnostic for " +
                   $"({scenario.TargetTypeName})baseVar where operator is only on " +
                   $"derived type {scenario.DerivedTypeName} (compile-time type is " +
                   $"{scenario.BaseTypeName}), but found none. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}