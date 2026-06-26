// Feature: pattern-matching, Property 6: Or-Pattern Variable Consistency
// **Validates: Requirements 7.1, 10.6**

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
/// Property-based tests for or-pattern variable consistency.
///
/// **Validates: Requirements 7.1, 10.6**
///
/// Property 6: For any <c>or</c> pattern whose alternatives introduce different
/// sets of variable names (or the same names with different types), the type
/// checker SHALL emit a diagnostic with ID <c>MIST_TC_0056</c>.
/// </summary>
public class OrPatternVariableConsistencyPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(
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
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents an or-pattern scenario with inconsistent variable bindings.
    /// The left alternative binds <c>leftName</c> as <c>int</c>, the right
    /// alternative binds <c>rightName</c> as <c>string</c>. When the names
    /// differ, the variable sets are inconsistent.
    /// </summary>
    public record OrPatternScenario(
        string ScrutineeName,
        string LeftBindingName,
        string RightBindingName)
    {
        public override string ToString()
        {
            return $"if ({ScrutineeName} is int {LeftBindingName} or string {RightBindingName})";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] LeftNames = ["a", "b", "c"];
    private static readonly string[] RightNames = ["d", "e", "f"];

    public static Arbitrary<OrPatternScenario> ArbitraryScenario()
    {
        // Generate scenarios where left and right binding names are always different
        // to ensure inconsistent variable sets
        var gen = from scrutName in Gen.Elements(ScrutineeNames)
                  from leftName in Gen.Elements(LeftNames)
                  from rightName in Gen.Elements(RightNames)
                  select new OrPatternScenario(scrutName, leftName, rightName);
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

    private static CompilationUnitNode BuildProgram(OrPatternScenario scenario)
    {
        // Declare scrutinee as int
        var scrutineeDecl = MistAstGenerator.MakeIntVarDecl(scenario.ScrutineeName, "42");

        // Build or-pattern: int leftName or string rightName
        // Left alternative: int leftName
        var leftPrimary = new TypePatternPrimaryPatternNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = Tok(scenario.LeftBindingName),
        };
        var leftUnary = new DirectPatternUnaryPatternNode { Primary = leftPrimary };
        var leftAnd = new AndPatternNode { Left = leftUnary };

        // Right alternative: string rightName
        var rightPrimary = new TypePatternPrimaryPatternNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new StringTypePrimitiveTypeNode(),
                }
            },
            Name = Tok(scenario.RightBindingName),
        };
        var rightUnary = new DirectPatternUnaryPatternNode { Primary = rightPrimary };
        var rightAnd = new AndPatternNode { Left = rightUnary };

        // Or pattern combining both
        var or = new OrPatternNode { Left = leftAnd, Right = rightAnd };
        var pattern = new PatternNode { OrPattern = or };

        // Build is expression
        var scrutinee = MakeIdentifierAdditive(scenario.ScrutineeName);
        var isNode = new IsExpressionRelationalNode { Left = scrutinee, Pattern = pattern };
        var eq = new EqualityNode { Left = isNode };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var isExpr = new AssignmentFormExpressionNode { Assignment = assign };

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
    // Property 6: Or-Pattern Variable Consistency
    //
    // **Validates: Requirements 7.1, 10.6**
    //
    // For any or pattern whose alternatives introduce different sets of
    // variable names, the type checker SHALL emit a diagnostic with ID
    // MIST_TC_0056.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OrPatternVariableConsistencyPropertyTests) })]
    public Property InconsistentOrPattern_EmitsDiagnostic(OrPatternScenario scenario)
    {
        var ast = BuildProgram(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var hasInconsistentVarDiag = diagnostics.Any(d =>
            d.Id.Value == "MIST_TC_0056");

        return hasInconsistentVarDiag.ToProperty()
            .Label($"Expected MIST_TC_0056 diagnostic for inconsistent or-pattern variables " +
                   $"(left: '{scenario.LeftBindingName}', right: '{scenario.RightBindingName}') " +
                   $"but got: [{string.Join(", ", diagnostics.Select(d => d.Id.Value))}]. " +
                   $"Scenario: {scenario}");
    }
}