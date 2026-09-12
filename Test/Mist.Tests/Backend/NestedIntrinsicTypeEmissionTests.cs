// Tier 3 (Component): how a nested type of a constructed generic is spelled in C#, which is the half of
// order 66 that samples never reached -- foreach never names an enumerator's type, and a local holding
// one had emitted the bare `Enumerator`. Built over the real Standard sources for the intrinsic cases and
// a probe type for the user case; the emitted text is asserted because a program printing the right value
// is equally consistent with `var`. The array case is a struct the backend generates: the CLR has no
// Array.Enumerator, and what it does have allocates and aliases where Array.mist promised a value copy.
// No Roslyn compile and no process spawning.

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
    public void ASpanEnumeratorNamedInTypePositionSpellsTheSame()
    {
        var emitted = Emit("Span<int> buffer = stackalloc int[2];\nSpan<int>.Enumerator e = buffer.GetEnumerator();");

        Assert.Contains("System.Span<int>.Enumerator e = buffer.GetEnumerator();", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlySpanEnumeratorLocalIsSpelledThroughItsInstantiation()
    {
        var emitted = Emit("ReadOnlySpan<int> buffer = stackalloc int[2];\nvar e = buffer.GetEnumerator();");

        Assert.Contains("System.ReadOnlySpan<int>.Enumerator e = buffer.GetEnumerator();", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayEnumeratorLocalIsTheBackendsStruct()
    {
        ArrayEnumeration.Reset();
        var emitted = Emit("int[] xs = [1, 2, 3];\nvar e = xs.GetEnumerator();\nint first = e.Current;");

        Assert.Contains("MistArrayEnumerator<int> e = new MistArrayEnumerator<int>(xs);", emitted, StringComparison.Ordinal);
        Assert.Contains("int first = e.Current;", emitted, StringComparison.Ordinal);
        Assert.True(ArrayEnumeration.Used());
    }

    [Fact]
    public void TheGeneratedStructHasTheDeclaredShape()
    {
        var writer = new CSharpWriter();
        ArrayEnumeration.Emit(writer);
        var runtime = writer.ToString();

        Assert.Contains("internal struct MistArrayEnumerator<T> : System.Collections.Generic.IEnumerator<T>", runtime, StringComparison.Ordinal);
        Assert.Contains("public bool MoveNext()", runtime, StringComparison.Ordinal);
        Assert.Contains("public T Current", runtime, StringComparison.Ordinal);
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