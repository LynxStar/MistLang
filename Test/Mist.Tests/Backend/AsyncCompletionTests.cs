// Tier 3 (Component): what a scope owes a task it holds. Asserted on the emitted TEXT because none of
// it is observable in output -- every helper these samples call completes synchronously, so an omitted
// scope-exit await reads exactly like a present one. Orders 173 to 175 of
// backlog/emitter-v2-capability-progression.yaml rest on this file.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class AsyncCompletionTests
{
    private const string Fixtures = """
        public class Helpers
        {
            public static async int ComputeAsync() { return 1; }

            public static async void LogAsync() { }
        }
        """;

    /// <summary>
    /// A task is relevant, so a scope holding one owes its completion the way it owes a handle its
    /// release. Awaiting is what dropping a task means.
    /// </summary>
    [Fact]
    public void ATaskBoundToALocalIsAwaitedAtScopeExit()
    {
        var emitted = Emit("var task = Helpers.ComputeAsync();\nint v = await task;", isAsync: true);

        Assert.Contains("finally", emitted, StringComparison.Ordinal);
        Assert.Contains("await task;", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// A task nobody kept still has to complete, so it is bound to a temporary rather than left
    /// running. Only reachable in an async context -- a sync one awaits the call where it stands.
    /// </summary>
    [Fact]
    public void ADiscardedTaskIsBoundAndAwaited()
    {
        var emitted = Emit("Helpers.LogAsync();", isAsync: true);

        Assert.Contains("var __", emitted, StringComparison.Ordinal);
        Assert.Contains("finally", emitted, StringComparison.Ordinal);
        Assert.Contains("await __", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// In a sync context the call is awaited where it stands, so nothing reaches emission as a bare
    /// task and there is nothing to defer.
    /// </summary>
    [Fact]
    public void ASyncContextAwaitsTheCallInPlaceInstead()
    {
        var emitted = Emit("Helpers.LogAsync();", isAsync: false);

        Assert.Contains("await ", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("finally", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// An assignment hands the task to a field, which takes the obligation with it. Reading it as a
    /// discard awaits a value the type is still holding -- and did, in a constructor no promotion had
    /// made async, until the predicate was narrowed.
    /// </summary>
    [Fact]
    public void AnAssignmentIsNotADiscard()
    {
        var emitted = Emit(
            "Holder h = new Holder();\nh.Pending = Helpers.ComputeAsync();",
            isAsync: true,
            extra: "public class Holder { public Task<int> Pending; }");

        Assert.DoesNotContain("await __", emitted, StringComparison.Ordinal);
    }

    /// <summary>
    /// The binder never saw these awaits -- the emitter inserts them -- so the promotion pass has to
    /// find them itself, or the method carries an await C# will not accept.
    /// </summary>
    [Fact]
    public void AScopeExitAwaitPromotesTheMethodRetroactively()
    {
        var model = ModelFor("public static void Run() { var t = async Helpers.ComputeAsync(); }");
        var host = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Host"]);
        var run = host.MethodsList.Single(method => method.Name == "Run");

        using var promotion = AsyncPromotion.Enter(model);

        Assert.True(AsyncPromotion.IsPromoted(run), "a method owing a scope-exit await must be async");
    }

    /// <summary>
    /// A method owing nothing stays as Mist declared it. Without this the previous test passes on a
    /// pass that promotes everything.
    /// </summary>
    [Fact]
    public void AMethodOwingNothingIsNotPromoted()
    {
        var model = ModelFor("public static void Run() { int x = 1; }");
        var host = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Host"]);
        var run = host.MethodsList.Single(method => method.Name == "Run");

        using var promotion = AsyncPromotion.Enter(model);

        Assert.False(AsyncPromotion.IsPromoted(run));
    }

    private static ProgramModelClass ModelFor(string member, string extra = "")
    {
        var model = StandardModelProbe.Build(
            "", out var diagnostics, $"{Fixtures}\n\n{extra}\n\npublic class Host\n{{\n    {member}\n}}");

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return model;
    }

    private static string Emit(string statements, bool isAsync, string extra = "")
    {
        var model = ModelFor(
            $"public static {(isAsync ? "async void" : "void")} Run() {{ {statements} }}", extra);

        var host = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Host"]);
        var method = host.MethodsList.Single(candidate => candidate.Name == "Run");
        var writer = new CSharpWriter();

        Assert.NotNull(method.Body);

        // Held for the duration of emission: promotion is what decides whether an await may be
        // spelled, and a scope disposed before the writing would answer no to every question.
        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitMethodBody(method, method.Body, writer, new DiagnosticBag());
        }

        return writer.ToString();
    }
}