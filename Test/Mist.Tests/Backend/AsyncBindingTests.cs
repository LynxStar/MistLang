// Tier 3 (Component): what `async` does to a signature, and what a call to a task-returning member
// means in each kind of context. `await task` is sugar for `task.Drop()` (drop-semantics Req 7.5), so
// the three spellings -- written await, written Drop, and the drop a sync context inserts -- bind to
// ONE node, the call to Standard's Drop, and the backend alone decides that the call is spelled
// `await`. Built over the REAL Standard sources because every type here is Standard's Task. No Roslyn
// and no process spawning.

using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class AsyncBindingTests
{
    private const string Fixtures = """
        public class Helpers
        {
            public static int ComputeSync() { return 1; }

            public static async int ComputeAsync() { return 2; }

            public static async void LogAsync() { }
        }
        """;

    /// <summary>
    /// `async` is a return-type TRANSFORM: the declaration says int and every caller sees Task of int.
    /// </summary>
    [Fact]
    public void AnAsyncMethodReturnsATask()
    {
        Assert.Equal("Standard.Task<>", ReturnDefinitionOf("ComputeAsync"));
    }

    /// <summary>
    /// The void case needs no second rule -- Task of void is what Standard's bare Task aliases.
    /// </summary>
    [Fact]
    public void AnAsyncVoidMethodReturnsTheResultlessTask()
    {
        Assert.Equal("Standard.Task<>", ReturnDefinitionOf("LogAsync"));
        Assert.True(IsVoidResult("LogAsync"));
    }

    [Fact]
    public void ASyncMethodIsUntouched()
    {
        Assert.Equal("Standard.Int32", ReturnDefinitionOf("ComputeSync"));
    }

    /// <summary>
    /// The same call site has two meanings depending only on where it sits, which is the whole of the
    /// context distinction.
    /// </summary>
    [Fact]
    public void ACallInSyncContextIsDroppedForYou()
    {
        var initializer = InitializerIn("var x = Helpers.ComputeAsync();", isAsync: false);
        var drop = Assert.IsType<InvocationExpression>(initializer);

        Assert.True(drop.IsImplicit);
        Assert.Equal("Drop", drop.Method?.Name);
        Assert.Equal("ComputeAsync", Assert.IsType<InvocationExpression>(TaskResult.Dropped(drop)).Method?.Name);
        Assert.Equal("Standard.Int32", drop.Type.FullyQualifiedName);
    }

    [Fact]
    public void ACallInAsyncContextYieldsTheTask()
    {
        var initializer = InitializerIn("var x = Helpers.ComputeAsync();", isAsync: true);

        _ = Assert.IsType<InvocationExpression>(initializer);
    }

    /// <summary>
    /// An explicit await asks for the task, so the drop the sync context inserted is undone rather than
    /// stacked. Without this the operand is already an int and there is nothing left to drop.
    /// </summary>
    [Fact]
    public void AnExplicitAwaitInSyncContextUndoesTheInsertedOne()
    {
        var initializer = InitializerIn("var x = await Helpers.ComputeAsync();", isAsync: false);
        var drop = Assert.IsType<InvocationExpression>(initializer);

        Assert.False(drop.IsImplicit);
        Assert.Equal("ComputeAsync", Assert.IsType<InvocationExpression>(TaskResult.Dropped(drop)).Method?.Name);
        Assert.Equal("Standard.Int32", drop.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnAwaitBindsToTheTasksDrop()
    {
        var initializer = InitializerIn("Task<int> t = Helpers.ComputeAsync();\nvar x = await t;", isAsync: false);
        var drop = Assert.IsType<InvocationExpression>(initializer);

        Assert.False(drop.IsImplicit);
        Assert.True(TaskResult.IsDrop(drop.Method!));
        _ = Assert.IsType<LocalReferenceExpression>(TaskResult.Dropped(drop));
        Assert.Equal("Standard.Int32", drop.Type.FullyQualifiedName);
    }

    [Fact]
    public void AWrittenDropBindsToTheSameMember()
    {
        var awaited = InitializerIn("Task<int> t = Helpers.ComputeAsync();\nvar x = await t;", isAsync: false);
        var dropped = InitializerIn("Task<int> t = Helpers.ComputeAsync();\nvar x = t.Drop();", isAsync: false);

        Assert.Equal(
            Assert.IsType<InvocationExpression>(awaited).Method!.Name,
            Assert.IsType<InvocationExpression>(dropped).Method!.Name);
        Assert.Equal("Standard.Int32", dropped.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnAwaitOnAValueTaskBindsToItsDrop()
    {
        var initializer = InitializerIn("ValueTask<int> v = async valuetask { return 1; };\nvar x = await v;", isAsync: true);
        var drop = Assert.IsType<InvocationExpression>(initializer);

        Assert.True(TaskResult.IsDrop(drop.Method!));
        Assert.Equal("Standard.ValueTask<>", drop.Method!.DeclaringType!.FullyQualifiedName);
        Assert.Equal("Standard.Int32", drop.Type.FullyQualifiedName);
    }

    [Fact]
    public void TheAsyncFunctorLiftsASyncCall()
    {
        var lifted = Assert.IsType<AsyncExpression>(
            InitializerIn("var x = async Helpers.ComputeSync();", isAsync: false));

        Assert.True(lifted.IsLift);
        Assert.Equal("Standard.Task<>", DefinitionOf(lifted.Type));
    }

    /// <summary>
    /// Applied to a call that is already a task the functor lifts nothing -- it suppresses the await
    /// the sync context would have inserted, which is the same idea seen from the other side.
    /// </summary>
    [Fact]
    public void TheAsyncFunctorSuppressesTheInsertedAwait()
    {
        var suppressed = Assert.IsType<AsyncExpression>(
            InitializerIn("var x = async Helpers.ComputeAsync();", isAsync: false));

        Assert.False(suppressed.IsLift);
        _ = Assert.IsType<InvocationExpression>(suppressed.Operand);
        Assert.Equal("Standard.Task<>", DefinitionOf(suppressed.Type));
    }

    private static string ReturnDefinitionOf(string methodName)
    {
        return DefinitionOf(MethodNamed(methodName).ReturnType);
    }

    private static bool IsVoidResult(string methodName)
    {
        var constructed = Assert.IsType<ConstructedType>(MethodNamed(methodName).ReturnType);

        return constructed.TypeArguments[0].FullyQualifiedName == "Standard.Void";
    }

    private static string DefinitionOf(IMistType type)
    {
        return type is ConstructedType constructed
            ? constructed.Definition.FullyQualifiedName
            : type.FullyQualifiedName;
    }

    private static Method MethodNamed(string methodName)
    {
        var model = StandardModelProbe.Build("", out var diagnostics, Fixtures);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var helpers = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Helpers"]);
        return helpers.MethodsList.Single(method => method.Name == methodName);
    }

    /// <summary>
    /// The statement is placed in a probe method of the requested kind, since which kind it is is the
    /// thing under test.
    /// </summary>
    private static Expression InitializerIn(string statement, bool isAsync)
    {
        var kind = isAsync ? "async void" : "void";
        var types = $$"""
            {{Fixtures}}

            public class Host
            {
                public static {{kind}} Run()
                {
                    {{statement}}
                }
            }
            """;

        var model = StandardModelProbe.Build("", out var diagnostics, types);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var host = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Host"]);
        var body = host.MethodsList.Single(method => method.Name == "Run").Body;

        Assert.NotNull(body);

        var declaration = body.Statements.OfType<LocalDeclarationStatement>().Last();
        Assert.NotNull(declaration.Initializer);

        return declaration.Initializer;
    }
}