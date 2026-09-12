// Tier 3 (Component): how a nested type of a constructed generic is spelled in C#, which is the half of
// order 66 that samples never reached -- foreach never names an enumerator's type, and a local holding
// one had emitted the bare `Enumerator`. Built over the real Standard sources for the intrinsic cases and
// a probe type for the user case; the emitted text is asserted because a program printing the right value
// is equally consistent with `var`. The array case is the backend's own reading: the CLR has no
// Array.Enumerator, and what System.Array.GetEnumerator answers has an object Current where Standard
// declares a TValue one, so the enumerator is read off the array's generic enumerable view and a value
// read from it keeps the type the model gave it. No Roslyn compile and no process spawning.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class NestedIntrinsicTypeEmissionTests
{
    private const string Box = """
        public class Box<T>
        {
            public class Wrapper
            {
                public static string Info() { return "wrapper"; }

                public static Wrapper Make() { return new Wrapper(); }
            }
        }
        """;

    [Fact]
    public void ASpanEnumeratorLocalIsSpelledThroughItsInstantiation()
    {
        var emitted = Emit("Span<int> buffer = stackalloc int[2];\nvar e = buffer.GetEnumerator();");

        Assert.Contains("System.Span<int>.Enumerator e = buffer.GetEnumerator();", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlySpanEnumeratorLocalIsSpelledThroughItsInstantiation()
    {
        var emitted = Emit("ReadOnlySpan<int> buffer = stackalloc int[2];\nvar e = buffer.GetEnumerator();");

        Assert.Contains("System.ReadOnlySpan<int>.Enumerator e = buffer.GetEnumerator();", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayEnumeratorLocalIsTheGenericEnumeratorOfItsElement()
    {
        var emitted = Emit("int[] xs = [1, 2, 3];\nvar e = xs.GetEnumerator();\nint first = e.Current;");

        Assert.Contains(
            "System.Collections.Generic.IEnumerator<int> e = ((System.Collections.Generic.IEnumerable<int>)xs).GetEnumerator();",
            emitted,
            StringComparison.Ordinal);
        Assert.Contains("int first = e.Current;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AUserNestedTypeThroughAnInstantiationIsSpelledUnderIt()
    {
        var emitted = Emit("var info = Box<int>.Wrapper.Info();", Box);

        Assert.Contains("string info = Probe.Box<int>.Wrapper.Info();", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalHoldingAUserNestedTypeIsSpelledUnderTheInstantiation()
    {
        var emitted = Emit("var made = Box<int>.Wrapper.Make();", Box);

        Assert.Contains("Probe.Box<int>.Wrapper made = Probe.Box<int>.Wrapper.Make();", emitted, StringComparison.Ordinal);
    }

    private static string Emit(string statements, string extraTypes = "")
    {
        var model = StandardModelProbe.Build(statements, out var built, extraTypes);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));

        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run");
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(run.Body);

        using(IntrinsicSupply.Enter(model, Type.GetType))
        using(AsyncPromotion.Enter(model))
        {
            BodyEmitter.EmitMethodBody(run, run.Body, writer, diagnostics);
        }

        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
        return writer.ToString();
    }
}