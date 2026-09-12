// Tier 3 (Component): the shape of the task-combinator runtime, and of a call whose result is discarded.
// Asserted on emitted text: the wrappers exist to supply the declared shape, so where nothing observes
// it -- a statement-position await -- the CLR call is written directly and no wrapper is owed. No Roslyn
// compile and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class TaskCombinatorEmissionTests
{
    private static string EmitRun(string statements)
    {
        var model = StandardModelProbe.Build(statements, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();

        TaskCombinators.Reset();
        BodyEmitter.EmitBlock(run.Body!, writer, new DiagnosticBag());

        return writer.ToString();
    }

    [Fact]
    public void OnlyTheVoidWhenAllIsAsync()
    {
        var writer = new CSharpWriter();

        TaskCombinators.Emit(writer);
        var text = writer.ToString();

        Assert.Contains("public static async System.Threading.Tasks.Task<Standard.Void[]> WhenAll(", text);
        Assert.Contains("public static System.Threading.Tasks.Task<T[]> WhenAll<T>(", text);
        Assert.Contains("public static System.Threading.Tasks.Task WhenAny(", text);
        Assert.Contains("public static System.Threading.Tasks.Task<T> WhenAny<T>(", text);
        Assert.Equal(1, text.Split("async ").Length - 1);
    }

    [Fact]
    public void ADiscardedCombinatorCallsTheClrDirectly()
    {
        var text = EmitRun("Task[] pending = new Task[0];\nTask.WhenAll(pending);");

        Assert.Contains("System.Threading.Tasks.Task.WhenAll(pending)", text);
        Assert.DoesNotContain("MistTasks", text);
        Assert.False(TaskCombinators.Used());
    }

    [Fact]
    public void AnObservedCombinatorGoesThroughTheWrapper()
    {
        var text = EmitRun("Task[] pending = new Task[0];\nvar settled = Task.WhenAll(pending);");

        Assert.Contains("MistTasks.WhenAll(pending)", text);
        Assert.True(TaskCombinators.Used());
    }
}