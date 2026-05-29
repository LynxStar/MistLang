// Feature: structured-concurrency, Property 12: Task-Void Lowers to Non-Generic Task
// **Validates: Requirements 10.6**

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests verifying that async methods with declared return type <c>void</c>
/// (effective Mist type <c>Task&lt;Void&gt;</c>) emit the non-generic <c>Task</c> in C#,
/// not <c>Task&lt;MistVoid&gt;</c> or <c>Task&lt;void&gt;</c>.
///
/// **Validates: Requirements 10.6**
///
/// For any async method with declared return type <c>void</c> where the task value is only
/// awaited for completion, the emitted C# return type SHALL be the non-generic
/// <c>System.Threading.Tasks.Task</c>.
///
/// All work runs in-process using compiler APIs — no external process is spawned.
/// Uses <see cref="CSharpEmitter.EmitToMemory"/> for content generation without disk I/O.
/// </summary>
public sealed class TaskVoidLoweringPropertyTests
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

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A scenario describing an async void method that is called (and its task awaited
    /// for completion only) from another method.
    /// </summary>
    public record TaskVoidLoweringScenario(
        string ClassName,
        string AsyncVoidMethodName,
        string CallerMethodName,
        bool IsStatic)
    {
        public override string ToString()
        {
            var staticStr = IsStatic ? "static " : "";
            return $"class {ClassName} {{ public {staticStr}async void {AsyncVoidMethodName}() {{}} " +
                   $"public async void {CallerMethodName}() {{ await {AsyncVoidMethodName}(); }} }}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Worker", "Service", "Handler", "Processor", "Manager"];

    private static readonly string[] AsyncVoidMethodNames =
        ["DoWork", "Process", "Execute", "Cleanup", "Initialize", "Notify", "Reset"];

    private static readonly string[] CallerMethodNames =
        ["RunAll", "Orchestrate", "Dispatch", "Coordinate", "Invoke"];

    public static Gen<TaskVoidLoweringScenario> GenTaskVoidLoweringScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from asyncVoidMethodName in Gen.Elements(AsyncVoidMethodNames)
               from callerMethodName in Gen.Elements(CallerMethodNames)
               from isStatic in Gen.Elements(true, false)
               where asyncVoidMethodName != callerMethodName
               select new TaskVoidLoweringScenario(
                   className, asyncVoidMethodName, callerMethodName, isStatic);
    }

    public static Arbitrary<TaskVoidLoweringScenario> ArbitraryTaskVoidLoweringScenario() =>
        GenTaskVoidLoweringScenario().ToArbitrary();

    // ─── AST construction ────────────────────────────────────────────────

    /// <summary>
    /// Builds an AST representing a class with:
    /// - An async void method (the target)
    /// - An async void caller method that awaits the target method
    ///
    /// This ensures the Task&lt;Void&gt; is only awaited for completion (not used as a value).
    /// </summary>
    private static CompilationUnitNode BuildAst(TaskVoidLoweringScenario scenario)
    {
        // Build the async void target method
        var targetModifiers = ImmutableArray.CreateBuilder<ModifierNode>();
        targetModifiers.Add(new PublicModifierModifierNode());
        if (scenario.IsStatic)
            targetModifiers.Add(new StaticModifierModifierNode());
        targetModifiers.Add(new AsyncModifierModifierNode());

        var targetMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.AsyncVoidMethodName),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode { Statements = ImmutableArray<StatementNode>.Empty },
            },
            Modifiers = targetModifiers.ToImmutable(),
        };

        // Build the caller method that awaits the target method.
        // The caller is async void and calls: await TargetMethod();
        var callPrimary = new IdentifierExpressionPrimaryNode { Name = Tok(scenario.AsyncVoidMethodName) };
        var invocationSuffix = new InvocationSuffixPostfixSuffixNode();
        var callPostfix = new PostfixNode
        {
            Target = callPrimary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(invocationSuffix),
        };
        var callUnary = new PostfixExpressionUnaryNode { Primary = callPostfix };

        // Wrap in await expression
        var awaitExpr = new AwaitExpressionUnaryNode { Operand = callUnary };

        var mult = new MultiplicativeNode { Left = awaitExpr };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var expr = new ExpressionNode { Assignment = assign };

        var awaitStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = expr },
        };

        var callerMethod = new MethodDeclarationNode
        {
            Name = Tok(scenario.CallerMethodName),
            ReturnType = new VoidTypeRefTypeReferenceNode(),
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(awaitStmt),
                },
            },
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

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new ClassTypeTypeDeclarationNode { ClassDeclaration = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static (bool Succeeded, string CsContent, string DiagnosticText) CompileAndEmit(
        TaskVoidLoweringScenario scenario)
    {
        var ast = BuildAst(scenario);
        var (model, buildDiagnostics) = StdlibTestHelper.BuildModelWithStdlib(ast);

        var buildErrors = buildDiagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (buildErrors.Count > 0)
        {
            var errorText = string.Join("\n", buildErrors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return (false, "", $"Build errors:\n{errorText}");
        }

        // Emit in-memory (no disk I/O)
        var emitBag = new DiagnosticBag();
        var files = CSharpEmitter.EmitToMemory(model, "TestProgram", emitBag);

        if (emitBag.HasErrors)
        {
            var emitErrors = emitBag.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"  {d.Id.Value}: {d.Message}");
            return (false, "", $"Emit errors:\n{string.Join("\n", emitErrors)}");
        }

        // Concatenate all emitted .cs content
        var sb = new StringBuilder();
        foreach (var (_, content) in files)
            sb.AppendLine(content);

        return (true, sb.ToString(), "");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Task-Void Lowers to Non-Generic Task
    //
    // **Validates: Requirements 10.6**
    //
    // For any async method with declared return type `void` (effective Mist type
    // `Task<Void>`) where the task value is only awaited for completion, the
    // emitted C# return type SHALL be the non-generic `Task`, not `Task<MistVoid>`.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TaskVoidLoweringPropertyTests) })]
    public Property AsyncVoidMethod_EmitsNonGenericTask(TaskVoidLoweringScenario scenario)
    {
        var (succeeded, csContent, diagnosticText) = CompileAndEmit(scenario);

        if (!succeeded)
        {
            return false.ToProperty()
                .Label($"Compilation failed for scenario: {scenario}\n{diagnosticText}");
        }

        if (string.IsNullOrWhiteSpace(csContent))
        {
            return false.ToProperty()
                .Label($"C# output was empty for scenario: {scenario}");
        }

        // The emitted C# MUST contain `async System.Threading.Tasks.Task` (non-generic)
        // for the async void methods.
        var containsNonGenericTask = csContent.Contains("async System.Threading.Tasks.Task ");

        if (!containsNonGenericTask)
        {
            // Also check for the pattern at end of line or before method name
            containsNonGenericTask = csContent.Contains("async System.Threading.Tasks.Task\n") ||
                                     csContent.Contains($"async System.Threading.Tasks.Task {scenario.AsyncVoidMethodName}") ||
                                     csContent.Contains($"async System.Threading.Tasks.Task {scenario.CallerMethodName}");
        }

        if (!containsNonGenericTask)
        {
            return false.ToProperty()
                .Label($"Missing non-generic 'async System.Threading.Tasks.Task' in emitted C#.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Emitted C#:\n{csContent}");
        }

        // The emitted C# MUST NOT contain `Task<MistVoid>` — that would be incorrect lowering
        if (csContent.Contains("Task<MistVoid>"))
        {
            return false.ToProperty()
                .Label($"Found forbidden 'Task<MistVoid>' in emitted C#.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Emitted C#:\n{csContent}");
        }

        // The emitted C# MUST NOT contain `Task<void>` — that's invalid C#
        if (csContent.Contains("Task<void>"))
        {
            return false.ToProperty()
                .Label($"Found forbidden 'Task<void>' in emitted C#.\n" +
                       $"Scenario: {scenario}\n" +
                       $"Emitted C#:\n{csContent}");
        }

        return true.ToProperty()
            .Label($"OK: {scenario}");
    }
}
