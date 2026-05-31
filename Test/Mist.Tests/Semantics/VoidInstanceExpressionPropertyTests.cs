// Feature: void-unit-type, Property 2: Void.Void expression type
// **Validates: Requirements 2.1, 2.2**

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
/// Property-based tests verifying that the expression `Void.Void` is assigned the void type
/// by the semantic model in any expression context.
///
/// Property 2: For any Mist program containing the expression `Void.Void` in any expression
/// context, the semantic model SHALL assign the void type to that expression.
/// </summary>
public class VoidInstanceExpressionPropertyTests
{
    private readonly SemanticModelBuilder _builder = new();

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

    private (ISemanticModel Model, ImmutableArray<Diagnostic> Diagnostics) BuildModel(
        Storm.Runtime.SyntaxNode ast,
        string source = "")
    {
        var compilation = MakeCompilation(source);
        var diagnostics = new DiagnosticBag();
        var model = _builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
        return (model, diagnostics.ToImmutableArray());
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static CompilationUnitNode MakeCompilationUnit(params TypeDeclarationNode[] typeDecls)
    {
        return new CompilationUnitNode { Items = typeDecls.ToImmutableArray().AsTopLevelItems() };
    }

    /// <summary>
    /// Creates the expression `Void.Void` as an AST: an IdentifierExpressionPrimaryNode("Void")
    /// with a MemberAccessSuffixPostfixSuffixNode("Void") suffix, wrapped through the full
    /// expression precedence chain.
    /// Returns both the full ExpressionNode and the PostfixNode (for type verification).
    /// </summary>
    private static (ExpressionNode Expression, PostfixNode Postfix, MemberAccessSuffixPostfixSuffixNode Suffix)
        MakeVoidDotVoidExpression()
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
        var expr = new ExpressionNode { Assignment = assign };
        return (expr, postfix, suffix);
    }

    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode> body)
    {
        return new MethodDeclarationNode
        {
            Name = MakeToken("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents the expression context where `Void.Void` appears.
    /// </summary>
    public enum VoidExprContext
    {
        /// <summary>`void x = Void.Void;` — typed variable initializer</summary>
        TypedVariableInitializer,

        /// <summary>`var x = Void.Void;` — inferred variable initializer</summary>
        VarVariableInitializer,

        /// <summary>`return Void.Void;` — return statement in a void method</summary>
        ReturnStatement,

        /// <summary>`method(Void.Void);` — method argument</summary>
        MethodArgument,

        /// <summary>`Void.Void;` — expression statement</summary>
        ExpressionStatement,
    }

    /// <summary>
    /// A scenario combining an expression context with random names.
    /// </summary>
    public record VoidExprScenario(
        VoidExprContext Context,
        string MethodName,
        string VarName,
        string ParamName)
    {
        public override string ToString() =>
            $"Void.Void in {Context} (method={MethodName}, var={VarName}, param={ParamName})";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] MethodNames = ["DoWork", "Process", "Execute", "Run", "Handle"];
    private static readonly string[] VarNames = ["x", "y", "result", "value", "temp"];
    private static readonly string[] ParamNames = ["arg", "input", "param", "data", "item"];

    public static Gen<VoidExprScenario> GenScenario()
    {
        return from context in Gen.Elements(
                   VoidExprContext.TypedVariableInitializer,
                   VoidExprContext.VarVariableInitializer,
                   VoidExprContext.ReturnStatement,
                   VoidExprContext.MethodArgument,
                   VoidExprContext.ExpressionStatement)
               from methodName in Gen.Elements(MethodNames)
               from varName in Gen.Elements(VarNames)
               from paramName in Gen.Elements(ParamNames)
               select new VoidExprScenario(context, methodName, varName, paramName);
    }

    public static Arbitrary<VoidExprScenario> ArbitraryScenario() =>
        GenScenario().ToArbitrary();

    // ─── Scenario builders ───────────────────────────────────────────────

    /// <summary>
    /// Builds a program with `void varName = Void.Void;` and verifies the Void.Void
    /// expression is assigned the void type.
    /// </summary>
    private bool VerifyTypedVariableInitializer(VoidExprScenario scenario)
    {
        var (voidExpr, postfix, suffix) = MakeVoidDotVoidExpression();

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Name = MakeToken(scenario.VarName),
                Initializer = voidExpr,
            },
        };

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(varDecl));
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        return VerifyVoidType(model, postfix, suffix);
    }

    /// <summary>
    /// Builds a program with `var varName = Void.Void;` and verifies the Void.Void
    /// expression is assigned the void type.
    /// </summary>
    private bool VerifyVarVariableInitializer(VoidExprScenario scenario)
    {
        var (voidExpr, postfix, suffix) = MakeVoidDotVoidExpression();

        var varDecl = new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode
            {
                Name = MakeToken(scenario.VarName),
                Initializer = voidExpr,
            },
        };

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(varDecl));
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        return VerifyVoidType(model, postfix, suffix);
    }

    /// <summary>
    /// Builds a program with `return Void.Void;` in a void method and verifies the
    /// Void.Void expression is assigned the void type.
    /// </summary>
    private bool VerifyReturnStatement(VoidExprScenario scenario)
    {
        var (voidExpr, postfix, suffix) = MakeVoidDotVoidExpression();

        var returnStmt = new ReturnStatementStatementNode
        {
            ReturnStatement = new ReturnStatementNode { Value = voidExpr },
        };

        var testMethod = new MethodDeclarationNode
        {
            Name = MakeToken(scenario.MethodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray.Create<StatementNode>(returnStmt) } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        var mainMethod = MakeMainMethod(ImmutableArray<StatementNode>.Empty);
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod },
                new MethodMemberMemberDeclarationNode { Method = testMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        return VerifyVoidType(model, postfix, suffix);
    }

    /// <summary>
    /// Builds a program with a method call `methodName(Void.Void)` where the method
    /// accepts a void parameter, and verifies the Void.Void expression is assigned the void type.
    /// </summary>
    private bool VerifyMethodArgument(VoidExprScenario scenario)
    {
        var (voidExpr, postfix, suffix) = MakeVoidDotVoidExpression();

        // Build a method that accepts a void parameter
        var paramList = new ParameterListNode
        {
            Parameter = new ParameterNode
            {
                Type = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
                Name = MakeToken(scenario.ParamName),
            },
        };

        var targetMethod = new MethodDeclarationNode
        {
            Name = MakeToken(scenario.MethodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = paramList,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
        };

        // Build a call: this.methodName(Void.Void) in Main
        var callPrimary = new IdentifierExpressionPrimaryNode
        {
            Name = MakeToken(scenario.MethodName),
        };
        var argList = new ArgumentListNode
        {
            Argument = new ValueArgumentArgumentNode { Expression = voidExpr },
        };
        var callSuffix = new InvocationSuffixPostfixSuffixNode
        {
            Arguments = argList,
        };
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(callSuffix),
        };
        var callUnary = new PostfixExpressionUnaryNode { Primary = callPostfix };
        var callMult = new MultiplicativeNode { Left = callUnary };
        var callAdd = new AdditiveNode { Left = callMult };
        var callRel = new RelationalComparisonRelationalNode { Left = callAdd };
        var callEq = new EqualityNode { Left = callRel };
        var callLogAnd = new LogicalAndNode { Left = callEq };
        var callLogOr = new LogicalOrNode { Left = callLogAnd };
        var callAssign = new AssignmentNode { Target = new VoidCoalescingNode { Left = callLogOr } };
        var callExpr = new ExpressionNode { Assignment = callAssign };

        var callStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = callExpr },
        };

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(callStmt));
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod },
                new MethodMemberMemberDeclarationNode { Method = targetMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        return VerifyVoidType(model, postfix, suffix);
    }

    /// <summary>
    /// Builds a program with `Void.Void;` as an expression statement and verifies the
    /// Void.Void expression is assigned the void type.
    /// </summary>
    private bool VerifyExpressionStatement(VoidExprScenario scenario)
    {
        var (voidExpr, postfix, suffix) = MakeVoidDotVoidExpression();

        var exprStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = voidExpr },
        };

        var mainMethod = MakeMainMethod(ImmutableArray.Create<StatementNode>(exprStmt));
        var classDecl = new ClassDeclarationNode
        {
            Name = MakeToken("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = mainMethod }),
        };

        var ast = MakeCompilationUnit(new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl });
        var (model, _) = BuildModel(ast);

        return VerifyVoidType(model, postfix, suffix);
    }

    /// <summary>
    /// Verifies that the semantic model assigns the void type to both the PostfixNode and
    /// the MemberAccessSuffixPostfixSuffixNode of a Void.Void expression.
    /// </summary>
    private static bool VerifyVoidType(
        ISemanticModel model,
        PostfixNode postfix,
        MemberAccessSuffixPostfixSuffixNode suffix)
    {
        // The suffix node should have the void type assigned
        var suffixType = model.GetTypeOf(suffix);
        if (suffixType is null || !suffixType.IsVoid())
            return false;

        // The overall postfix node should also have the void type assigned
        var postfixType = model.GetTypeOf(postfix);
        if (postfixType is null || !postfixType.IsVoid())
            return false;

        // Both should be identified as void
        return suffixType.IsVoid()
            && postfixType.IsVoid();
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any Mist program containing the expression `Void.Void` in any
    /// expression context, the semantic model SHALL assign the void type to that expression.
    ///
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(VoidInstanceExpressionPropertyTests) }, MaxTest = 100)]
    public bool VoidDotVoid_IsAssignedVoidType(VoidExprScenario scenario)
    {
        return scenario.Context switch
        {
            VoidExprContext.TypedVariableInitializer => VerifyTypedVariableInitializer(scenario),
            VoidExprContext.VarVariableInitializer => VerifyVarVariableInitializer(scenario),
            VoidExprContext.ReturnStatement => VerifyReturnStatement(scenario),
            VoidExprContext.MethodArgument => VerifyMethodArgument(scenario),
            VoidExprContext.ExpressionStatement => VerifyExpressionStatement(scenario),
            _ => false,
        };
    }
}
