// Feature: cast-operators, Property 4: Cast operator parameter count validation
// **Validates: Requirements 6.3, 6.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for cast operator parameter count validation.
///
/// Property 4: For any cast operator declaration with a parameter count other
/// than one (zero or more than one), the semantic model builder SHALL emit
/// exactly one diagnostic error indicating the expected parameter count.
/// For any cast operator with exactly one parameter, no parameter-count
/// diagnostic SHALL be emitted.
///
/// The diagnostic ID is <c>MIST_SB_0040</c> with message:
/// "Cast operator must have exactly one parameter; found {count}."
///
/// **Validates: Requirements 6.3, 6.4**
/// </summary>
public class CastOperatorValidationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private const string DiagnosticId = "MIST_SB_0040";

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private static DiagnosticBag BuildModelAndCollectDiagnostics(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return diagnostics;
    }

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Describes a cast operator with exactly one parameter (valid).
    /// </summary>
#pragma warning disable CA1034
    public record ValidCastOperatorScenario(
        string CastKind,
        string TargetTypeName,
        string ParamTypeName,
        string ParamName,
        string EnclosingTypeName,
        bool IsStruct)
    {
        public override string ToString() =>
            $"public static {CastKind} operator {TargetTypeName}({ParamTypeName} {ParamName}) " +
            $"in {(IsStruct ? "struct" : "class")} {EnclosingTypeName}";
    }

    /// <summary>
    /// Describes a cast operator with an invalid parameter count (zero or more than one).
    /// </summary>
    public record InvalidParamCountScenario(
        string CastKind,
        string TargetTypeName,
        string EnclosingTypeName,
        bool IsStruct,
        int ExtraParamCount,
        bool HasParameterList)
    {
        /// <summary>
        /// The total parameter count:
        /// - HasParameterList=false → 0 parameters
        /// - HasParameterList=true → 1 + ExtraParamCount parameters
        /// </summary>
        public int TotalParamCount => HasParameterList ? 1 + ExtraParamCount : 0;

        public override string ToString() =>
            $"public static {CastKind} operator {TargetTypeName}(" +
            $"{TotalParamCount} params) in {(IsStruct ? "struct" : "class")} {EnclosingTypeName}";
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Banana", "Weight", "Temperature", "Distance", "Velocity"];

    private static readonly string[] ParamNames =
        ["value", "source", "input", "item", "arg"];

    private static readonly string[] CastKinds = ["implicit", "explicit"];

    /// <summary>
    /// Generates a valid cast operator scenario (exactly one parameter).
    /// </summary>
    private static Gen<ValidCastOperatorScenario> GenValidScenario()
    {
        return from castKind in Gen.Elements(CastKinds)
               from targetType in Gen.Elements(TypeNames)
               from paramType in Gen.Elements(TypeNames)
               from paramName in Gen.Elements(ParamNames)
               from enclosingType in Gen.Elements(TypeNames)
               from isStruct in Gen.Elements(true, false)
               select new ValidCastOperatorScenario(
                   castKind, targetType, paramType, paramName,
                   enclosingType, isStruct);
    }

    /// <summary>
    /// Generates an invalid cast operator scenario (zero or more than one parameter).
    /// Either no ParameterList (0 params) or a ParameterList with 1+ extra params (2+ total).
    /// </summary>
    private static Gen<InvalidParamCountScenario> GenInvalidParamCountScenario()
    {
        return from castKind in Gen.Elements(CastKinds)
               from targetType in Gen.Elements(TypeNames)
               from enclosingType in Gen.Elements(TypeNames)
               from isStruct in Gen.Elements(true, false)
               from hasParamList in Gen.Elements(true, false)
               from extraCount in hasParamList
                   ? Gen.Choose(1, 4) // 1+ extra → 2–5 total params
                   : Gen.Constant(0)  // no param list → 0 params
               where !hasParamList || extraCount >= 1 // ensure invalid: 0 or 2+
               select new InvalidParamCountScenario(
                   castKind, targetType, enclosingType, isStruct,
                   extraCount, hasParamList);
    }

    public static Arbitrary<ValidCastOperatorScenario> ArbitraryValidScenario() =>
        GenValidScenario().ToArbitrary();

    public static Arbitrary<InvalidParamCountScenario> ArbitraryInvalidScenario() =>
        GenInvalidParamCountScenario().ToArbitrary();

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

    private static CastOperatorKindNode MakeCastKindNode(string kind) =>
        kind == "implicit"
            ? new ImplicitCastCastOperatorKindNode()
            : (CastOperatorKindNode)new ExplicitCastCastOperatorKindNode();

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
    /// Builds a cast operator declaration with exactly one parameter.
    /// </summary>
    private static CastOperatorDeclarationNode MakeValidCastOperatorDecl(
        ValidCastOperatorScenario scenario)
    {
        return new CastOperatorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            CastKind = MakeCastKindNode(scenario.CastKind),
            TargetType = MakeNamedTypeRef(scenario.TargetTypeName),
            Parameters = new ParameterListNode
            {
                Parameter = MakeParameter(scenario.ParamTypeName, scenario.ParamName),
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
                                Value = MakeIdentifierExpression(scenario.ParamName),
                            },
                        }),
                },
            },
        };
    }

    /// <summary>
    /// Builds a cast operator declaration with an invalid parameter count.
    /// </summary>
    private static CastOperatorDeclarationNode MakeInvalidCastOperatorDecl(
        InvalidParamCountScenario scenario)
    {
        ParameterListNode? parameterList;

        if (!scenario.HasParameterList)
        {
            // 0 parameters: null ParameterList
            parameterList = null;
        }
        else
        {
            // 1 + ExtraParamCount parameters (ExtraParamCount >= 1 → 2+ total)
            var extraParams = Enumerable.Range(0, scenario.ExtraParamCount)
                .Select(i => MakeParameter(TypeNames[i % TypeNames.Length], ParamNames[i % ParamNames.Length]))
                .ToImmutableArray();

            parameterList = new ParameterListNode
            {
                Parameter = MakeParameter("SourceType", "first"),
                Parameters = extraParams,
            };
        }

        return new CastOperatorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            CastKind = MakeCastKindNode(scenario.CastKind),
            TargetType = MakeNamedTypeRef(scenario.TargetTypeName),
            Parameters = parameterList,
            Body = new OperatorBlockBodyOperatorBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new ReturnStatementStatementNode
                        {
                            ReturnStatement = new ReturnStatementNode
                            {
                                Value = MakeIdentifierExpression("first"),
                            },
                        }),
                },
            },
        };
    }

    private static CompilationUnitNode BuildAst(
        string enclosingTypeName,
        bool isStruct,
        CastOperatorDeclarationNode castOpDecl)
    {
        var members = ImmutableArray.Create<MemberDeclarationNode>(
            new CastOperatorMemberMemberDeclarationNode
            {
                CastOperator = castOpDecl,
            });

        TypeDeclarationNode typeDecl;
        if (isStruct)
        {
            typeDecl = new StructTypeTypeDeclarationNode
            {
                StructDeclaration = new StructDeclarationNode
                {
                    Name = Tok(enclosingTypeName),
                    Members = members,
                },
            };
        }
        else
        {
            typeDecl = new ClassTypeTypeDeclarationNode
            {
                ClassDeclaration = new ClassDeclarationNode
                {
                    Name = Tok(enclosingTypeName),
                    Members = members,
                },
            };
        }

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create(typeDecl).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Cast operator parameter count validation
    //
    // **Validates: Requirements 6.3, 6.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any cast operator with exactly one parameter, no MIST_SB_0040
    /// diagnostic shall be emitted.
    ///
    /// **Validates: Requirement 6.3, 6.4**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorValidationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_WithOneParameter_EmitsNoParameterCountDiagnostic(
        ValidCastOperatorScenario scenario)
    {
        var castOpDecl = MakeValidCastOperatorDecl(scenario);
        var ast = BuildAst(scenario.EnclosingTypeName, scenario.IsStruct, castOpDecl);
        var diagnostics = BuildModelAndCollectDiagnostics(ast);

        var paramCountDiags = diagnostics.ToImmutableArray()
            .Where(d => d.Id.Value == DiagnosticId)
            .ToArray();

        return (paramCountDiags.Length == 0)
            .ToProperty()
            .Label($"Expected no {DiagnosticId} diagnostics for 1-parameter cast operator, " +
                   $"but found {paramCountDiags.Length}: " +
                   $"[{string.Join(", ", paramCountDiags.Select(d => d.Message))}]");
    }

    /// <summary>
    /// For any cast operator with a parameter count other than one (zero or
    /// more than one), exactly one MIST_SB_0040 diagnostic shall be emitted
    /// with the correct parameter count in the message.
    ///
    /// **Validates: Requirements 6.3, 6.4**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorValidationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_WithInvalidParamCount_EmitsExactlyOneDiagnostic(
        InvalidParamCountScenario scenario)
    {
        var castOpDecl = MakeInvalidCastOperatorDecl(scenario);
        var ast = BuildAst(scenario.EnclosingTypeName, scenario.IsStruct, castOpDecl);
        var diagnostics = BuildModelAndCollectDiagnostics(ast);

        var paramCountDiags = diagnostics.ToImmutableArray()
            .Where(d => d.Id.Value == DiagnosticId)
            .ToArray();

        var expectedCount = scenario.TotalParamCount;
        var expectedMessage = $"Cast operator must have exactly one parameter; found {expectedCount}.";

        if (paramCountDiags.Length != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 {DiagnosticId} diagnostic for {expectedCount}-parameter " +
                       $"cast operator, but found {paramCountDiags.Length}. " +
                       $"All diagnostics: [{string.Join(", ", diagnostics.ToImmutableArray().Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        var actualMessage = paramCountDiags[0].Message;
        return (actualMessage == expectedMessage)
            .ToProperty()
            .Label($"Diagnostic message mismatch. Expected: '{expectedMessage}', Actual: '{actualMessage}'");
    }
}
