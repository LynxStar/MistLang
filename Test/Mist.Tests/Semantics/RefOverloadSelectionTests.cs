using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class RefOverloadSelectionTests : ProgramModelProbe
{
    public RefOverloadSelectionTests()
        : base("MistRefOverload_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Ov
        {
            public static int Pick(int v)
            {
                return 1;
            }

            public static int Pick(ref int v)
            {
                return 2;
            }

            public static int Only(ref int v)
            {
                return 3;
            }
        }
        """;

    protected override string Preamble { get; } = "int arg = 1;";

    private bool SelectedIsByRef(string call)
    {
        var invocation = InitializerOf<InvocationExpression>($"var chosen = {call};");
        Assert.NotNull(invocation.Method);
        return invocation.Method!.ParametersList[0].Modifier.IsRef;
    }

    [Fact]
    public void ValueArgument_SelectsTheByValueOverload()
    {
        Assert.False(SelectedIsByRef("Ov.Pick(arg)"));
    }

    [Fact]
    public void RefArgument_SelectsTheByRefOverload()
    {
        Assert.True(SelectedIsByRef("Ov.Pick(ref arg)"));
    }

    [Fact]
    public void SoleByRefCandidate_IsStillSelected()
    {
        Assert.True(SelectedIsByRef("Ov.Only(ref arg)"));
    }
}