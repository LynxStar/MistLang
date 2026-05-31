// Feature: cast-operators, Property 3: Cast operator enclosing type constraint validation
// **Validates: Requirements 6.2, 13.3**

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
/// Property-based tests for cast operator enclosing type constraint validation.
///
/// Property 3: For any cast operator declaration where neither the source parameter
/// type nor the target type is the enclosing type, the type checker SHALL emit exactly
/// one diagnostic error identifying the enclosing type, source type, and target type.
/// Conversely, for any cast operator where at least one of the source or target types
/// is the enclosing type, no enclosing-type-constraint diagnostic SHALL be emitted.
///
/// The diagnostic ID is <c>MIST_TC_0040</c> with message:
/// "Either the source type or the target type of a cast operator must be the enclosing
/// type '{enclosingType}'. Source: '{sourceType}', Target: '{targetType}'."
///
/// **Validates: Requirements 6.2, 13.3**
/// </summary>
public class CastOperatorValidationPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

    private const string DiagnosticId = "MIST_TC_0040";

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
    /// A cast operator where at least one of source/target matches the enclosing type (valid).
    /// </summary>
#pragma warning disable CA1034
    public record ValidEnclosingTypeScenario(
        string CastKind,
        string EnclosingTypeName,
        string SourceTypeName,
        string TargetTypeName,
        bool IsStruct)
    {
        public override string ToString() =>
            $"{CastKind} operator {TargetTypeName}({SourceTypeName} value) " +
            $"in {(IsStruct ? "struct" : "class")} {EnclosingTypeName}";
    }

    /// <summary>
    /// A cast operator where neither source nor target matches the enclosing type (invalid).
    /// </summary>
    public record InvalidEnclosingTypeScenario(
        string CastKind,
        string EnclosingTypeName,
        string SourceTypeName,
        string TargetTypeName,
        bool IsStruct)
    {
        public override string ToString() =>
            $"{CastKind} operator {TargetTypeName}({SourceTypeName} value) " +
            $"in {(IsStruct ? "struct" : "class")} {EnclosingTypeName}";
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    /// <summary>
    /// Distinct type names used for generating scenarios. Each name will be
    /// declared as a separate class/struct in the compilation unit so that
    /// the semantic model can resolve them.
    /// </summary>
    private static readonly string[] TypeNames =
        ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];

    private static readonly string[] CastKinds = ["implicit", "explicit"];

    /// <summary>
    /// Generates a valid scenario where source OR target matches the enclosing type.
    /// </summary>
    private static Gen<ValidEnclosingTypeScenario> GenValidScenario()
    {
        return from castKind in Gen.Elements(CastKinds)
               from enclosing in Gen.Elements(TypeNames)
               from other in Gen.Elements(TypeNames).Where(n => n != enclosing)
               from sourceIsEnclosing in Gen.Elements(true, false)
               from isStruct in Gen.Elements(true, false)
               let source = sourceIsEnclosing ? enclosing : other
               let target = sourceIsEnclosing ? other : enclosing
               select new ValidEnclosingTypeScenario(
                   castKind, enclosing, source, target, isStruct);
    }

    /// <summary>
    /// Generates an invalid scenario where NEITHER source nor target matches
    /// the enclosing type. All three names are distinct.
    /// </summary>
    private static Gen<InvalidEnclosingTypeScenario> GenInvalidScenario()
    {
        return from castKind in Gen.Elements(CastKinds)
               from names in Gen.Shuffle(TypeNames).Select(a => a.Take(3).ToArray())
               from isStruct in Gen.Elements(true, false)
               // names[0] = enclosing, names[1] = source, names[2] = target
               // All three are distinct, so neither source nor target is enclosing.
               select new InvalidEnclosingTypeScenario(
                   castKind, names[0], names[1], names[2], isStruct);
    }

    public static Arbitrary<ValidEnclosingTypeScenario> ArbitraryValidEnclosingTypeScenario() =>
        GenValidScenario().ToArbitrary();

    public static Arbitrary<InvalidEnclosingTypeScenario> ArbitraryInvalidEnclosingTypeScenario() =>
        GenInvalidScenario().ToArbitrary();

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
        new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
        } };

    private static ParameterNode MakeParameter(string typeName, string paramName) =>
        new()
        {
            Type = MakeNamedTypeRef(typeName),
            Name = Tok(paramName),
        };

    /// <summary>
    /// Builds a cast operator declaration with exactly one parameter.
    /// </summary>
    private static CastOperatorDeclarationNode MakeCastOperatorDecl(
        string castKind, string targetTypeName, string sourceTypeName)
    {
        return new CastOperatorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            CastKind = MakeCastKindNode(castKind),
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

    /// <summary>
    /// Creates an empty class declaration (used for auxiliary types that need
    /// to exist in the semantic model so type references resolve).
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
    /// Builds a full compilation unit with:
    /// - The enclosing type (class or struct) containing the cast operator
    /// - Empty class declarations for any other referenced types
    /// </summary>
    private static CompilationUnitNode BuildAst(
        string enclosingTypeName,
        bool isStruct,
        string sourceTypeName,
        string targetTypeName,
        string castKind)
    {
        var castOpDecl = MakeCastOperatorDecl(castKind, targetTypeName, sourceTypeName);

        var members = ImmutableArray.Create<MemberDeclarationNode>(
            new CastOperatorMemberMemberDeclarationNode
            {
                CastOperator = castOpDecl,
            });

        // Build the enclosing type declaration
        TypeDeclarationNode enclosingTypeDecl;
        if (isStruct)
        {
            enclosingTypeDecl = new StructTypeTypeDeclarationNode
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
            enclosingTypeDecl = new ClassTypeTypeDeclarationNode
            {
                ClassDeclaration = new ClassDeclarationNode
                {
                    Name = Tok(enclosingTypeName),
                    Members = members,
                },
            };
        }

        // Collect all distinct type names that need declarations
        var allTypeNames = new HashSet<string> { enclosingTypeName, sourceTypeName, targetTypeName };
        var typeDecls = new List<TypeDeclarationNode> { enclosingTypeDecl };

        // Add empty class declarations for any types that aren't the enclosing type
        foreach (var typeName in allTypeNames)
        {
            if (typeName != enclosingTypeName)
            {
                typeDecls.Add(MakeEmptyClassDecl(typeName));
            }
        }

        return new CompilationUnitNode
        {
            Items = typeDecls.ToImmutableArray().AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Cast operator enclosing type constraint validation
    //
    // **Validates: Requirements 6.2, 13.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any cast operator where at least one of the source or target types
    /// is the enclosing type, no MIST_TC_0040 diagnostic shall be emitted.
    ///
    /// **Validates: Requirements 6.2, 13.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorValidationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_WithEnclosingTypeMatch_EmitsNoEnclosingTypeDiagnostic(
        ValidEnclosingTypeScenario scenario)
    {
        var ast = BuildAst(
            scenario.EnclosingTypeName,
            scenario.IsStruct,
            scenario.SourceTypeName,
            scenario.TargetTypeName,
            scenario.CastKind);

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var enclosingTypeDiags = diagnostics
            .Where(d => d.Id.Value == DiagnosticId)
            .ToArray();

        return (enclosingTypeDiags.Length == 0)
            .ToProperty()
            .Label($"Expected no {DiagnosticId} diagnostics for cast operator where " +
                   $"source='{scenario.SourceTypeName}' or target='{scenario.TargetTypeName}' " +
                   $"matches enclosing type '{scenario.EnclosingTypeName}', " +
                   $"but found {enclosingTypeDiags.Length}: " +
                   $"[{string.Join(", ", enclosingTypeDiags.Select(d => d.Message))}]");
    }

    /// <summary>
    /// For any cast operator where neither the source parameter type nor the
    /// target type is the enclosing type, exactly one MIST_TC_0040 diagnostic
    /// shall be emitted with the correct enclosing type, source type, and
    /// target type in the message.
    ///
    /// **Validates: Requirements 6.2, 13.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorValidationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_WithNoEnclosingTypeMatch_EmitsExactlyOneDiagnostic(
        InvalidEnclosingTypeScenario scenario)
    {
        var ast = BuildAst(
            scenario.EnclosingTypeName,
            scenario.IsStruct,
            scenario.SourceTypeName,
            scenario.TargetTypeName,
            scenario.CastKind);

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var enclosingTypeDiags = diagnostics
            .Where(d => d.Id.Value == DiagnosticId)
            .ToArray();

        var expectedMessage =
            $"Either the source type or the target type of a cast operator must be " +
            $"the enclosing type '{scenario.EnclosingTypeName}'. " +
            $"Source: '{scenario.SourceTypeName}', Target: '{scenario.TargetTypeName}'.";

        if (enclosingTypeDiags.Length != 1)
        {
            return false.ToProperty()
                .Label($"Expected exactly 1 {DiagnosticId} diagnostic for cast operator where " +
                       $"neither source='{scenario.SourceTypeName}' nor target='{scenario.TargetTypeName}' " +
                       $"matches enclosing type '{scenario.EnclosingTypeName}', " +
                       $"but found {enclosingTypeDiags.Length}. " +
                       $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }

        var actualMessage = enclosingTypeDiags[0].Message;
        return (actualMessage == expectedMessage)
            .ToProperty()
            .Label($"Diagnostic message mismatch.\n" +
                   $"Expected: '{expectedMessage}'\n" +
                   $"Actual:   '{actualMessage}'");
    }
}
