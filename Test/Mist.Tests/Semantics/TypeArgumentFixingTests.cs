// Tier 3 (Component): what inference fixes a type parameter to when its arguments give it different
// types -- the one type the others all convert to, or nothing, in which case the call is refused rather
// than bound on a type no argument supplied. Asserted on the model, since emission states the fixed type
// and Roslyn would compile several of the wrong answers. See ProgramModelProbe for the harness.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class TypeArgumentFixingTests : ProgramModelProbe
{
    private const string ConflictId = "MIST0392";
    private const string NotInferredId = "MIST0391";

    public TypeArgumentFixingTests()
        : base("MistTypeArgumentFixing_")
    {
    }

    protected override string Fixtures { get; } = """
        type NonNegative = int >= 0;
        type Positive = int > 0;

        public class Animal
        {
        }

        public class Dog : Animal
        {
        }

        public class Cat : Animal
        {
        }

        public struct Meters
        {
            public int Value;

            public Meters(int value)
            {
                Value = value;
            }
        }

        public struct Feet
        {
            public int Value;

            public Feet(int value)
            {
                Value = value;
            }

            public static implicit operator Meters(Feet feet)
            {
                return new Meters(feet.Value);
            }
        }

        public class Api
        {
            public static T Pick<T>(T a, T b)
            {
                return b;
            }

            public static void Unstated<T, U>(T a, T b)
            {
            }
        }

        public class Over
        {
            public static T Pick<T>(T a, T b)
            {
                return b;
            }

            public static string Pick(unknown a, unknown b)
            {
                return "unknown";
            }
        }
        """;

    protected override string Preamble { get; } = """
        NonNegative n = 3;
        Positive p = 5;
        unknown u = 1;
        """;

    [Theory]
    [InlineData("var r = Api.Pick(1, 2L);", "long")]
    [InlineData("var r = Api.Pick(2L, 1);", "long")]
    [InlineData("var r = Api.Pick(new Dog(), new Animal());", "Animal")]
    [InlineData("var r = Api.Pick(u, 1);", "unknown")]
    [InlineData("var r = Api.Pick(n, 4);", "int")]
    [InlineData("var r = Api.Pick(4, n);", "int")]
    [InlineData("var r = Api.Pick(new Feet(3), new Meters(1));", "Meters")]
    public void DifferingBoundsFixToTheOneTheOthersAllConvertTo(string statement, string expected)
    {
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Equal(expected, TypeDisplay.Mist(call.Type));
        Assert.Empty(Errors(BuildRun(statement), ConflictId));
    }

    [Fact]
    public void RefinementsTheOthersReachOnlyThroughTheirBaseFixToOneOfThemRatherThanConflict()
    {
        var statement = "var r = Api.Pick(n, p);";
        var call = InitializerOf<InvocationExpression>(statement);

        _ = Assert.IsType<RefinedType>(call.Type);
        Assert.Empty(Errors(BuildRun(statement), ConflictId));
    }

    [Theory]
    [InlineData("var r = Api.Pick(\"x\", 1);", "'string', 'int'")]
    [InlineData("var r = Api.Pick(new Dog(), new Cat());", "'Dog', 'Cat'")]
    public void BoundsNoneOfWhichTheOthersConvertToAreRefusedNamingThem(string statement, string named)
    {
        var diagnostics = BuildRun(statement);

        var conflict = Assert.Single(Errors(diagnostics, ConflictId));
        Assert.Contains(named, conflict.Message);
        Assert.Empty(Errors(diagnostics, NotInferredId));
    }

    [Fact]
    public void AUnionTheCallStatesBindsWhereInferenceWouldRefuse()
    {
        var statement = "var r = Api.Pick<string | int>(\"x\", 1);";
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Equal("int | string", TypeDisplay.Mist(call.Type));
        Assert.Empty(Errors(BuildRun(statement), ConflictId));
    }

    [Fact]
    public void AConflictingCandidateIsPassedOverForOneThatApplies()
    {
        var statement = "var r = Over.Pick(\"x\", 1);";
        var call = InitializerOf<InvocationExpression>(statement);

        Assert.Empty(call.Method!.TypeParametersList);
        Assert.Empty(Errors(BuildRun(statement), ConflictId));
    }

    [Fact]
    public void AParameterNoArgumentMentionsIsReportedAheadOfAConflict()
    {
        var diagnostics = BuildRun("Api.Unstated(\"x\", 1);");

        _ = Assert.Single(Errors(diagnostics, NotInferredId));
        Assert.Empty(Errors(diagnostics, ConflictId));
    }
}