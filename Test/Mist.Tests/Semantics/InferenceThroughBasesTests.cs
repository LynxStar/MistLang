// Tier 3 (Component): what type-argument inference learns from an argument whose OWN type is not a
// construction of the parameter's definition but whose base chain reaches one. `First<T>(Holder<T>)`
// called with an IntHolder answers int only because IntHolder conforms to Holder<int>, and a generic
// conformer answers through its receiver's arguments. Asserted on the model, since emission lets
// Roslyn infer again. See ProgramModelProbe for the harness.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class InferenceThroughBasesTests : ProgramModelProbe
{
    private const string NotInferredId = "MIST_PM_TYPE_ARGUMENTS_NOT_INFERRED";

    public InferenceThroughBasesTests()
        : base("MistInferenceThroughBases_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract Holder<T>
        {
            T Item();
        }

        public class IntHolder : Holder<int>
        {
            public int Item()
            {
                return 5;
            }
        }

        public class Wrapper<T> : Holder<T>
        {
            private T held;

            public Wrapper(T held)
            {
                this.held = held;
            }

            public T Item()
            {
                return held;
            }
        }

        public class Api
        {
            public static T First<T>(Holder<T> holder)
            {
                return holder.Item();
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("var first = Api.First(new IntHolder());", "int")]
    [InlineData("var word = Api.First(new Wrapper<string>(\"w\"));", "string")]
    public void AnArgumentConformingToAConstructionOfTheParameterInfersThroughIt(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Equal("First", call.Method?.Name);
        Assert.Equal(expected, TypeDisplay.Mist(call.Type));
    }

    [Fact]
    public void AnArgumentReachingNoConstructionStillReportsTheInferenceFailure()
    {
        var diagnostics = BuildRun("var none = Api.First(3);");

        _ = Assert.Single(Errors(diagnostics, NotInferredId));
    }
}