// Feature: pattern-matching, Property 8: Emission Structural Correctness
// **Validates: Requirements 8.1–8.8, 9.1–9.13**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for pattern emission structural correctness.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8,
///              9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 9.9, 9.10,
///              9.11, 9.12, 9.13**
///
/// Property 8: For any valid Mist program containing <c>is</c> expressions with
/// patterns, the emitted C# source text SHALL contain the corresponding C# pattern
/// syntax (e.g., <c>is Type name</c> for type patterns, <c>is [1, 2, ..]</c> for
/// list patterns, <c>is &gt; 0 and &lt; 100</c> for logical/relational patterns).
/// No process spawning — structural string verification only.
/// </summary>
public sealed class PatternEmissionPropertyTests : IDisposable
{
    private readonly SemanticModelBuilder builder = new();
    private readonly string outputDir;

    public PatternEmissionPropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistPatternEmissionTests_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

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

    private string EmitAndGetCsContent(ISemanticModel model)
    {
        var diagnostics = new DiagnosticBag();
        CSharpEmitter.Emit(model, outputDir, "TestProject", diagnostics);

        var csFile = Directory.GetFiles(outputDir, "*.cs")
            .FirstOrDefault(f => !f.EndsWith("MistVoid.g.cs", StringComparison.Ordinal));
        return csFile is not null ? File.ReadAllText(csFile) : "";
    }

    private void CleanOutputDir()
    {
        if(Directory.Exists(outputDir))
        {
            foreach(var file in Directory.GetFiles(outputDir))
            {
                File.Delete(file);
            }
        }
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// The kind of pattern to test emission for.
    /// </summary>
    public enum EmissionPatternKind
    {
        TypePattern,
        VarPattern,
        DiscardPattern,
        ConstantIntPattern,
        ConstantBoolPattern,
        RelationalGreaterThan,
        RelationalLessThan,
    }

    /// <summary>
    /// Represents a pattern emission scenario.
    /// </summary>
    public record EmissionScenario(
        EmissionPatternKind Kind,
        string ScrutineeName,
        int IntValue)
    {
        public override string ToString() =>
            $"{ScrutineeName} is {Kind} (intVal={IntValue})";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];

    public static Arbitrary<EmissionScenario> ArbitraryScenario()
    {
        var gen = from kind in Gen.Elements(
                      EmissionPatternKind.TypePattern,
                      EmissionPatternKind.VarPattern,
                      EmissionPatternKind.DiscardPattern,
                      EmissionPatternKind.ConstantIntPattern,
                      EmissionPatternKind.ConstantBoolPattern,
                      EmissionPatternKind.RelationalGreaterThan,
                      EmissionPatternKind.RelationalLessThan)
                  from name in Gen.Elements(ScrutineeNames)
                  from intVal in Gen.Choose(0, 99)
                  select new EmissionScenario(kind, name, intVal);
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

    private static PatternNode MakePattern(EmissionPatternKind kind, int intValue)
    {
        PrimaryPatternNode primary = kind switch
        {
            EmissionPatternKind.TypePattern => new TypePatternPrimaryPatternNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = Tok("matched"),
            },
            EmissionPatternKind.VarPattern => new VarPatternPrimaryPatternNode
            {
                Name = Tok("matched"),
            },
            EmissionPatternKind.DiscardPattern => new VarPatternPrimaryPatternNode
            {
                Name = Tok("_"),
            },
            EmissionPatternKind.ConstantIntPattern => new ConstantPatternPrimaryPatternNode
            {
                Literal = new IntegerConstantConstantLiteralNode
                {
                    Value = Tok(intValue.ToString()),
                },
            },
            EmissionPatternKind.ConstantBoolPattern => new ConstantPatternPrimaryPatternNode
            {
                Literal = new TrueConstantConstantLiteralNode(),
            },
            EmissionPatternKind.RelationalGreaterThan => new GreaterThanRelationalPatternPrimaryPatternNode
            {
                Constant = MistAstGenerator.WrapPrimary(
                    new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(intValue.ToString()) }).AsAssignment().Target,
            },
            EmissionPatternKind.RelationalLessThan => new LessThanRelationalPatternPrimaryPatternNode
            {
                Constant = MistAstGenerator.WrapPrimary(
                    new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok(intValue.ToString()) }).AsAssignment().Target,
            },
            _ => new VarPatternPrimaryPatternNode { Name = Tok("_") },
        };

        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static string GetExpectedSubstring(EmissionPatternKind kind, int intValue) => kind switch
    {
        EmissionPatternKind.TypePattern => "is int matched",
        EmissionPatternKind.VarPattern => "is var matched",
        EmissionPatternKind.DiscardPattern => "is var _",
        EmissionPatternKind.ConstantIntPattern => $"is {intValue}",
        EmissionPatternKind.ConstantBoolPattern => "is true",
        EmissionPatternKind.RelationalGreaterThan => $"is > {intValue}",
        EmissionPatternKind.RelationalLessThan => $"is < {intValue}",
        _ => "is",
    };

    private static CompilationUnitNode BuildProgram(EmissionScenario scenario)
    {
        // Declare scrutinee as int (works for all pattern kinds in this test)
        var scrutineeDecl = MistAstGenerator.MakeIntVarDecl(scenario.ScrutineeName, "42");

        var pattern = MakePattern(scenario.Kind, scenario.IntValue);

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
    // Property 8: Emission Structural Correctness
    //
    // **Validates: Requirements 8.1–8.8, 9.1–9.13**
    //
    // For any valid Mist program containing is expressions with patterns,
    // the emitted C# source text SHALL contain the corresponding C# pattern
    // syntax. No process spawning — structural string verification only.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PatternEmissionPropertyTests) })]
    public Property EmittedCSharp_ContainsExpectedPatternSyntax(EmissionScenario scenario)
    {
        CleanOutputDir();

        var ast = BuildProgram(scenario);
        var model = BuildModel(ast);
        var csContent = EmitAndGetCsContent(model);

        if(string.IsNullOrEmpty(csContent))
        {
            return false.ToProperty()
                .Label($"Emitted C# content was empty. Scenario: {scenario}");
        }

        // The emitted C# should contain "is" keyword (basic structural check)
        var containsIs = csContent.Contains(" is ");

        // The emitted C# should contain the expected pattern-specific substring
        var expectedSubstring = GetExpectedSubstring(scenario.Kind, scenario.IntValue);
        var containsExpected = csContent.Contains(expectedSubstring);

        return (containsIs && containsExpected).ToProperty()
            .Label($"Expected emitted C# to contain '{expectedSubstring}' and ' is '. " +
                   $"Actual content:\n{csContent}\n" +
                   $"Scenario: {scenario}");
    }
}