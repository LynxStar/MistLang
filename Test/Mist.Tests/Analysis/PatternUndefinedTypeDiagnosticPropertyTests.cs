// Feature: pattern-matching, Property 5: Undefined Type in Pattern Produces Diagnostic
// **Validates: Requirements 6.2, 10.1**

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
/// Property-based tests for undefined type diagnostic in patterns.
///
/// **Validates: Requirements 6.2, 10.1**
///
/// Property 5: For any type pattern that references a type name not defined in
/// any reachable scope, the type checker SHALL emit a diagnostic with ID
/// <c>MIST_TC_0050</c>.
/// </summary>
public class PatternUndefinedTypeDiagnosticPropertyTests
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
    /// Represents a scenario with a type pattern referencing a non-existent type.
    /// </summary>
    public record UndefinedTypeScenario(
        string ScrutineeName,
        string UndefinedTypeName,
        string BindingName)
    {
        public override string ToString()
        {
            return $"if ({ScrutineeName} is {UndefinedTypeName} {BindingName})";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] BindingNames = ["a", "b", "c", "d", "result"];
    // These type names are guaranteed to not exist in the default scope
    private static readonly string[] FakeTypeNames =
        ["Foo", "Bar", "Baz", "Widget", "Gadget", "Qux", "Zork", "Plugh"];

    public static Arbitrary<UndefinedTypeScenario> ArbitraryScenario()
    {
        var gen = from scrutName in Gen.Elements(ScrutineeNames)
                  from fakeType in Gen.Elements(FakeTypeNames)
                  from bindName in Gen.Elements(BindingNames)
                  select new UndefinedTypeScenario(scrutName, fakeType, bindName);
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

    private static CompilationUnitNode BuildProgram(UndefinedTypeScenario scenario)
    {
        // Declare scrutinee as int
        var scrutineeDecl = MistAstGenerator.MakeIntVarDecl(scenario.ScrutineeName, "42");

        // Build type pattern with a non-existent named type
        var typePattern = new TypePatternPrimaryPatternNode
        {
            Type = new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode
                    {
                        IDENTIFIER = Tok(scenario.UndefinedTypeName),
                    },
                }
            },
            Name = Tok(scenario.BindingName),
        };

        var unary = new DirectPatternUnaryPatternNode { Primary = typePattern };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
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
    // Property 5: Undefined Type in Pattern Produces Diagnostic
    //
    // **Validates: Requirements 6.2, 10.1**
    //
    // For any type pattern that references a type name not defined in any
    // reachable scope, the type checker SHALL emit a diagnostic with ID
    // MIST_TC_0050.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PatternUndefinedTypeDiagnosticPropertyTests) })]
    public Property UndefinedTypePattern_EmitsDiagnostic(UndefinedTypeScenario scenario)
    {
        var ast = BuildProgram(scenario);
        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        // The type checker should emit MIST_TC_0050 for the undefined type
        var hasUndefinedTypeDiag = diagnostics.Any(d =>
            d.Id.Value == "MIST_TC_0050");

        return hasUndefinedTypeDiag.ToProperty()
            .Label($"Expected MIST_TC_0050 diagnostic for undefined type '{scenario.UndefinedTypeName}' " +
                   $"but got: [{string.Join(", ", diagnostics.Select(d => d.Id.Value))}]. " +
                   $"Scenario: {scenario}");
    }
}