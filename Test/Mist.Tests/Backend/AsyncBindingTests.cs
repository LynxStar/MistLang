// Tier 3 (Component): what `async` does to a signature, and what a call to a task-returning member
// means in each kind of context. Built over the REAL Standard sources because every type here is
// Standard's Task. No Roslyn and no process spawning.

using Mist.Common.Diagnostics;
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
    public void ACallInSyncContextIsAwaitedForYou()
    {
        var initializer = InitializerIn("var x = Helpers.ComputeAsync();", isAsync: false);
        var await = Assert.IsType<AwaitExpression>(initializer);

        Assert.True(await.IsImplicit);
        Assert.Equal("Standard.Int32", await.Type.FullyQualifiedName);
    }

    [Fact]
    public void ACallInAsyncContextYieldsTheTask()
    {
        var initializer = InitializerIn("var x = Helpers.ComputeAsync();", isAsync: true);

        _ = Assert.IsType<InvocationExpression>(initializer);
    }

    /// <summary>
    /// An explicit await asks for the task, so the one the sync context inserted is undone rather than
    /// stacked. Without this the operand is already an int and there is nothing left to await.
    /// </summary>
    [Fact]
    public void AnExplicitAwaitInSyncContextUndoesTheInsertedOne()
    {
        var initializer = InitializerIn("var x = await Helpers.ComputeAsync();", isAsync: false);
        var await = Assert.IsType<AwaitExpression>(initializer);

        Assert.False(await.IsImplicit);
        _ = Assert.IsType<InvocationExpression>(await.Operand);
        Assert.Equal("Standard.Int32", await.Type.FullyQualifiedName);
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

        var declaration = body.Statements.OfType<LocalDeclarationStatement>().Single();
        Assert.NotNull(declaration.Initializer);

        return declaration.Initializer;
    }
}