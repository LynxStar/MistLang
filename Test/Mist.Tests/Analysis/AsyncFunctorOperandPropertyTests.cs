// Feature: structured-concurrency, Property 9: Async Functor Requires Invocation Operand
// **Validates: Requirements 3.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that MIST_TC_0107 is emitted when the <c>async</c>
/// functor is applied to an expression that is not a method invocation.
///
/// **Validates: Requirements 3.7**
///
/// For any use of the <c>async</c> functor where the operand is not a method invocation
/// expression (e.g., a variable reference, literal, or property access), the type checker
/// SHALL emit diagnostic <c>MIST_TC_0107</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class AsyncFunctorOperandPropertyTests
{
    private static readonly TypeCheckerAnalyzer Analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private static UsingDeclarationNode MakeUsingDeclaration(string namespaceName)
    {
        return new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(namespaceName) },
        };
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// The kind of non-invocation operand applied to the <c>async</c> functor.
    /// </summary>
    public enum NonInvocationKind
    {
        /// <summary>A variable reference: <c>async x</c></summary>
        VariableReference,

        /// <summary>An integer literal: <c>async 42</c></summary>
        IntLiteral,

        /// <summary>A string literal: <c>async "hello"</c></summary>
        StringLiteral,

        /// <summary>A boolean literal: <c>async true</c></summary>
        BoolLiteral,

        /// <summary>A property access: <c>async this.Value</c></summary>
        PropertyAccess,
    }

    /// <summary>
    /// A scenario describing an <c>async</c> functor applied to a non-invocation operand.
    /// </summary>
    public record AsyncFunctorOperandScenario(
        string ClassName,
        string MethodName,
        NonInvocationKind OperandKind,
        string VarName)
    {
        public override string ToString()
        {
            var operandDesc = OperandKind switch
            {
                NonInvocationKind.VariableReference => $"async {VarName}",
                NonInvocationKind.IntLiteral => "async 42",
                NonInvocationKind.StringLiteral => "async \"hello\"",
                NonInvocationKind.BoolLiteral => "async true",
                NonInvocationKind.PropertyAccess => "async this.Value",
                _ => "async ???",
            };
            return $"class {ClassName} {{ void {MethodName}() {{ {operandDesc}; }} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Worker", "Service", "Handler", "Processor", "Manager"];
    private static readonly string[] MethodNames = ["Run", "Execute", "Process", "Handle", "DoWork"];
    private static readonly string[] VarNames = ["x", "y", "z", "value", "data", "result"];

    public static Gen<AsyncFunctorOperandScenario> GenScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from methodName in Gen.Elements(MethodNames)
               from operandKind in Gen.Elements(
                   NonInvocationKind.VariableReference,
                   NonInvocationKind.IntLiteral,
                   NonInvocationKind.StringLiteral,
                   NonInvocationKind.BoolLiteral,
                   NonInvocationKind.PropertyAccess)
               from varName in Gen.Elements(VarNames)
               select new AsyncFunctorOperandScenario(className, methodName, operandKind, varName);
    }

    public static Arbitrary<AsyncFunctorOperandScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds the non-invocation operand as a <see cref="UnaryNode"/> based on the scenario kind.
    /// </summary>
    private static UnaryNode BuildNonInvocationOperand(AsyncFunctorOperandScenario scenario)
    {
        PrimaryNode primary = scenario.OperandKind switch
        {
            NonInvocationKind.VariableReference =>
                new IdentifierExpressionPrimaryNode { Name = Tok(scenario.VarName) },

            NonInvocationKind.IntLiteral =>
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("42") },

            NonInvocationKind.StringLiteral =>
                new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"hello\"") },

            NonInvocationKind.BoolLiteral =>
                new TrueLiteralExpressionPrimaryNode(),

            NonInvocationKind.PropertyAccess =>
                // this.Value — a ThisExpression with a MemberAccessSuffix (no invocation)
                new ThisExpressionPrimaryNode(),

            _ => throw new ArgumentOutOfRangeException(),
        };

        // For property access, add a member access suffix (this.Value)
        var suffix = scenario.OperandKind == NonInvocationKind.PropertyAccess
            ? ImmutableArray.Create<PostfixSuffixNode>(
                new MemberAccessSuffixPostfixSuffixNode { Name = Tok("Value") })
            : ImmutableArray<PostfixSuffixNode>.Empty;

        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = suffix,
        };

        return new PostfixExpressionUnaryNode { Primary = postfix };
    }

    /// <summary>
    /// Builds a complete AST representing a class with a method that uses <c>async</c>
    /// on a non-invocation operand.
    /// </summary>
    private static CompilationUnitNode BuildAst(AsyncFunctorOperandScenario scenario)
    {
        // Build the async expression with a non-invocation operand
        var operand = BuildNonInvocationOperand(scenario);
        var asyncExprNode = new AsyncExpressionUnaryNode { Operand = operand };

        // Wrap in expression chain: var t = async <operand>;
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = asyncExprNode } };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        // Build statements for the method body
        var statements = ImmutableArray.CreateBuilder<StatementNode>();

        // For variable reference scenarios, declare the variable first
        if(scenario.OperandKind == NonInvocationKind.VariableReference)
        {
            statements.Add(MistAstGenerator.MakeIntVarDecl(scenario.VarName, "42"));
        }

        // For property access, add a Value field to the class (handled below)

        // var t = async <operand>;
        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = Tok("t"),
                Initializer = expr,
            },
        };
        statements.Add(varDecl);

        // Build the method containing the async expression
        var method = new MethodDeclarationNode
        {
            Name = Tok(scenario.MethodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode { Statements = statements.ToImmutable() },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Build class members
        var members = ImmutableArray.CreateBuilder<MemberDeclarationNode>();

        // For property access, add a Value property to the class
        if(scenario.OperandKind == NonInvocationKind.PropertyAccess)
        {
            var valueField = new FieldDeclarationNode
            {
                Name = Tok("Value"),
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            };
            members.Add(new FieldMemberMemberDeclarationNode { Field = valueField });
        }

        members.Add(new MethodMemberMemberDeclarationNode { Method = method });

        // Build the Program class with Main entry point
        var mainMethod = new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new CallableBlockBodyCallableBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };

        var programClass = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok(scenario.ClassName),
            Members = members.ToImmutable(),
        };

        return new CompilationUnitNode
        {
            Usings = ImmutableArray.Create(MakeUsingDeclaration("Standard")),
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl },
                new BodyTypeTypeDeclarationNode { TypeBody = programClass }).AsTopLevelItems(),
        };
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 9: For any use of the <c>async</c> functor where the operand is not a method
    /// invocation expression, the type checker SHALL emit diagnostic <c>MIST_TC_0107</c>.
    ///
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AsyncFunctorOperandPropertyTests) })]
    public Property AsyncFunctor_NonInvocationOperand_EmitsMIST_TC_0107(
        AsyncFunctorOperandScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiag) = StdlibTestHelper.BuildModelWithStdlib(ast);

        var tcDiag = new DiagnosticBag();
        Analyzer.Analyze(model, tcDiag);

        var allDiagnostics = new List<Diagnostic>();
        allDiagnostics.AddRange(buildDiag);
        allDiagnostics.AddRange(tcDiag.ToImmutableArray());

        var matchingDiagnostics = allDiagnostics
            .Where(d => d.Id.Value == "MIST_TC_0107")
            .ToList();

        if(matchingDiagnostics.Count == 0)
        {
            return false.ToProperty()
                .Label($"Expected MIST_TC_0107 but no such diagnostic was emitted. " +
                       $"All diagnostics: [{string.Join(", ", allDiagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]. " +
                       $"Scenario: {scenario}");
        }

        // Verify the diagnostic message mentions relevant context
        var diag = matchingDiagnostics[0];
        var mentionsAsync = diag.Message.Contains("async");
        var mentionsInvocation = diag.Message.Contains("method invocation") ||
                                 diag.Message.Contains("invocation");

        if(!mentionsAsync || !mentionsInvocation)
        {
            return false.ToProperty()
                .Label($"MIST_TC_0107 emitted but message doesn't mention 'async' and 'invocation'. " +
                       $"Message: '{diag.Message}'. Scenario: {scenario}");
        }

        return true.ToProperty();
    }
}