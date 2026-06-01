// Feature: pattern-matching, Property 2: Pattern Variable Scoping in Then Branch
// **Validates: Requirements 5.1, 5.2, 5.3**

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
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for pattern variable scoping in then-branch.
///
/// **Validates: Requirements 5.1, 5.2, 5.3**
///
/// Property 2: For any <c>if</c> statement whose condition contains an <c>is</c>
/// expression with a type pattern or var pattern that introduces a variable binding,
/// the pattern variable SHALL be resolvable by name in the then-branch scope and
/// SHALL have <c>SymbolKind.LocalVariable</c> with the correct type (the pattern
/// type for type patterns, the scrutinee type for var patterns).
/// </summary>
public class PatternVariableScopingPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return _builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a pattern variable scoping scenario: a scrutinee type,
    /// a pattern kind (type or var), and the binding variable name.
    /// </summary>
    public record PatternVarScopeScenario(
        string ScrutineeType,
        string ScrutineeName,
        string BindingName,
        bool IsTypePattern)
    {
        public override string ToString() =>
            IsTypePattern
                ? $"if ({ScrutineeName} is {ScrutineeType} {BindingName})"
                : $"if ({ScrutineeName} is var {BindingName})";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] BindingNames = ["a", "b", "c", "d", "result"];
    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    public static Arbitrary<PatternVarScopeScenario> ArbitraryScenario()
    {
        var gen = from scrutType in Gen.Elements(PrimitiveTypes)
                  from scrutName in Gen.Elements(ScrutineeNames)
                  from bindName in Gen.Elements(BindingNames)
                  from isTypePattern in Gen.Elements(true, false)
                  select new PatternVarScopeScenario(scrutType, scrutName, bindName, isTypePattern);
        return gen.ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    private static PrimitiveTypeNode MakePrimitiveType(string typeName) => typeName switch
    {
        "int" => new IntTypePrimitiveTypeNode(),
        "string" => new StringTypePrimitiveTypeNode(),
        "bool" => new BoolTypePrimitiveTypeNode(),
        _ => new IntTypePrimitiveTypeNode(),
    };

    private static StatementNode MakeScrutineeDecl(string typeName, string name)
    {
        ExpressionNode initializer = typeName switch
        {
            "int" => MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("42") }),
            "string" => MistAstGenerator.WrapPrimary(
                new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"hello\"") }),
            "bool" => MistAstGenerator.WrapPrimary(
                new TrueLiteralExpressionPrimaryNode()),
            _ => MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") }),
        };

        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = MakePrimitiveType(typeName),
                } },
                Name = Tok(name),
                Initializer = initializer,
            },
        };
    }

    private static PatternNode MakeTypePattern(string typeName, string bindingName)
    {
        var primary = new TypePatternPrimaryPatternNode
        {
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = MakePrimitiveType(typeName),
            } },
            Name = Tok(bindingName),
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakeVarPattern(string bindingName)
    {
        var primary = new VarPatternPrimaryPatternNode
        {
            Name = Tok(bindingName),
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static AdditiveNode MakeIdentifierAdditive(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        return new AdditiveNode { Left = mult };
    }

    private static ExpressionNode MakeIsExpression(string scrutineeName, PatternNode pattern)
    {
        var scrutinee = MakeIdentifierAdditive(scrutineeName);
        var rel = new IsExpressionRelationalNode { Left = scrutinee, Pattern = pattern };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static CompilationUnitNode BuildProgram(PatternVarScopeScenario scenario)
    {
        var scrutineeDecl = MakeScrutineeDecl(scenario.ScrutineeType, scenario.ScrutineeName);

        var pattern = scenario.IsTypePattern
            ? MakeTypePattern(scenario.ScrutineeType, scenario.BindingName)
            : MakeVarPattern(scenario.BindingName);

        var isExpr = MakeIsExpression(scenario.ScrutineeName, pattern);

        // Use the binding variable in the then-branch to ensure it's resolved
        var useBinding = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode
            {
                Expression = MistAstGenerator.MakeIdentifierExpr(scenario.BindingName),
            },
        };

        var ifStatement = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = isExpr,
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(useBinding),
                },
            },
        };

        var statements = ImmutableArray.Create(
            scrutineeDecl,
            (StatementNode)ifStatement);

        return MistProgramGenerator.MakeProgram(statements);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Pattern Variable Scoping in Then Branch
    //
    // **Validates: Requirements 5.1, 5.2, 5.3**
    //
    // For any if statement whose condition contains an is expression with
    // a type pattern or var pattern that introduces a variable binding,
    // the pattern variable SHALL be resolvable by name in the then-branch
    // scope and SHALL have SymbolKind.LocalVariable with the correct type.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PatternVariableScopingPropertyTests) })]
    public Property PatternVariable_IsInThenBranchScope_WithCorrectKindAndType(
        PatternVarScopeScenario scenario)
    {
        var ast = BuildProgram(scenario);
        var model = BuildModel(ast);

        // Find the pattern variable symbol by name
        var patternVar = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.BindingName
                                 && s.Kind == SymbolKind.LocalVariable);

        if (patternVar is null)
        {
            return false.ToProperty()
                .Label($"Pattern variable '{scenario.BindingName}' not found as LocalVariable in model. " +
                       $"Scenario: {scenario}");
        }

        // Verify the type is correct
        var declaredType = patternVar.DeclaredType;
        if (declaredType is null)
        {
            return false.ToProperty()
                .Label($"Pattern variable '{scenario.BindingName}' has null DeclaredType. " +
                       $"Scenario: {scenario}");
        }

        // For type patterns, the variable type should match the pattern type.
        // For var patterns, the variable type should match the scrutinee type.
        // In both cases here, the type should correspond to the scrutinee's primitive type.
        var expectedTypeName = scenario.ScrutineeType switch
        {
            "int" => "Int32",
            "string" => "String",
            "bool" => "Boolean",
            _ => scenario.ScrutineeType,
        };

        var typeNameMatches = declaredType.DisplayName == expectedTypeName
                              || declaredType.DisplayName.Equals(scenario.ScrutineeType, StringComparison.OrdinalIgnoreCase);

        return typeNameMatches.ToProperty()
            .Label($"Expected type containing '{expectedTypeName}' but got '{declaredType.DisplayName}'. " +
                   $"Scenario: {scenario}");
    }
}
