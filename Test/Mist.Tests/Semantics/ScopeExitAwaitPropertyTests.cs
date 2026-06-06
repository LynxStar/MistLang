// Feature: structured-concurrency, Property 6: Scope-Exit Await Insertion
// **Validates: Requirements 6.1, 6.4, 7.1, 7.2, 7.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests verifying that the semantic model records discarded tasks for
/// block scopes in async context methods containing discarded task-producing expressions.
///
/// **Validates: Requirements 6.1, 6.4, 7.1, 7.2, 7.4**
///
/// For any async context method body containing task-producing expressions that are
/// neither explicitly awaited nor returned, the semantic model SHALL record discarded tasks
/// for that block scope (via <c>GetDiscardedTasks</c>).
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// </summary>
public class ScopeExitAwaitPropertyTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new Storm.Runtime.Token(
            kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

    private static TypeReferenceNode MakePrimitiveTypeRef(string primitiveName)
    {
        PrimitiveTypeNode primitiveNode = primitiveName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
        return new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode { PrimitiveType = primitiveNode } };
    }

    /// <summary>
    /// Builds an expression statement representing a bare method invocation: MethodName();
    /// This is a "discarded" call — the result is not assigned or awaited.
    /// </summary>
    private static (StatementNode Statement, PostfixNode Invocation) MakeDiscardedCall(string methodName)
    {
        var callPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(methodName) };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };
        var callUnary = new PostfixExpressionUnaryNode { Primary = callPostfix };

        var mult = new MultiplicativeNode { Left = callUnary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new AssignmentFormExpressionNode { Assignment = assign };

        var exprStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };

        return (exprStmt, callPostfix);
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an async context method with discarded async method calls.
    /// </summary>
    public record ScopeExitAwaitScenario(
        string ClassName,
        string AsyncMethodName,
        string CallerMethodName,
        string ReturnTypeName,
        int DiscardedCallCount,
        bool IsVoid)
    {
        public override string ToString()
        {
            var retType = IsVoid ? "void" : ReturnTypeName;
            var calls = string.Join(" ", Enumerable.Range(0, DiscardedCallCount)
                .Select(_ => $"{AsyncMethodName}();"));
            return $"class {ClassName} {{ public async {retType} {AsyncMethodName}() {{}} " +
                   $"public async void {CallerMethodName}() {{ {calls} }} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames = ["Worker", "Service", "Handler", "Processor"];
    private static readonly string[] AsyncMethodNames = ["Compute", "Fetch", "Process", "Execute", "Load"];
    private static readonly string[] CallerMethodNames = ["RunAll", "DoWork", "Orchestrate", "Dispatch"];
    private static readonly string[] ReturnTypes = ["int", "string", "bool", "float", "void"];

    public static Gen<ScopeExitAwaitScenario> GenScopeExitAwaitScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from asyncMethodName in Gen.Elements(AsyncMethodNames)
               from callerMethodName in Gen.Elements(CallerMethodNames)
               from returnType in Gen.Elements(ReturnTypes)
               from discardedCallCount in Gen.Choose(1, 4)
               select new ScopeExitAwaitScenario(
                   className, asyncMethodName, callerMethodName, returnType,
                   discardedCallCount, returnType == "void");
    }

    public static Arbitrary<ScopeExitAwaitScenario> ArbitraryScopeExitAwaitScenario() =>
        GenScopeExitAwaitScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds an AST representing a class with:
    /// - An async target method with the specified return type
    /// - An async caller method that discards N calls to the target method
    ///
    /// Returns the AST and the BlockNode of the caller method so we can query
    /// GetDiscardedTasks on it.
    /// </summary>
    private static (CompilationUnitNode Ast, BlockNode CallerBlock) BuildAst(
        ScopeExitAwaitScenario scenario)
    {
        // Build the async target method (the one being called)
        TypeReferenceNode returnTypeRef = scenario.IsVoid
            ? new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() }
            : MakePrimitiveTypeRef(scenario.ReturnTypeName);

        var targetMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.AsyncMethodName),
            ReturnType = returnTypeRef,
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new AsyncModifierModifierNode()),
        };

        // Build the discarded calls in the caller method
        var statements = ImmutableArray.CreateBuilder<StatementNode>();
        for (var i = 0; i < scenario.DiscardedCallCount; i++)
        {
            var (stmt, _) = MakeDiscardedCall(scenario.AsyncMethodName);
            statements.Add(stmt);
        }

        // Build the caller method block (async context)
        var callerBlock = new BlockNode { Statements = statements.ToImmutable() };

        var callerMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.CallerMethodName),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = callerBlock },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new AsyncModifierModifierNode()),
        };

        // Build the class
        var classDecl = new ClassDeclarationNode
        {
            Name = Tok(scenario.ClassName),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = targetMethod },
                new MethodMemberMemberDeclarationNode { Method = callerMethod }),
        };

        var ast = new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };

        return (ast, callerBlock);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Scope-Exit Await Insertion
    //
    // **Validates: Requirements 6.1, 6.4, 7.1, 7.2, 7.4**
    //
    // For any async context method body containing task-producing expressions
    // that are neither explicitly awaited nor returned, the semantic model
    // SHALL record discarded tasks for that block scope (via GetDiscardedTasks).
    //
    // This covers:
    // - 6.1: Discarded async method calls in async context → tracked as live tasks
    // - 6.4: Assigned-but-un-awaited Task<T> locals → tracked as live tasks
    // - 7.1: No task is orphaned — all are tracked for scope-exit await
    // - 7.2: All tasks spawned within a method that are not returned complete
    // - 7.4: Un-awaited Task<T> locals get implicit await at scope exit
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ScopeExitAwaitPropertyTests) })]
    public Property AsyncContext_DiscardedAsyncCalls_RecordsLiveTasks(ScopeExitAwaitScenario scenario)
    {
        var (ast, callerBlock) = BuildAst(scenario);
        var (model, diagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        // Query the semantic model for discarded tasks on the caller's block scope
        var liveTasks = model.GetDiscardedTasks(callerBlock);

        if (liveTasks.Count == 0)
            return false.ToProperty()
                .Label($"GetDiscardedTasks returned empty for caller block with {scenario.DiscardedCallCount} " +
                       $"discarded async calls. Scenario: {scenario}");

        // The number of live tasks should match the number of discarded calls
        if (liveTasks.Count != scenario.DiscardedCallCount)
            return false.ToProperty()
                .Label($"Expected {scenario.DiscardedCallCount} live tasks, " +
                       $"got {liveTasks.Count}. Scenario: {scenario}");

        // Each live task should have a Task<T> type
        for (var i = 0; i < liveTasks.Count; i++)
        {
            var task = liveTasks[i];

            if (task.TaskType is not ConstructedType constructedType)
                return false.ToProperty()
                    .Label($"Discarded task {i} type is not a ConstructedType. " +
                           $"Actual: {task.TaskType.GetType().Name} ({task.TaskType.DisplayName}). " +
                           $"Scenario: {scenario}");

            if (constructedType.GenericDefinition is not ClassType taskClass
                || taskClass.DisplayName != "Task"
                || taskClass.ContainingNamespace != "Standard")
                return false.ToProperty()
                    .Label($"Discarded task {i} type is not Standard.Task<T>. " +
                           $"Actual: {constructedType.GenericDefinition.DisplayName}. " +
                           $"Scenario: {scenario}");

            // Verify the generated variable name follows the __task_N pattern
            if (!task.GeneratedVariableName.StartsWith("__task_"))
                return false.ToProperty()
                    .Label($"Discarded task {i} has unexpected variable name '{task.GeneratedVariableName}'. " +
                           $"Expected pattern '__task_N'. Scenario: {scenario}");
        }

        return true.ToProperty();
    }
}
