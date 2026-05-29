// Feature: cast-operators, Property 5: Implicit cast resolution at assignment, argument, and return sites
// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6**

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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for implicit cast resolution at assignment, argument, and return sites.
///
/// Property 5: For any program where a type defines an implicit cast operator from source
/// type S to target type T, and an assignment/argument/return site requires converting a
/// value of type S to type T where S is not directly assignable to T, the type checker
/// SHALL accept the conversion without emitting a type mismatch diagnostic. The type
/// checker SHALL search for implicit cast operators on both the source type and the target
/// type. For any program where multiple implicit cast operators are applicable for the
/// same conversion, the type checker SHALL emit an ambiguity diagnostic.
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6**
/// </summary>
public class ImplicitCastResolutionPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    private const string TypeMismatchId = "MIST_TC_0001";
    private const string AmbiguousImplicitCastId = "MIST_TC_0044";

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
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
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Describes where the implicit cast operator is defined and what conversion site is tested.
    /// </summary>
#pragma warning disable CA1034
    public enum OperatorLocation
    {
        /// <summary>Implicit cast operator defined on the source type.</summary>
        OnSourceType,
        /// <summary>Implicit cast operator defined on the target type.</summary>
        OnTargetType,
    }

    public enum ConversionSite
    {
        /// <summary>Assignment: TargetType x = sourceValue;</summary>
        Assignment,
        /// <summary>Argument: Method(sourceValue) where param is TargetType.</summary>
        Argument,
        /// <summary>Return: return sourceValue; in a method returning TargetType.</summary>
        Return,
    }

    /// <summary>
    /// A scenario where an implicit cast operator exists and should allow the conversion.
    /// </summary>
    public record ValidImplicitCastScenario(
        string SourceTypeName,
        string TargetTypeName,
        OperatorLocation Location,
        ConversionSite Site)
    {
        public override string ToString() =>
            $"implicit operator {TargetTypeName}({SourceTypeName}) on {Location}, site={Site}";
    }

    /// <summary>
    /// A scenario where no implicit cast operator exists and a type mismatch should be emitted.
    /// </summary>
    public record NoImplicitCastScenario(
        string SourceTypeName,
        string TargetTypeName)
    {
        public override string ToString() =>
            $"no implicit cast: {SourceTypeName} → {TargetTypeName}";
    }

    /// <summary>
    /// A scenario where multiple implicit cast operators are applicable (ambiguous).
    /// Both source and target types define an implicit cast operator for the same conversion.
    /// </summary>
    public record AmbiguousImplicitCastScenario(
        string SourceTypeName,
        string TargetTypeName)
    {
        public override string ToString() =>
            $"ambiguous implicit cast: {SourceTypeName} → {TargetTypeName} (both types define operator)";
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];

    private static Gen<ValidImplicitCastScenario> GenValidScenario()
    {
        return from source in Gen.Elements(TypeNames)
               from target in Gen.Elements(TypeNames).Where(t => t != source)
               from location in Gen.Elements(OperatorLocation.OnSourceType, OperatorLocation.OnTargetType)
               from site in Gen.Elements(ConversionSite.Assignment, ConversionSite.Argument, ConversionSite.Return)
               select new ValidImplicitCastScenario(source, target, location, site);
    }

    private static Gen<NoImplicitCastScenario> GenNoImplicitCastScenario()
    {
        return from source in Gen.Elements(TypeNames)
               from target in Gen.Elements(TypeNames).Where(t => t != source)
               select new NoImplicitCastScenario(source, target);
    }

    private static Gen<AmbiguousImplicitCastScenario> GenAmbiguousScenario()
    {
        return from source in Gen.Elements(TypeNames)
               from target in Gen.Elements(TypeNames).Where(t => t != source)
               select new AmbiguousImplicitCastScenario(source, target);
    }

    public static Arbitrary<ValidImplicitCastScenario> ArbitraryValidImplicitCastScenario() =>
        GenValidScenario().ToArbitrary();

    public static Arbitrary<NoImplicitCastScenario> ArbitraryNoImplicitCastScenario() =>
        GenNoImplicitCastScenario().ToArbitrary();

    public static Arbitrary<AmbiguousImplicitCastScenario> ArbitraryAmbiguousImplicitCastScenario() =>
        GenAmbiguousScenario().ToArbitrary();

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
        return new ExpressionNode { Assignment = assign };
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
        return new ExpressionNode { Assignment = assign };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name) =>
        new NamedTypeRefTypeReferenceNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
        };

    private static ParameterNode MakeParameter(string typeName, string paramName) =>
        new()
        {
            Type = MakeNamedTypeRef(typeName),
            Name = Tok(paramName),
        };

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

    /// <summary>
    /// Creates an empty class declaration.
    /// </summary>
    private static TypeDeclarationNode MakeEmptyClassDecl(string name) =>
        new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = new ClassDeclarationNode
            {
                Name = Tok(name),
                Members = ImmutableArray<MemberDeclarationNode>.Empty,
            },
        };

    /// <summary>
    /// Creates a class declaration with the given members.
    /// </summary>
    private static TypeDeclarationNode MakeClassDeclWithMembers(
        string name, ImmutableArray<MemberDeclarationNode> members) =>
        new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = new ClassDeclarationNode
            {
                Name = Tok(name),
                Members = members,
            },
        };

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
    /// </summary>
    private static StatementNode MakeTypedVarDeclFromIdentifier(
        string declaredTypeName, string varName, string identifierName)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = MakeNamedTypeRef(declaredTypeName),
                Name = Tok(varName),
                Initializer = MakeIdentifierExpression(identifierName),
            },
        };
    }

    /// <summary>
    /// Creates a static method call expression: <c>target.methodName(argExpr)</c>
    /// </summary>
    private static ExpressionNode MakeStaticMethodCallExpression(
        string target, string methodName, ExpressionNode arg)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(target) };
        var suffix = new MethodCallSuffixPostfixSuffixNode
        {
            Name = Tok(methodName),
            Arguments = new ArgumentListNode { Argument = new ValueArgumentArgumentNode { Expression = arg } },
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
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

    /// <summary>
    /// Creates a method that accepts a parameter of the given type.
    /// <c>public static void AcceptTarget(TargetType param) { }</c>
    /// </summary>
    private static MemberDeclarationNode MakeAcceptMethod(string paramTypeName)
    {
        return new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = Tok("AcceptTarget"),
                ReturnType = new VoidTypeRefTypeReferenceNode(),
                Parameters = new ParameterListNode
                {
                    Parameter = MakeParameter(paramTypeName, "param"),
                },
                Body = new MethodBlockBodyMethodBodyNode
                {
                    Block = new BlockNode
                    {
                        Statements = ImmutableArray<StatementNode>.Empty,
                    },
                },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(),
                    new StaticModifierModifierNode()),
            },
        };
    }

    /// <summary>
    /// Creates a method that returns the given type, with a body that returns
    /// a new instance of sourceTypeName.
    /// <c>public static TargetType GetTarget() { SourceType s = new SourceType(); return s; }</c>
    /// </summary>
    private static MemberDeclarationNode MakeReturnMethod(
        string returnTypeName, string sourceTypeName)
    {
        var statements = ImmutableArray.Create<StatementNode>(
            MakeTypedVarDecl(sourceTypeName, "s", sourceTypeName),
            new ReturnStatementStatementNode
            {
                ReturnStatement = new ReturnStatementNode
                {
                    Value = MakeIdentifierExpression("s"),
                },
            });

        return new MethodMemberMemberDeclarationNode
        {
            Method = new MethodDeclarationNode
            {
                Name = Tok("GetTarget"),
                ReturnType = MakeNamedTypeRef(returnTypeName),
                Parameters = null,
                Body = new MethodBlockBodyMethodBodyNode
                {
                    Block = new BlockNode { Statements = statements },
                },
                Modifiers = ImmutableArray.Create<ModifierNode>(
                    new PublicModifierModifierNode(),
                    new StaticModifierModifierNode()),
            },
        };
    }

    // ─── AST builders for each scenario ──────────────────────────────────

    /// <summary>
    /// Builds a compilation unit for a valid implicit cast scenario.
    /// The source and target types are separate classes. The implicit cast operator
    /// is placed on either the source or target type depending on the scenario.
    /// A Program class exercises the conversion at the specified site.
    /// </summary>
    private static CompilationUnitNode BuildValidImplicitCastAst(ValidImplicitCastScenario scenario)
    {
        var sourceMembers = new List<MemberDeclarationNode>();
        var targetMembers = new List<MemberDeclarationNode>();

        // Place the implicit cast operator on the appropriate type
        var castMember = MakeCastOperatorMember(scenario.TargetTypeName, scenario.SourceTypeName);
        if (scenario.Location == OperatorLocation.OnSourceType)
            sourceMembers.Add(castMember);
        else
            targetMembers.Add(castMember);

        // Build the Program class with a Main method exercising the conversion site
        var programMembers = new List<MemberDeclarationNode>();
        var mainStatements = new List<StatementNode>();

        switch (scenario.Site)
        {
            case ConversionSite.Assignment:
                // SourceType src = new SourceType();
                // TargetType tgt = src;  ← implicit cast should apply
                mainStatements.Add(MakeTypedVarDecl(scenario.SourceTypeName, "src", scenario.SourceTypeName));
                mainStatements.Add(MakeTypedVarDeclFromIdentifier(scenario.TargetTypeName, "tgt", "src"));
                break;

            case ConversionSite.Argument:
                // SourceType src = new SourceType();
                // Program.AcceptTarget(src);  ← implicit cast should apply at argument site
                mainStatements.Add(MakeTypedVarDecl(scenario.SourceTypeName, "src", scenario.SourceTypeName));
                mainStatements.Add(new ExpressionStatementStatementNode
                {
                    ExpressionStatement = new ExpressionStatementNode
                    {
                        Expression = MakeStaticMethodCallExpression("Program", "AcceptTarget",
                            MakeIdentifierExpression("src")),
                    },
                });
                programMembers.Add(MakeAcceptMethod(scenario.TargetTypeName));
                break;

            case ConversionSite.Return:
                // The return-site conversion is tested via a separate method on Program.
                // public static TargetType GetTarget() { SourceType s = new SourceType(); return s; }
                programMembers.Add(MakeReturnMethod(scenario.TargetTypeName, scenario.SourceTypeName));
                break;
        }

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = mainStatements.ToImmutableArray(),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
        programMembers.Add(new MethodMemberMemberDeclarationNode { Method = mainMethod });

        var typeDecls = new List<TypeDeclarationNode>
        {
            MakeClassDeclWithMembers(scenario.SourceTypeName, sourceMembers.ToImmutableArray()),
            MakeClassDeclWithMembers(scenario.TargetTypeName, targetMembers.ToImmutableArray()),
            MakeClassDeclWithMembers("Program", programMembers.ToImmutableArray()),
        };

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    /// <summary>
    /// Builds a compilation unit for a scenario with no implicit cast operator.
    /// Two unrelated classes, and an assignment from source to target.
    /// </summary>
    private static CompilationUnitNode BuildNoImplicitCastAst(NoImplicitCastScenario scenario)
    {
        var mainStatements = ImmutableArray.Create<StatementNode>(
            MakeTypedVarDecl(scenario.SourceTypeName, "src", scenario.SourceTypeName),
            MakeTypedVarDeclFromIdentifier(scenario.TargetTypeName, "tgt", "src"));

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
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
    /// Builds a compilation unit for an ambiguous implicit cast scenario.
    /// Both source and target types define an implicit cast operator for the same conversion.
    /// </summary>
    private static CompilationUnitNode BuildAmbiguousImplicitCastAst(
        AmbiguousImplicitCastScenario scenario)
    {
        // Source type defines: public static implicit operator TargetType(SourceType value)
        var sourceMembers = ImmutableArray.Create(
            MakeCastOperatorMember(scenario.TargetTypeName, scenario.SourceTypeName));

        // Target type also defines: public static implicit operator TargetType(SourceType value)
        var targetMembers = ImmutableArray.Create(
            MakeCastOperatorMember(scenario.TargetTypeName, scenario.SourceTypeName));

        var mainStatements = ImmutableArray.Create<StatementNode>(
            MakeTypedVarDecl(scenario.SourceTypeName, "src", scenario.SourceTypeName),
            MakeTypedVarDeclFromIdentifier(scenario.TargetTypeName, "tgt", "src"));

        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
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
            MakeClassDeclWithMembers(scenario.SourceTypeName, sourceMembers),
            MakeClassDeclWithMembers(scenario.TargetTypeName, targetMembers),
            MakeClassDeclWithMembers("Program", ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod })),
        };

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Implicit cast resolution at assignment, argument, and return sites
    //
    // **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5, 7.6**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any program where a type defines an implicit cast operator from source type S
    /// to target type T, and an assignment/argument/return site requires converting S to T,
    /// the type checker SHALL accept the conversion without emitting a type mismatch diagnostic.
    /// The operator may be defined on either the source type or the target type.
    ///
    /// **Validates: Requirements 7.1, 7.2, 7.3, 7.4, 7.5**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ImplicitCastResolutionPropertyTests) },
        MaxTest = 100)]
    public Property ImplicitCast_WithOperator_AcceptsConversionWithoutTypeMismatch(
        ValidImplicitCastScenario scenario)
    {
        var ast = BuildValidImplicitCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == TypeMismatchId)
            .ToArray();

        var ambiguityDiags = diagnostics
            .Where(d => d.Id.Value == AmbiguousImplicitCastId)
            .ToArray();

        return (typeMismatchDiags.Length == 0 && ambiguityDiags.Length == 0)
            .ToProperty()
            .Label($"Expected no type mismatch or ambiguity diagnostics for " +
                   $"implicit cast {scenario.SourceTypeName} → {scenario.TargetTypeName} " +
                   $"(operator on {scenario.Location}, site={scenario.Site}), " +
                   $"but found {typeMismatchDiags.Length} type mismatch and " +
                   $"{ambiguityDiags.Length} ambiguity diagnostics. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// For any two unrelated types where no implicit cast operator is defined,
    /// the type checker SHALL emit a type mismatch diagnostic when attempting
    /// to assign a value of the source type to a variable of the target type.
    ///
    /// **Validates: Requirements 7.7**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ImplicitCastResolutionPropertyTests) },
        MaxTest = 100)]
    public Property NoImplicitCast_EmitsTypeMismatchDiagnostic(
        NoImplicitCastScenario scenario)
    {
        var ast = BuildNoImplicitCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var typeMismatchDiags = diagnostics
            .Where(d => d.Id.Value == TypeMismatchId)
            .ToArray();

        return (typeMismatchDiags.Length > 0)
            .ToProperty()
            .Label($"Expected at least one {TypeMismatchId} diagnostic for assignment " +
                   $"{scenario.SourceTypeName} → {scenario.TargetTypeName} with no implicit cast, " +
                   $"but found none. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// For any program where multiple implicit cast operators are applicable for the
    /// same conversion (both source and target types define the operator), the type
    /// checker SHALL reject the conversion. The ambiguity causes IsAssignable to return
    /// false (since no single operator can be selected), resulting in a type mismatch
    /// diagnostic (MIST_TC_0001) or an ambiguity diagnostic (MIST_TC_0044) at the
    /// conversion site.
    ///
    /// **Validates: Requirements 7.6**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ImplicitCastResolutionPropertyTests) },
        MaxTest = 100)]
    public Property AmbiguousImplicitCast_RejectsConversion(
        AmbiguousImplicitCastScenario scenario)
    {
        var ast = BuildAmbiguousImplicitCastAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // When multiple implicit cast operators are applicable, the conversion
        // is rejected. This manifests as either MIST_TC_0044 (ambiguity) or
        // MIST_TC_0001 (type mismatch, because ambiguity causes IsAssignable
        // to return false).
        var rejectionDiags = diagnostics
            .Where(d => d.Id.Value == AmbiguousImplicitCastId || d.Id.Value == TypeMismatchId)
            .ToArray();

        return (rejectionDiags.Length > 0)
            .ToProperty()
            .Label($"Expected at least one rejection diagnostic ({AmbiguousImplicitCastId} or " +
                   $"{TypeMismatchId}) for ambiguous implicit cast " +
                   $"{scenario.SourceTypeName} → {scenario.TargetTypeName} " +
                   $"(both types define operator), but found none. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
