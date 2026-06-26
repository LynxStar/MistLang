// Feature: extension-methods, Property 11: Set Accessor Rejection
//
// **Validates: Requirements 6.5, 20.3**
//
// For any extension property that declares a `set` or `init` accessor,
// the compiler SHALL emit diagnostic `MIST_SB_0071`.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for extension set accessor rejection.
///
/// **Validates: Requirements 6.5, 20.3**
///
/// For any extension property that declares a `set` or `init` accessor,
/// the compiler SHALL emit diagnostic `MIST_SB_0071`.
/// </summary>
public class ExtensionSetAccessorRejectionPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static (Mist.Semantics.Contracts.ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics)
        BuildModelWithDiagnostics(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        var model = Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── Scenario types ──────────────────────────────────────────────────

    /// <summary>
    /// Describes a scenario where an extension property has a set or init accessor.
    /// </summary>
#pragma warning disable CA1034
    public record SetAccessorScenario(
        string ReceiverTypeName,
        string ReceiverParamName,
        string PropertyName,
        string PropertyType,
        AccessorKind Kind)
    {
        public override string ToString()
        {
            return $"extension({ReceiverTypeName} {ReceiverParamName}) {{ {PropertyType} {PropertyName} {{ get; {Kind.ToString().ToLowerInvariant()}; }} }}";
        }
    }

    public enum AccessorKind
    {
        AutoSet,
        BlockSet,
        ExpressionSet,
        AutoInit,
        BlockInit,
        ExpressionInit,
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] ReceiverTypeNames =
        ["MyClass", "Widget", "Container", "Service", "Handler"];

    private static readonly string[] ReceiverParamNames =
        ["self", "value", "item"];

    private static readonly string[] PropertyNames =
        ["Length", "Count", "IsEmpty", "Name", "Status",
         "Size", "Capacity", "IsValid", "Label", "Tag"];

    private static readonly string[] PropertyTypes =
        ["int", "string", "bool", "double"];

    private static Gen<SetAccessorScenario> GenScenario()
    {
        return from receiverType in Gen.Elements(ReceiverTypeNames)
               from receiverParam in Gen.Elements(ReceiverParamNames)
               from propName in Gen.Elements(PropertyNames)
               from propType in Gen.Elements(PropertyTypes)
               from kind in Gen.Elements(
                   AccessorKind.AutoSet,
                   AccessorKind.BlockSet,
                   AccessorKind.ExpressionSet,
                   AccessorKind.AutoInit,
                   AccessorKind.BlockInit,
                   AccessorKind.ExpressionInit)
               select new SetAccessorScenario(receiverType, receiverParam, propName, propType, kind);
    }

    public static Arbitrary<SetAccessorScenario> ArbitrarySetAccessorScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

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

    private static ExpressionNode MakeFieldAssignment()
    {
        var fieldPrimary = new FieldKeywordExpressionPrimaryNode();
        var fieldPostfix = new PostfixNode
        {
            Target = fieldPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var fieldUnary = new PostfixExpressionUnaryNode { Primary = fieldPostfix };
        var fieldMult = new MultiplicativeNode { Left = fieldUnary };
        var fieldAdd = new AdditiveNode { Left = fieldMult };
        var fieldRel = new RelationalComparisonRelationalNode { Left = fieldAdd };
        var fieldEq = new EqualityNode { Left = fieldRel };
        var fieldLogAnd = new LogicalAndNode { Left = fieldEq };
        var fieldLogOr = new LogicalOrNode { Left = fieldLogAnd };

        var valuePrimary = new IdentifierExpressionPrimaryNode
        {
            Name = Tok("value"),
        };
        var valuePostfix = new PostfixNode
        {
            Target = valuePrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var valueUnary = new PostfixExpressionUnaryNode { Primary = valuePostfix };
        var valueMult = new MultiplicativeNode { Left = valueUnary };
        var valueAdd = new AdditiveNode { Left = valueMult };
        var valueRel = new RelationalComparisonRelationalNode { Left = valueAdd };
        var valueEq = new EqualityNode { Left = valueRel };
        var valueLogAnd = new LogicalAndNode { Left = valueEq };
        var valueLogOr = new LogicalOrNode { Left = valueLogAnd };
        var valueAssign = new AssignmentNode { Target = new VoidCoalescingNode { Left = valueLogOr } };

        return new AssignmentFormExpressionNode
        {
            Assignment = new AssignmentNode
            {
                Target = new VoidCoalescingNode { Left = fieldLogOr },
                Value = new AssignmentFormExpressionNode { Assignment = valueAssign },
            },
        };
    }

    private static AccessorDeclarationNode MakeSetOrInitAccessor(AccessorKind kind)
    {
        return kind switch
        {
            AccessorKind.AutoSet => new AutoSetAccessorAccessorDeclarationNode(),
            AccessorKind.BlockSet => new BlockSetAccessorAccessorDeclarationNode
            {
                Body = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new ExpressionStatementStatementNode
                        {
                            ExpressionStatement = new ExpressionStatementNode
                            {
                                Expression = MakeFieldAssignment(),
                            },
                        }),
                },
            },
            AccessorKind.ExpressionSet => new ExpressionSetAccessorAccessorDeclarationNode
            {
                Expression = MakeFieldAssignment(),
            },
            AccessorKind.AutoInit => new AutoInitAccessorAccessorDeclarationNode(),
            AccessorKind.BlockInit => new BlockInitAccessorAccessorDeclarationNode
            {
                Body = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new ExpressionStatementStatementNode
                        {
                            ExpressionStatement = new ExpressionStatementNode
                            {
                                Expression = MakeFieldAssignment(),
                            },
                        }),
                },
            },
            AccessorKind.ExpressionInit => new ExpressionInitAccessorAccessorDeclarationNode
            {
                Expression = MakeFieldAssignment(),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static CompilationUnitNode BuildAst(SetAccessorScenario scenario)
    {
        // Declare the receiver type as a class so it can be resolved
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.ReceiverTypeName),
            Members = ImmutableArray<MemberDeclarationNode>.Empty,
        };
        var classTypeDecl = new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl };

        // Build a block property with get + set/init accessor
        var accessors = ImmutableArray.Create(
            new AutoGetAccessorAccessorDeclarationNode(),
            MakeSetOrInitAccessor(scenario.Kind));

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray<ModifierNode>.Empty,
            Type = MakeNamedTypeRef(scenario.PropertyType),
            Name = Tok(scenario.PropertyName),
            Accessors = accessors,
        };

        var extMember = new ExtensionPropertyMemberExtensionMemberDeclarationNode
        {
            Property = property,
        };

        var extDecl = new ExtensionBlockDeclarationNode
        {
            ReceiverType = MakeNamedTypeRef(scenario.ReceiverTypeName),
            ReceiverName = Tok(scenario.ReceiverParamName),
            Members = ImmutableArray.Create<ExtensionMemberDeclarationNode>(extMember),
        };

        var extItem = new ExtensionItemTopLevelItemNode
        {
            ExtensionDeclaration = extDecl,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create(
                classTypeDecl.AsTopLevelItem(),
                extItem),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: Set Accessor Rejection
    //
    // **Validates: Requirements 6.5, 20.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any extension property that declares a `set` or `init` accessor,
    /// the compiler SHALL emit diagnostic `MIST_SB_0071`.
    ///
    /// **Validates: Requirements 6.5, 20.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(ExtensionSetAccessorRejectionPropertyTests) },
        MaxTest = 100)]
    public Property ExtensionProperty_WithSetOrInitAccessor_EmitsMIST_SB_0071(
        SetAccessorScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (_, diagnostics) = BuildModelWithDiagnostics(ast);

        var hasExpectedDiagnostic = diagnostics.Any(d =>
            d.Id.Value == "MIST_SB_0071" &&
            d.Severity == DiagnosticSeverity.Error);

        return hasExpectedDiagnostic.ToProperty()
            .Label($"Expected MIST_SB_0071 diagnostic for {scenario}. " +
                   $"Got diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}:{d.Message}"))}]");
    }
}