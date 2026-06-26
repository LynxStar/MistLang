// Feature: structured-concurrency — Example-based parsing tests for async/await
// **Validates: Requirements 12.4, 12.5, 12.6**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

/// <summary>
/// Example-based tests verifying that async modifier, async functor, and await
/// expressions parse correctly and produce the expected AST structure.
/// Uses the parse → pretty-print approach to verify structure.
/// No process spawning — all operations are in-process.
/// </summary>
public class AsyncParsingTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Text.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    private static string PrettyPrint(string source)
    {
        var cu = ParseOrFail(source);
        return MistPrettyPrinter.Print(cu, source);
    }

    // ─── AsyncModifier on method declarations ────────────────────────────

    [Fact]
    public void AsyncModifier_OnInstanceMethod_ParsesCorrectly()
    {
        const string source = "class C { public async int Compute() { return 1; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("public async int Compute()", printed);
    }

    [Fact]
    public void AsyncModifier_OnStaticMethod_ParsesCorrectly()
    {
        const string source = "class C { public static async int Compute() { return 1; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("public static async int Compute()", printed);
    }

    [Fact]
    public void AsyncModifier_OnVoidMethod_ParsesCorrectly()
    {
        const string source = "class C { public async void DoWork() { } }";

        var printed = PrettyPrint(source);

        Assert.Contains("public async void DoWork()", printed);
    }

    [Fact]
    public void AsyncModifier_ProducesAsyncModifierModifierNode()
    {
        const string source = "class C { public async int Compute() { return 1; } }";
        var cu = ParseOrFail(source);

        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var modifiers = methodMember.Method.Modifiers;

        Assert.Contains(modifiers, m => m is AsyncModifierModifierNode);
    }

    [Fact]
    public void AsyncModifier_RoundTripIsStable()
    {
        const string source = "class C { public async int Compute() { return 1; } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── AsyncExpression on invocations ──────────────────────────────────

    [Fact]
    public void AsyncExpression_OnMethodCall_ParsesCorrectly()
    {
        const string source = "class C { public void M() { var t = async foo(); } }";

        var printed = PrettyPrint(source);

        Assert.Contains("async foo()", printed);
    }

    [Fact]
    public void AsyncExpression_OnInstanceMethodCall_ParsesCorrectly()
    {
        const string source = "class C { public void M() { var t = async obj.Method(); } }";

        var printed = PrettyPrint(source);

        Assert.Contains("async obj.Method()", printed);
    }

    [Fact]
    public void AsyncExpression_ProducesAsyncExpressionUnaryNode()
    {
        const string source = "class C { public void M() { var t = async foo(); } }";
        var cu = ParseOrFail(source);

        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        // Navigate to the variable declaration's initializer expression
        var varDecl = block!.Statements[0] as VariableDeclarationStatementStatementNode;
        Assert.NotNull(varDecl);

        var inferred = varDecl!.VariableDeclaration as InferredVariableDeclarationVariableDeclarationStatementNode;
        Assert.NotNull(inferred);

        // The expression's assignment → target chain should contain an AsyncExpressionUnaryNode
        var assignment = inferred!.Initializer.AsAssignment();
        var unary = GetUnaryFromAssignment(assignment);
        Assert.IsType<AsyncExpressionUnaryNode>(unary);
    }

    [Fact]
    public void AsyncExpression_RoundTripIsStable()
    {
        const string source = "class C { public void M() { var t = async foo(); } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── AwaitExpression on expressions ──────────────────────────────────

    [Fact]
    public void AwaitExpression_OnIdentifier_ParsesCorrectly()
    {
        const string source = "class C { public void M() { var x = await task; } }";

        var printed = PrettyPrint(source);

        Assert.Contains("await task", printed);
    }

    [Fact]
    public void AwaitExpression_OnMethodCall_ParsesCorrectly()
    {
        const string source = "class C { public void M() { var x = await GetTask(); } }";

        var printed = PrettyPrint(source);

        Assert.Contains("await GetTask()", printed);
    }

    [Fact]
    public void AwaitExpression_ProducesAwaitExpressionUnaryNode()
    {
        const string source = "class C { public void M() { var x = await task; } }";
        var cu = ParseOrFail(source);

        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var varDecl = block!.Statements[0] as VariableDeclarationStatementStatementNode;
        Assert.NotNull(varDecl);

        var inferred = varDecl!.VariableDeclaration as InferredVariableDeclarationVariableDeclarationStatementNode;
        Assert.NotNull(inferred);

        var assignment = inferred!.Initializer.AsAssignment();
        var unary = GetUnaryFromAssignment(assignment);
        Assert.IsType<AwaitExpressionUnaryNode>(unary);
    }

    [Fact]
    public void AwaitExpression_RoundTripIsStable()
    {
        const string source = "class C { public void M() { var x = await task; } }";

        var ast1 = ParseOrFail(source);
        var printed1 = MistPrettyPrinter.Print(ast1, source);

        var ast2 = ParseOrFail(printed1);
        var printed2 = MistPrettyPrinter.Print(ast2, printed1);

        Assert.Equal(printed1, printed2);
    }

    // ─── Precedence tests ────────────────────────────────────────────────

    [Fact]
    public void Precedence_AsyncBindsTighterThanBinaryPlus()
    {
        // async foo() + 1 should parse as (async foo()) + 1
        const string source = "class C { public void M() { var x = async foo() + 1; } }";

        var printed = PrettyPrint(source);

        // The pretty-printer should show "async foo() + 1" where async binds to foo()
        // and the + is a binary operation at a higher level
        Assert.Contains("async foo() + 1", printed);
    }

    [Fact]
    public void Precedence_AwaitBindsTighterThanBinaryPlus()
    {
        // await x + y should parse as (await x) + y
        const string source = "class C { public void M() { var z = await x + y; } }";

        var printed = PrettyPrint(source);

        // The pretty-printer should show "await x + y" where await binds to x
        // and the + is a binary operation at a higher level
        Assert.Contains("await x + y", printed);
    }

    [Fact]
    public void Precedence_AsyncFoo_PlusBinaryOp_ParsesAsAsyncBindingToInvocation()
    {
        // Verify the AST structure: the additive node's left side contains the async expression
        const string source = "class C { public void M() { var x = async foo() + 1; } }";
        var cu = ParseOrFail(source);

        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var varDecl = block!.Statements[0] as VariableDeclarationStatementStatementNode;
        Assert.NotNull(varDecl);

        var inferred = varDecl!.VariableDeclaration as InferredVariableDeclarationVariableDeclarationStatementNode;
        Assert.NotNull(inferred);

        // Navigate through the expression tree:
        // Expression → Assignment → LogicalOr → LogicalAnd → Equality → Relational → Additive
        // The Additive should have a Left (containing async foo()) and a Right (containing 1)
        var assignment = inferred!.Initializer.AsAssignment();
        var additive = GetAdditiveFromAssignment(assignment);
        Assert.NotNull(additive);

        // The left side of the additive should contain the async expression
        var leftUnary = GetUnaryFromAdditiveLeft(additive!);
        Assert.IsType<AsyncExpressionUnaryNode>(leftUnary);

        // The right side should exist (the + 1 part)
        Assert.NotNull(additive.Right);
    }

    [Fact]
    public void Precedence_AwaitX_PlusBinaryOp_ParsesAsAwaitBindingToIdentifier()
    {
        // Verify the AST structure: the additive node's left side contains the await expression
        const string source = "class C { public void M() { var z = await x + y; } }";
        var cu = ParseOrFail(source);

        var typeDecl = Assert.IsType<ClassTypeTypeDeclarationNode>(cu.GetTypes()[0]);
        var methodMember = Assert.IsType<MethodMemberMemberDeclarationNode>(typeDecl.ClassDeclaration.Members[0]);
        var block = methodMember.Method.Body.GetBlock();
        Assert.NotNull(block);

        var varDecl = block!.Statements[0] as VariableDeclarationStatementStatementNode;
        Assert.NotNull(varDecl);

        var inferred = varDecl!.VariableDeclaration as InferredVariableDeclarationVariableDeclarationStatementNode;
        Assert.NotNull(inferred);

        var assignment = inferred!.Initializer.AsAssignment();
        var additive = GetAdditiveFromAssignment(assignment);
        Assert.NotNull(additive);

        // The left side of the additive should contain the await expression
        var leftUnary = GetUnaryFromAdditiveLeft(additive!);
        Assert.IsType<AwaitExpressionUnaryNode>(leftUnary);

        // The right side should exist (the + y part)
        Assert.NotNull(additive.Right);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates from an AssignmentNode down to the Unary node at the leaf.
    /// Follows: Assignment → LogicalOr → LogicalAnd → Equality → Relational → Additive → Multiplicative → Unary
    /// </summary>
    private static UnaryNode? GetUnaryFromAssignment(AssignmentNode assignment)
    {
        var additive = GetAdditiveFromAssignment(assignment);
        if(additive is null)
        {
            return null;
        }

        return GetUnaryFromAdditiveLeft(additive);
    }

    private static AdditiveNode? GetAdditiveFromAssignment(AssignmentNode assignment)
    {
        var logicalOr = assignment.Target.Left;
        var logicalAnd = logicalOr.Left;
        var equality = logicalAnd.Left;

        if(equality.Left is not RelationalComparisonRelationalNode relational)
        {
            return null;
        }

        return relational.Left;
    }

    /// <summary>
    /// Gets the Unary node from the left side of an AdditiveNode.
    /// Path: Additive.Left (MultiplicativeNode) → Multiplicative.Left (UnaryNode)
    /// </summary>
    private static UnaryNode? GetUnaryFromAdditiveLeft(AdditiveNode additive)
    {
        var multiplicative = additive.Left;
        return multiplicative.Left;
    }
}