// Tier 3 (Component): what type-argument inference learns from an argument standing in an OPTIONAL
// slot. `Take<T>(T? value)` given a 3 answers int, because `T?` is `T | void` and only the present
// constituent says anything about T. The two refusals matter as much as the answer: a general union
// is not read constituent by constituent, and a `void` argument teaches nothing at all, having taken
// its own type from the slot it fills. Asserted on the model, since emission lets Roslyn infer again.
// The `Optional<T>` spelling of the same slot is banked in samples/OptionalTypeParameters instead:
// the harness registers Standard's type shells without its aliases, so that one cannot resolve here.
// See ProgramModelProbe for the harness.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class InferenceThroughOptionalsTests : ProgramModelProbe
{
    private const string NotInferredId = "MIST0391";

    public InferenceThroughOptionalsTests()
        : base("MistInferenceThroughOptionals_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Box<T>
        {
            public T Value;

            public Box(T value)
            {
                this.Value = value;
            }
        }

        public class Api
        {
            public static T Bare<T>(T? value)
            {
                if (value is T present)
                {
                    return present;
                }

                return default(T);
            }

            public static T Nested<T>(Box<T>? value)
            {
                if (value is Box<T> present)
                {
                    return present.Value;
                }

                return default(T);
            }

            public static int Mixed<T>(T | int value)
            {
                return 1;
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("var bare = Api.Bare(3);", "int")]
    [InlineData("var word = Api.Bare(\"w\");", "string")]
    [InlineData("var nested = Api.Nested(new Box<string>(\"w\"));", "string")]
    public void AnArgumentInAnOptionalSlotInfersFromThePresentConstituent(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Equal(expected, TypeDisplay.Mist(call.Type));
    }

    [Theory]
    [InlineData("var bare = Api.Bare(3);", "int | void")]
    [InlineData("var nested = Api.Nested(new Box<string>(\"w\"));", "Box<string> | void")]
    public void TheArgumentStillBindsAgainstTheWholeOptional(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Equal(expected, TypeDisplay.Mist(Assert.Single(call.ParameterTypes!)));
    }

    /// <summary>
    /// An already-absent argument gives up its own present constituent, so the slot's optionality is
    /// not inferred a second time: T is int, where taking the argument whole would make it
    /// <c>int | void</c> and the return type with it.
    /// </summary>
    [Fact]
    public void AnOptionalArgumentInfersItsOwnPresentConstituent()
    {
        var call = InitializerOf<InvocationExpression>(
            "var absent = Api.Bare(maybe);", extraPreamble: "int? maybe = void;");

        Assert.Equal("int", TypeDisplay.Mist(call.Type));
    }

    [Fact]
    public void AVoidArgumentInfersNothing()
    {
        var diagnostics = BuildRun("var none = Api.Bare(void);");

        _ = Assert.Single(Errors(diagnostics, NotInferredId));
    }

    [Fact]
    public void AGeneralUnionIsNotReadConstituentByConstituent()
    {
        var diagnostics = BuildRun("var none = Api.Mixed(3);");

        _ = Assert.Single(Errors(diagnostics, NotInferredId));
    }
}