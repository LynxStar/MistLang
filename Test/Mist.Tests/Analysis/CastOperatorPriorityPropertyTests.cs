// Feature: cast-operators, Property 8: Direct assignability takes precedence over implicit casts
// **Validates: Requirements 14.1, 14.2, 14.3**

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
/// Property-based tests for direct assignability precedence over implicit casts.
///
/// Property 8: For any program where a type is both directly assignable to a target
/// type (via identity, subtype, abstract type implementation, or numeric
/// widening) AND defines an implicit cast operator to that target type, the type checker
/// SHALL use the direct assignability path and SHALL NOT record a resolved cast operator
/// on the assignment node.
///
/// **Validates: Requirements 14.1, 14.2, 14.3**
/// </summary>
public class CastOperatorPriorityPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    private const string TypeMismatchId = "MIST_TC_0001";
    private const string AmbiguousImplicitCastId = "MIST_TC_0044";

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
    /// A scenario where a derived class defines an implicit cast operator to its
    /// base class. When assigning derived to base, the type checker should use the
    /// subtype relationship (direct assignability) and NOT invoke the implicit cast.
    /// </summary>
#pragma warning disable CA1034
    public record SubtypeWithImplicitCastScenario(
        string BaseTypeName,
        string DerivedTypeName)
    {
        public override string ToString()
        {
            return $"{DerivedTypeName} : {BaseTypeName} with implicit operator {BaseTypeName}({DerivedTypeName})";
        }
    }

    /// <summary>
    /// A scenario where a type defines an implicit cast operator to itself (identity).
    /// When assigning the type to itself, the type checker should use identity
    /// assignability and NOT invoke the implicit cast.
    /// </summary>
    public record IdentityWithImplicitCastScenario(
        string TypeName)
    {
        public override string ToString()
        {
            return $"{TypeName} with implicit operator {TypeName}({TypeName}) — identity";
        }
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];

    private static Gen<SubtypeWithImplicitCastScenario> GenSubtypeScenario()
    {
        return from baseName in Gen.Elements(TypeNames)
               from derivedName in Gen.Elements(TypeNames).Where(n => n != baseName)
               select new SubtypeWithImplicitCastScenario(baseName, derivedName);
    }

    private static Gen<IdentityWithImplicitCastScenario> GenIdentityScenario()
    {
        return from name in Gen.Elements(TypeNames)
               select new IdentityWithImplicitCastScenario(name);
    }

    public static Arbitrary<SubtypeWithImplicitCastScenario> ArbitrarySubtypeWithImplicitCastScenario()
    {
        return GenSubtypeScenario().ToArbitrary();
    }

    public static Arbitrary<IdentityWithImplicitCastScenario> ArbitraryIdentityWithImplicitCastScenario()
    {
        return GenIdentityScenario().ToArbitrary();
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

    /// <summary>
    /// Builds an implicit cast operator declaration with exactly one parameter.
    /// <c>public static implicit operator targetType(sourceType value) { return value; }</c>
    /// </summary>
    private static CastOperatorDeclarationNode MakeImplicitCastOperatorDecl(
        string targetTypeName, string sourceTypeName)
    {
        return new CastOperatorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            CastKind = new ImplicitCastCastOperatorKindNode(),
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
        string targetTypeName, string sourceTypeName)
    {
        return new CastOperatorMemberMemberDeclarationNode
        {
            CastOperator = MakeImplicitCastOperatorDecl(targetTypeName, sourceTypeName),
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
    /// Creates a variable declaration statement: <c>DeclaredType varName = new InitType();</c>
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

    /// <summary>
    /// Creates a variable declaration with an identifier initializer:
    /// <c>DeclaredType varName = identifierName;</c>
    /// Returns both the statement and the initializer expression node for later inspection.
    /// </summary>
    private static (StatementNode Statement, ExpressionNode Initializer)
        MakeTypedVarDeclFromIdentifierWithInitializer(
            string declaredTypeName, string varName, string identifierName)
    {
        var initializer = MakeIdentifierExpression(identifierName);
        var stmt = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeNamedTypeRef(declaredTypeName),
                Name = Tok(varName),
                Initializer = initializer,
            },
        };
        return (stmt, initializer);
    }

    // ─── AST builders for each scenario ──────────────────────────────────

    /// <summary>
    /// Builds a compilation unit for a subtype-with-implicit-cast scenario.
    ///
    /// BaseType (empty base class)
    /// DerivedType : BaseType (defines implicit operator BaseType(DerivedType value))
    /// Program:
    ///   DerivedType d = new DerivedType();
    ///   BaseType b = d;  ← should use subtype assignability, NOT the implicit cast
    ///
    /// Returns the AST and the initializer expression node for the assignment
    /// so we can check whether a resolved operator was recorded on it.
    /// </summary>
    private static (CompilationUnitNode Ast, ExpressionNode AssignmentInitializer)
        BuildSubtypeWithImplicitCastAst(SubtypeWithImplicitCastScenario scenario)
    {
        // Derived type defines: public static implicit operator BaseType(DerivedType value)
        var derivedMembers = ImmutableArray.Create(
            MakeCastOperatorMember(scenario.BaseTypeName, scenario.DerivedTypeName));

        // Build the assignment: BaseType b = d;
        var (assignStmt, assignInitializer) =
            MakeTypedVarDeclFromIdentifierWithInitializer(
                scenario.BaseTypeName, "b", "d");

        var mainStatements = ImmutableArray.Create(
            MakeTypedVarDecl(scenario.DerivedTypeName, "d", scenario.DerivedTypeName),
            assignStmt);

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
            MakeEmptyClassDecl(scenario.BaseTypeName),
            MakeDerivedClassDecl(scenario.DerivedTypeName, scenario.BaseTypeName, derivedMembers),
            MakeClassDeclWithMembers("Program", ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod })),
        };

        var ast = new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };

        return (ast, assignInitializer);
    }

    /// <summary>
    /// Builds a compilation unit for an identity-with-implicit-cast scenario.
    ///
    /// TypeName (defines implicit operator TypeName(TypeName value))
    /// Program:
    ///   TypeName a = new TypeName();
    ///   TypeName b = a;  ← should use identity assignability, NOT the implicit cast
    ///
    /// Returns the AST and the initializer expression node for the assignment.
    /// </summary>
    private static (CompilationUnitNode Ast, ExpressionNode AssignmentInitializer)
        BuildIdentityWithImplicitCastAst(IdentityWithImplicitCastScenario scenario)
    {
        // Type defines: public static implicit operator TypeName(TypeName value)
        var members = ImmutableArray.Create(
            MakeCastOperatorMember(scenario.TypeName, scenario.TypeName));

        // Build the assignment: TypeName b = a;
        var (assignStmt, assignInitializer) =
            MakeTypedVarDeclFromIdentifierWithInitializer(
                scenario.TypeName, "b", "a");

        var mainStatements = ImmutableArray.Create(
            MakeTypedVarDecl(scenario.TypeName, "a", scenario.TypeName),
            assignStmt);

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
            MakeClassDeclWithMembers(scenario.TypeName, members),
            MakeClassDeclWithMembers("Program", ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod })),
        };

        var ast = new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };

        return (ast, assignInitializer);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Direct assignability takes precedence over implicit casts
    //
    // **Validates: Requirements 14.1, 14.2, 14.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any program where a derived class defines an implicit cast operator to its
    /// base class, and an assignment assigns the derived type to the base type, the type
    /// checker SHALL use the subtype relationship (direct assignability) and SHALL NOT
    /// record a resolved cast operator on the assignment node. No type mismatch or
    /// ambiguity diagnostics shall be emitted.
    ///
    /// **Validates: Requirements 14.1, 14.2, 14.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorPriorityPropertyTests) },
        MaxTest = 100)]
    public Property SubtypeAssignability_TakesPrecedence_OverImplicitCast(
        SubtypeWithImplicitCastScenario scenario)
    {
        var (ast, assignInitializer) = BuildSubtypeWithImplicitCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // 1. No type mismatch or ambiguity diagnostics should be emitted.
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == TypeMismatchId)
            .ToArray();

        var ambiguityDiags = diagnostics
            .Where(d => d.Id.Value == AmbiguousImplicitCastId)
            .ToArray();

        var noDiagnostics = typeMismatchDiags.Length == 0 && ambiguityDiags.Length == 0;

        // 2. No resolved cast operator should be recorded on the assignment initializer.
        //    The subtype relationship should handle the assignability, not the implicit cast.
        var resolvedOp = model.GetResolvedOperator(assignInitializer);
        var noResolvedOperator = resolvedOp is null;

        return (noDiagnostics && noResolvedOperator)
            .ToProperty()
            .Label($"Expected subtype assignability to take precedence over implicit cast for " +
                   $"{scenario.DerivedTypeName} → {scenario.BaseTypeName}. " +
                   $"Type mismatch diags: {typeMismatchDiags.Length}, " +
                   $"ambiguity diags: {ambiguityDiags.Length}, " +
                   $"resolved operator: {(resolvedOp is null ? "null (correct)" : resolvedOp.Name + " (should be null)")}. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// For any program where a type defines an implicit cast operator to itself (identity),
    /// and an assignment assigns the type to itself, the type checker SHALL use identity
    /// assignability and SHALL NOT record a resolved cast operator on the assignment node.
    ///
    /// **Validates: Requirements 14.1, 14.2**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorPriorityPropertyTests) },
        MaxTest = 100)]
    public Property IdentityAssignability_TakesPrecedence_OverImplicitCast(
        IdentityWithImplicitCastScenario scenario)
    {
        var (ast, assignInitializer) = BuildIdentityWithImplicitCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // 1. No type mismatch or ambiguity diagnostics should be emitted.
        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == TypeMismatchId)
            .ToArray();

        var ambiguityDiags = diagnostics
            .Where(d => d.Id.Value == AmbiguousImplicitCastId)
            .ToArray();

        var noDiagnostics = typeMismatchDiags.Length == 0 && ambiguityDiags.Length == 0;

        // 2. No resolved cast operator should be recorded on the assignment initializer.
        //    Identity assignability should handle this, not the implicit cast.
        var resolvedOp = model.GetResolvedOperator(assignInitializer);
        var noResolvedOperator = resolvedOp is null;

        return (noDiagnostics && noResolvedOperator)
            .ToProperty()
            .Label($"Expected identity assignability to take precedence over implicit cast for " +
                   $"{scenario.TypeName} → {scenario.TypeName}. " +
                   $"Type mismatch diags: {typeMismatchDiags.Length}, " +
                   $"ambiguity diags: {ambiguityDiags.Length}, " +
                   $"resolved operator: {(resolvedOp is null ? "null (correct)" : resolvedOp.Name + " (should be null)")}. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}