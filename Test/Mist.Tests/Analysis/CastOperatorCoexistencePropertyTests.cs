// Feature: cast-operators, Property 9: Cast operators coexist with arithmetic operators
// **Validates: Requirements 14.4, 14.5**

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for cast operator and arithmetic operator coexistence.
///
/// Property 9: For any type that declares both cast operators (implicit and/or explicit)
/// and arithmetic/comparison operator overloads, the semantic model builder SHALL register
/// all operators without conflict, and the type checker SHALL validate each operator
/// independently without interference between cast operators and arithmetic operators.
///
/// **Validates: Requirements 14.4, 14.5**
/// </summary>
public class CastOperatorCoexistencePropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

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
    /// Describes a scenario where a class declares both a cast operator and an
    /// arithmetic operator. The test verifies both are registered without conflict.
    /// </summary>
#pragma warning disable CA1034
    public record CoexistenceScenario(
        string TypeName,
        string CastTargetTypeName,
        string CastKind,
        ArithmeticOp ArithOp)
    {
        public override string ToString()
        {
            return $"{TypeName} with {CastKind} operator {CastTargetTypeName} and operator{ArithOp.Symbol}";
        }
    }

    /// <summary>
    /// Represents an arithmetic operator kind with its symbol and AST node factory.
    /// </summary>
    public record ArithmeticOp(string Symbol, string EncodedName)
    {
        public override string ToString()
        {
            return Symbol;
        }
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Alpha", "Bravo", "Charlie", "Delta", "Echo"];

    private static readonly ArithmeticOp[] ArithOps =
    [
        new("+", "operator+"),
        new("-", "operator-"),
        new("*", "operator*"),
        new("/", "operator/"),
        new("==", "operator=="),
        new("!=", "operator!="),
        new("<", "operator<"),
        new(">", "operator>"),
    ];

    private static readonly string[] CastKinds = ["implicit", "explicit"];

    private static Gen<CoexistenceScenario> GenCoexistenceScenario()
    {
        return from typeName in Gen.Elements(TypeNames)
               from castTarget in Gen.Elements(TypeNames).Where(n => n != typeName)
               from castKind in Gen.Elements(CastKinds)
               from arithOp in Gen.Elements(ArithOps)
               select new CoexistenceScenario(typeName, castTarget, castKind, arithOp);
    }

    public static Arbitrary<CoexistenceScenario> ArbitraryCoexistenceScenario()
    {
        return GenCoexistenceScenario().ToArbitrary();
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
    /// Creates an operator symbol node for the given arithmetic operator.
    /// </summary>
    private static OperatorSymbolNode MakeOperatorSymbol(string symbol)
    {
        return symbol switch
        {
            "+" => new PlusOpOperatorSymbolNode(),
            "-" => new MinusOpOperatorSymbolNode(),
            "*" => new StarOpOperatorSymbolNode(),
            "/" => new SlashOpOperatorSymbolNode(),
            "==" => new EqualEqualOpOperatorSymbolNode(),
            "!=" => new NotEqualOpOperatorSymbolNode(),
            "<" => new LessThanOpOperatorSymbolNode(),
            ">" => new GreaterThanOpOperatorSymbolNode(),
            _ => new PlusOpOperatorSymbolNode(),
        };
    }

    /// <summary>
    /// Builds a cast operator declaration:
    /// <c>public static {kind} operator targetType(sourceType value) { return value; }</c>
    /// </summary>
    private static CastOperatorDeclarationNode MakeCastOperatorDecl(
        string castKind, string targetTypeName, string sourceTypeName)
    {
        return new CastOperatorDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
            CastKind = castKind == "implicit"
                ? new ImplicitCastCastOperatorKindNode()
                : new ExplicitCastCastOperatorKindNode(),
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
    /// Builds an arithmetic operator declaration:
    /// <c>public static ReturnType operator{symbol}(TypeName a, TypeName b) { return a; }</c>
    /// For comparison operators that return bool, the return type is Boolean.
    /// </summary>
    private static OperatorDeclarationNode MakeArithmeticOperatorDecl(
        string typeName, ArithmeticOp arithOp)
    {
        var isComparison = arithOp.Symbol is "==" or "!=" or "<" or ">";
        var returnTypeName = isComparison ? "Boolean" : typeName;

        return new OperatorDeclarationNode
        {
            ReturnType = MakeNamedTypeRef(returnTypeName),
            Symbol = MakeOperatorSymbol(arithOp.Symbol),
            Parameters = new ParameterListNode
            {
                Parameter = MakeParameter(typeName, "a"),
                Parameters = ImmutableArray.Create(MakeParameter(typeName, "b")),
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
                                Value = MakeIdentifierExpression("a"),
                            },
                        }),
                },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    /// <summary>
    /// Builds a class that declares both a cast operator and an arithmetic operator.
    /// Also declares the cast target type as a separate empty class.
    /// </summary>
    private static CompilationUnitNode BuildCoexistenceAst(CoexistenceScenario scenario)
    {
        // Build the cast operator member
        var castOpMember = new CastOperatorMemberMemberDeclarationNode
        {
            CastOperator = MakeCastOperatorDecl(
                scenario.CastKind, scenario.CastTargetTypeName, scenario.TypeName),
        };

        // Build the arithmetic operator member
        var arithOpMember = new OperatorMemberMemberDeclarationNode
        {
            Operator = MakeArithmeticOperatorDecl(scenario.TypeName, scenario.ArithOp),
        };

        // The type that declares both operators
        var typeDecl = new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = new ClassDeclarationNode
            {
                Name = Tok(scenario.TypeName),
                Members = ImmutableArray.Create<MemberDeclarationNode>(
                    castOpMember, arithOpMember),
            },
        };

        // The cast target type (empty class)
        var targetTypeDecl = new ClassTypeTypeDeclarationNode
        {
            ClassDeclaration = new ClassDeclarationNode
            {
                Name = Tok(scenario.CastTargetTypeName),
                Members = ImmutableArray<MemberDeclarationNode>.Empty,
            },
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                typeDecl, targetTypeDecl).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Cast operators coexist with arithmetic operators
    //
    // **Validates: Requirements 14.4, 14.5**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any type that declares both a cast operator (implicit or explicit) and an
    /// arithmetic/comparison operator, the semantic model builder SHALL register all
    /// operators as SymbolKind.Method symbols in the type's member scope without
    /// conflict, and the type checker SHALL validate each operator independently
    /// without interference. No diagnostic errors related to operator registration
    /// conflicts shall be emitted.
    ///
    /// **Validates: Requirements 14.4, 14.5**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorCoexistencePropertyTests) },
        MaxTest = 100)]
    public Property CastAndArithmeticOperators_Coexist_WithoutConflict(
        CoexistenceScenario scenario)
    {
        var ast = BuildCoexistenceAst(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // 1. Both operators should be registered in the type's member scope.
        //    The cast operator has name like "operator_implicit_TargetType" or
        //    "operator_explicit_TargetType". The arithmetic operator has name
        //    like "operator+", "operator-", etc.
        var expectedCastName = $"operator_{scenario.CastKind}_{scenario.CastTargetTypeName}";
        var expectedArithName = scenario.ArithOp.EncodedName;

        // Find the type symbol via AllSymbols and access its MemberScope.
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.TypeName && s.Kind == SymbolKind.Class);

        var castOpFound = false;
        var arithOpFound = false;

        if(typeSymbol?.DeclaredType is ClassType classType)
        {
            var members = classType.MemberScope.Members;
            castOpFound = members.Any(m =>
                m.Name == expectedCastName && m.Kind == SymbolKind.Method);
            arithOpFound = members.Any(m =>
                m.Name == expectedArithName && m.Kind == SymbolKind.Method);
        }

        // 2. No diagnostic errors should indicate conflicts between cast and
        //    arithmetic operators. We filter for diagnostics that would indicate
        //    registration conflicts or interference.
        var conflictDiags = diagnostics
            .Where(d =>
                d.Id.Value.Contains("SB_0043") || // duplicate cast operator
                d.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var allGood = castOpFound && arithOpFound && conflictDiags.Length == 0;

        return allGood
            .ToProperty()
            .Label($"Expected both operators registered without conflict for {scenario}. " +
                   $"Cast op '{expectedCastName}' found: {castOpFound}, " +
                   $"Arith op '{expectedArithName}' found: {arithOpFound}, " +
                   $"Conflict diags: {conflictDiags.Length}. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}