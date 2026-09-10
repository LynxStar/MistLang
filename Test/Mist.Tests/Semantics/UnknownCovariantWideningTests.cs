// Covariant widening of a generic at unknown argument positions, and the C# spelling that makes it
// emittable. Both halves are here because the capability is the pair: the relation has to HOLD for the
// model to know a widened receiver is legal, and the construction has to be spelled `dynamic` for
// Roslyn to accept the assignment the relation permits. Neither is visible from program output -- the
// relation because nothing in v2 refuses a program on containment, the spelling because a passing
// program says only that SOME conversion happened. Order 82 of
// backlog/emitter-v2-capability-progression.yaml rests on this file.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class UnknownCovariantWideningTests : ProgramModelProbe
{
    public UnknownCovariantWideningTests()
        : base("MistUnknownWidening_")
    {
    }

    protected override string Fixtures { get; } = """
        public class MyList<T>
        {
            public int count;
            public T item0;

            public MyList()
            {
                this.count = 0;
            }

            public int Size()
            {
                return this.count;
            }

            public T GetAt(int index)
            {
                return this.item0;
            }

            public void Add(T value)
            {
                this.item0 = value;
            }
        }

        public struct MyBox<T>
        {
            public T item;
        }
        """;

    protected override string Preamble { get; } = """
        MyList<int> ints = new MyList<int>();
        """;

    [Fact]
    public void AClassGenericWidensToItsUnknownArgumentForm()
    {
        Assert.True(ConversionRelation.IsWithin(TypeOf("MyList<int>"), TypeOf("MyList<unknown>")));
    }

    /// <summary>
    /// The restriction that keeps this sound. An argument may widen to <c>unknown</c> and to nothing
    /// else, so the ordinary numeric widening from int to long does NOT lift through a construction --
    /// general covariance needs a declared variance annotation, which Mist has no syntax for.
    /// </summary>
    [Fact]
    public void TheWideningIsKeyedOnUnknownAndNotOnOtherSupertypes()
    {
        Assert.True(ConversionRelation.IsWithin(TypeOf("int"), TypeOf("long")));
        Assert.False(ConversionRelation.IsWithin(TypeOf("MyList<int>"), TypeOf("MyList<long>")));
    }

    [Fact]
    public void TheWideningRunsOneWayOnly()
    {
        Assert.False(ConversionRelation.IsWithin(TypeOf("MyList<unknown>"), TypeOf("MyList<int>")));
    }

    /// <summary>
    /// A struct generic is excluded because reaching the <c>dynamic</c> its emission would use means
    /// boxing, which discards the value semantics that made it a struct.
    /// </summary>
    [Fact]
    public void AStructGenericDoesNotWiden()
    {
        Assert.False(ConversionRelation.IsWithin(TypeOf("MyBox<int>"), TypeOf("MyBox<unknown>")));
    }

    [Fact]
    public void AConstructionReachingUnknownIsSpelledDynamic()
    {
        Assert.Equal("dynamic", TypeNameEmitter.Emit(TypeOf("MyList<unknown>")));
    }

    /// <summary>
    /// Transitively, so a construction is dynamic when unknown appears at any depth. Asserted alongside
    /// the bare case because they are the two sides of the same rule and it is the CONSTRUCTION that
    /// changes spelling, never <c>unknown</c> itself.
    /// </summary>
    [Fact]
    public void NestingReachesTheRuleButABareUnknownStaysObject()
    {
        Assert.Equal("dynamic", TypeNameEmitter.Emit(TypeOf("MyList<MyList<unknown>>")));
        Assert.Equal("object", TypeNameEmitter.Emit(TypeOf("unknown")));
    }

    [Fact]
    public void AConstructionWithNoUnknownKeepsItsArguments()
    {
        Assert.Equal("Probe.MyList<int>", TypeNameEmitter.Emit(TypeOf("MyList<int>")));
    }

    /// <summary>
    /// What makes a widened receiver useful: the type parameter is bound to <c>unknown</c> at the call
    /// site, so a read-only member's result comes back as the top type rather than as the erased
    /// original.
    /// </summary>
    [Fact]
    public void AMemberReadThroughAWidenedReceiverYieldsUnknown()
    {
        var call = InitializerOf<InvocationExpression>(
            "var read = widened.GetAt(0);",
            extraPreamble: "MyList<unknown> widened = ints;");

        _ = Assert.IsType<UnknownType>(call.Type);
    }

    private IMistType TypeOf(string typeName)
    {
        return SingleLocal($"{typeName} probe_local = default({typeName});").Type;
    }
}