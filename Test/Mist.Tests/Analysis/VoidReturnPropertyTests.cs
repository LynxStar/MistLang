// Feature: void-unit-type, Property 3: Void methods accept missing return values
// **Validates: Requirements 3.2, 3.3, 3.5**

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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that void methods accept all valid return patterns.
///
/// Property 3: For any Mist method with a `void` return type, the type checker SHALL accept
/// the method body without error regardless of whether it contains `return;`,
/// `return Void.Void;`, or no return statement at all.
/// </summary>
public class VoidReturnPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token MakeToken(string lexeme, int start = 0)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(start, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates the expression `Void.Void` as an AST node.
    /// </summary>
    private static ExpressionNode MakeVoidDotVoidExpression()
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken("Void"),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = MakeToken("Void"),
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
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a program with a class containing a static void Main() entry point
    /// and an additional void method with the given name and body statements.
    /// </summary>
    private static CompilationUnitNode MakeProgramWithVoidMethod(
        string methodName,
        ImmutableArray<StatementNode> methodBody)
    {
        var mainMethod = new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var testMethod = new MethodDeclarationNode
        {
            Name = MakeToken(methodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode { Statements = methodBody } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod },
                new MethodMemberMemberDeclarationNode { Method = testMethod }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents the return pattern used in a void method body.
    /// </summary>
    public enum VoidReturnPattern
    {
        /// <summary>`return;` — bare return statement (Req 3.2)</summary>
        BareReturn,

        /// <summary>`return Void.Void;` — explicit void value return (Req 3.5)</summary>
        ReturnVoidDotVoid,

        /// <summary>No return statement at all (Req 3.3)</summary>
        NoReturn,
    }

    /// <summary>
    /// A scenario combining a return pattern with a random method name.
    /// </summary>
    public record VoidReturnScenario(
        VoidReturnPattern Pattern,
        string MethodName)
    {
        public override string ToString()
        {
            return $"void {MethodName}() with {Pattern}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Run", "Handle", "Init", "Cleanup", "Reset", "Notify", "Update"];

    public static Gen<VoidReturnScenario> GenScenario()
    {
        return from pattern in Gen.Elements(
                   VoidReturnPattern.BareReturn,
                   VoidReturnPattern.ReturnVoidDotVoid,
                   VoidReturnPattern.NoReturn)
               from methodName in Gen.Elements(MethodNames)
               select new VoidReturnScenario(pattern, methodName);
    }

    public static Arbitrary<VoidReturnScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── Scenario builders ───────────────────────────────────────────────

    /// <summary>
    /// Builds the method body statements for the given return pattern.
    /// </summary>
    private static ImmutableArray<StatementNode> BuildMethodBody(VoidReturnPattern pattern)
    {
        return pattern switch
        {
            VoidReturnPattern.BareReturn => ImmutableArray.Create<StatementNode>(
                new ReturnStatementStatementNode
                {
                    ReturnStatement = new ReturnStatementNode { Value = null },
                }),

            VoidReturnPattern.ReturnVoidDotVoid => ImmutableArray.Create<StatementNode>(
                new ReturnStatementStatementNode
                {
                    ReturnStatement = new ReturnStatementNode { Value = MakeVoidDotVoidExpression() },
                }),

            VoidReturnPattern.NoReturn => ImmutableArray<StatementNode>.Empty,

            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3: For any Mist method with a `void` return type, the type checker SHALL
    /// accept the method body without error regardless of whether it contains `return;`,
    /// `return Void.Void;`, or no return statement at all.
    ///
    /// **Validates: Requirements 3.2, 3.3, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(VoidReturnPropertyTests) }, MaxTest = 100)]
    public bool VoidMethod_AcceptsAllValidReturnPatterns(VoidReturnScenario scenario)
    {
        var methodBody = BuildMethodBody(scenario.Pattern);
        var ast = MakeProgramWithVoidMethod(scenario.MethodName, methodBody);

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return errors.Count == 0;
    }
}