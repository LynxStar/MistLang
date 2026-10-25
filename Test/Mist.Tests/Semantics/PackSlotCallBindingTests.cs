using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// Tier 3: which declaration a call to a pack-typed slot's name binds, once a base list has bound the
// pack. On the conformer the slot is hidden by the member that fills it; through the abstract the slot
// takes exactly as many arguments as the pack holds.
[Trait("Tier", "3")]
public sealed class PackSlotCallBindingTests : ProgramModelProbe
{
    public PackSlotCallBindingTests()
        : base("MistPackSlotCall_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract IApplier<params TArgs, TResult>
        {
            TResult Apply(params TArgs args);
        }

        public class Adder : IApplier<int, int, int>
        {
            public int Apply(int a, int b) { return a + b; }
        }

        public class Constant : IApplier<int>
        {
            public int Apply() { return 42; }
        }

        public class Both : IApplier<int, int, int>
        {
            public int Apply(int a, int b) { return a - b; }
            public int Apply((int, int) pair) { return 0; }
        }

        public class Pre<params TRest> : IApplier<int, TRest..., int>
        {
            public int Apply(int first, params TRest rest) { return first; }
        }
        """;

    protected override string Preamble { get; } = """
        IApplier<int, int, int> applier = new Adder();
        IApplier<int> constant = new Constant();
        """;

    [Theory]
    [InlineData("var n = new Adder().Apply(2, 3);", "Adder", 2)]
    [InlineData("var n = new Constant().Apply();", "Constant", 0)]
    [InlineData("var n = new Both().Apply(2, 3);", "Both", 2)]
    [InlineData("var n = new Both().Apply((2, 3));", "Both", 1)]
    [InlineData("var n = new Pre<string, bool>().Apply(1, \"x\", true);", "Pre<>", 2)]
    [InlineData("var n = applier.Apply(2, 3);", "IApplier<,>", 1)]
    [InlineData("var n = constant.Apply();", "IApplier<,>", 1)]
    public void ACallBindsTheDeclarationTheReceiverAnswersWith(string statement, string declaring, int parameters)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Equal(declaring, call.Method?.DeclaringType?.Name);
        Assert.Equal(parameters, call.Method?.ParametersList.Count);
    }

    [Theory]
    [InlineData("var n = applier.Apply(2);")]
    [InlineData("var n = applier.Apply(1, 2, 3);")]
    [InlineData("var n = applier.Apply((2, 3));")]
    [InlineData("var n = constant.Apply(1);")]
    public void ACallThroughTheAbstract_TakesExactlyTheWidthThePackWasBoundTo(string statement)
    {
        _ = Assert.Single(Errors(BuildRun(statement), "MIST0385"));
    }
}