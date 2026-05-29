// Feature: pattern-matching, Property 7: Not-Pattern Variable Restriction
// **Validates: Requirements 7.2, 10.7**

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
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for not-pattern variable restriction.
///
/// **Validates: Requirements 7.2, 10.7**
///
/// Property 7: For any <c>not</c> pattern whose operand introduces a pattern
/// variable (via type pattern or var pattern), the type checker SHALL emit a
/// diagnostic with ID <c>MIST_TC_0057</c>.
/// </summary>
public class NotPatternVariableRestrictionPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();
    private readonly TypeCheckerAnalyzer _analyzer = new();

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

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        _analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Whether the not-pattern operand is a type pattern or a var pattern.
    /// Both introduce variables and should be rejected.
    /// </summary>
    public enum NotPatternOperandKind
    {
        TypePattern,
        VarPattern,
    }

    /// <summary>
    /// Represents a not-pattern scenario with a variable-binding operand.
    /// </summary>
    public record NotPatternScenario(
        string ScrutineeName,
        string BindingName,
        NotPatternOperandKind OperandKind)
    {
        public override string ToString() => OperandKind switch
        {
            NotPatternOperandKind.TypePattern =>
                $"if ({ScrutineeName} is not int {BindingName})",
            NotPatternOperandKind.VarPattern =>
                $"if ({ScrutineeName} is not var {BindingName})",
            _ => $"if ({ScrutineeName} is not ??? {BindingName})",
        };
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] BindingNames = ["a", "b", "c", "d", "result"];

    public static Arbitrary<NotPatternScenario> ArbitraryScenario()
    {
        var gen = from scrutName in Gen.Elements(ScrutineeNames)
                  from bindName in Gen.Elements(BindingNames)
                  from kind in Gen.Elements(
                      NotPatternOperandKind.TypePattern,
                      NotPatternOperandKind.VarPattern)
                  select new NotPatternScenario(scrutName, bindName, kind);
        return gen.ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

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

    private static CompilationUnitNode BuildProgram(NotPatternScenario scenario)
    {
        // Declare scrutinee as int
        var scrutineeDecl = MistAstGenerator.MakeIntVarDecl(scenario.ScrutineeName, "42");

        // Build the operand pattern (type or var) that introduces a variable
        PrimaryPatternNode operandPrimary = scenario.OperandKind switch
        {
            NotPatternOperandKind.TypePattern => new TypePatternPrimaryPatternNode
            {
                Type = new PrimitiveTypeRefTypeReferenceNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                },
                Name = Tok(scenario.BindingName),
            },
            NotPatternOperandKind.VarPattern => new VarPatternPrimaryPatternNode
            {
                Name = Tok(scenario.BindingName),
            },
            _ => new VarPatternPrimaryPatternNode { Name = Tok(scenario.BindingName) },
        };

        // Wrap in not pattern
        var notUnary = new NotPatternUnaryPatternNode { Operand = operandPrimary };
        var and = new AndPatternNode { Left = notUnary };
        var or = new OrPatternNode { Left = and };
        var pattern = new PatternNode { OrPattern = or };

        // Build is expression
        var scrutinee = MakeIdentifierAdditive(scenario.ScrutineeName);
        var isNode = new IsExpressionRelationalNode { Left = scrutinee, Pattern = pattern };
        var eq = new EqualityNode { Left = isNode };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var isExpr = new ExpressionNode { Assignment = assign };

        var ifStatement = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = isExpr,
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray<StatementNode>.Empty,
                },
            },
        };

        var statements = ImmutableArray.Create(
            scrutineeDecl,
            (StatementNode)ifStatement);

        return MistProgramGenerator.MakeProgram(statements);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Not-Pattern Variable Restriction
    //
    // **Validates: Requirements 7.2, 10.7**
    //
    // For any not pattern whose operand introduces a pattern variable
    // (via type pattern or var pattern), the type checker SHALL emit a
    // diagnostic with ID MIST_TC_0057.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(NotPatternVariableRestrictionPropertyTests) })]
    public Property NotPatternWithVariable_EmitsDiagnostic(NotPatternScenario scenario)
    {
        var ast = BuildProgram(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var hasNotVarDiag = diagnostics.Any(d =>
            d.Id.Value == "MIST_TC_0057");

        return hasNotVarDiag.ToProperty()
            .Label($"Expected MIST_TC_0057 diagnostic for not-pattern with variable binding " +
                   $"'{scenario.BindingName}' ({scenario.OperandKind}) " +
                   $"but got: [{string.Join(", ", diagnostics.Select(d => d.Id.Value))}]. " +
                   $"Scenario: {scenario}");
    }
}
