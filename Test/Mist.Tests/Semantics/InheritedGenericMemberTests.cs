// What an inherited member of a GENERIC base resolves to. Asserted on the model because the answer is a
// type, and a sample can only show it indirectly: printing the right value proves the field was found
// and initialized, not that Mist typed it by the receiver's argument rather than leaving it an open
// parameter. A method's half of the same question is InheritedGenericMethodTests. Orders 16 and 53 of
// backlog/emitter-v2-capability-progression.yaml rest on this file.

using Mist.Common.Diagnostics;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class InheritedGenericMemberTests : ProgramModelProbe
{
    public InheritedGenericMemberTests()
        : base("MistInheritedGeneric_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Holder<T>
        {
            public T Value;

            public int Count;

            public Holder(T value)
            {
                Value = value;
                Count = 1;
            }

            public T Echo(T given)
            {
                return given;
            }

            public T ReadOwn()
            {
                return Value;
            }
        }

        public class SameName<T> : Holder<T>
        {
            public SameName(T value) : base(value)
            {
            }
        }

        public class Renamed<U> : Holder<U>
        {
            public Renamed(U value) : base(value)
            {
            }
        }

        public class TwoLevel<W> : Renamed<W>
        {
            public TwoLevel(W value) : base(value)
            {
            }
        }

        public class Pair<A, B>
        {
            public A First;

            public B Second;

            public Pair(A first, B second)
            {
                First = first;
                Second = second;
            }
        }

        public class Swapped<X, Y> : Pair<Y, X>
        {
            public Swapped(Y first, X second) : base(first, second)
            {
            }
        }

        public class HoldsAnArray<T> : Holder<T[]>
        {
            public HoldsAnArray(T[] value) : base(value)
            {
            }
        }
        """;

    protected override string Preamble { get; } = """
        var same = new SameName<int>(1);
        var renamed = new Renamed<string>("a");
        var twoLevel = new TwoLevel<bool>(true);
        var swapped = new Swapped<int, string>("a", 1);
        var holdsArray = new HoldsAnArray<int>([1, 2]);
        """;

    [Fact]
    public void AnInheritedGenericFieldTakesTheReceiversArgument()
    {
        Assert.Equal("Standard.Int32", TypeOf("var probe = same.Value;"));
    }

    /// <summary>
    /// The case a name-and-ordinal match answers wrongly: <c>Holder</c>'s parameter is <c>T</c> and
    /// <c>Renamed</c>'s is <c>U</c>, so matching by name leaves the field an open <c>T</c>. Composing the
    /// base construction's own mapping is what makes the spelling irrelevant.
    /// </summary>
    [Fact]
    public void ABaseParameterRenamedByTheDeriverStillTakesTheReceiversArgument()
    {
        Assert.Equal("Standard.String", TypeOf("var probe = renamed.Value;"));
    }

    [Fact]
    public void ARenamedInheritedFieldIsUsableAsItsSubstitutedType()
    {
        BindsCleanly("var probe = renamed.Value.Length;");
    }

    [Fact]
    public void TwoLevelsOfGenericBaseCompose()
    {
        Assert.Equal("Standard.Boolean", TypeOf("var probe = twoLevel.Value;"));
    }

    /// <summary>
    /// The base's arguments are REORDERED, so an ordinal match answers the other parameter. Asserted in
    /// both positions, since swapping them twice would leave one of the two right by accident.
    /// </summary>
    [Fact]
    public void ReorderedBaseArgumentsAreNotMatchedByOrdinal()
    {
        Assert.Equal("Standard.String", TypeOf("var probe = swapped.First;"));
        Assert.Equal("Standard.Int32", TypeOf("var probe = swapped.Second;"));
    }

    [Fact]
    public void ABaseArgumentThatIsItselfConstructedSubstitutesThrough()
    {
        Assert.Equal("Standard.Array<Int32, unknown>", TypeOf("var probe = holdsArray.Value;"));
    }

    [Fact]
    public void AnInheritedMemberNamingNoParameterIsUnaffected()
    {
        Assert.Equal("Standard.Int32", TypeOf("var probe = same.Count;"));
    }

    [Theory]
    [InlineData("var probe = same.Echo(1);", "Standard.Int32")]
    [InlineData("var probe = renamed.Echo(\"b\");", "Standard.String")]
    [InlineData("var probe = twoLevel.ReadOwn();", "Standard.Boolean")]
    public void AnInheritedGenericMethodTakesTheReceiversArgument(string statement, string expected)
    {
        Assert.Equal(expected, TypeOf(statement));
    }

    private string TypeOf(string statement)
    {
        return SingleLocal(statement).Type.FullyQualifiedName;
    }

    private void BindsCleanly(string statement)
    {
        Assert.Empty(BuildRun(statement).Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}