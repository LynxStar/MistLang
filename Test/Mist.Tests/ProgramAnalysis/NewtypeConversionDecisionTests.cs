// Tier 3 (Component): the conversions between a newtype and a copyable source it clones (newtypes spec
// Requirements 2.4-2.7): out implicitly and losslessly, in only by a cast calling the newtype's conversion,
// and nothing either way for a mutable class that defines no copy. The corpus holds the refusals that have a
// line to mark.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class NewtypeConversionDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_CAST_HAS_NO_CONVERSION", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY", "MIST_PA_NO_IMPLICIT_CONVERSION", "MIST_PA_CAST_REQUIRED",
        "MIST_PA_CAST_ARGUMENT_MISMATCH", "MIST_PA_AMBIGUOUS_CAST", "MIST_PM_UNRESOLVABLE_OVERLOAD",
        "MIST_PM_AMBIGUOUS_OVERLOAD",
    ];

    public NewtypeConversionDecisionTests()
        : base("MistNewtypeConversion_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct Point
        {
            private int hidden;
            public int X;

            public Point(int x)
            {
                this.X = x;
                this.hidden = x;
            }
        }

        public readonly class Money
        {
            public int Amount { get; }

            public Money(int amount)
            {
                this.Amount = amount;
            }
        }

        public class Entity
        {
            public int Id;

            public Entity(int id)
            {
                this.Id = id;
            }
        }

        public class Reading
        {
            public Reading() { }

            public static implicit operator Point(Reading r) { return new Point(1); }
        }

        public class Gauge
        {
            public Gauge() { }

            public static implicit operator Position(Gauge g) { return (Position)new Point(2); }
        }

        public class Sink
        {
            public static int Take(Point point) { return 1; }

            public static int Take(unknown anything) { return 2; }
        }

        public struct Pair<A, B>
        {
            private A first;
            public B Second;

            public Pair(A first, B second)
            {
                this.first = first;
                this.Second = second;
            }
        }

        type Position = new Point;
        type Coord = new Point;
        type IntPair = new Pair<int, string>;
        type Cash = new Money;
        type Actor = new Entity;
        """;

    protected override string Preamble { get; } = """
        var point = new Point(3);
        var position = new Position(4);
        var money = new Money(5);
        var cash = new Cash(6);
        var entity = new Entity(7);
        var actor = new Actor(8);
        var reading = new Reading();
        var pair = new Pair<int, string>(1, "a");
        var intPair = new IntPair(2, "b");
        """;

    [Theory]
    [InlineData("Point leavesAStructClone = position;")]
    [InlineData("Money leavesAReadonlyClassClone = cash;")]
    [InlineData("Position entersByCast = (Position)point;")]
    [InlineData("Cash entersAReadonlyClassByCast = (Cash)money;")]
    [InlineData("Coord crossesThroughTheSource = (Coord)position;")]
    [InlineData("Position entersThroughADeclaredImplicit = (Position)reading;")]
    [InlineData("unknown widensThroughTheSource = position;")]
    [InlineData("Point leavesFreeAfterADeclaredImplicit = new Gauge();")]
    [InlineData("Pair<int, string> leavesAConstructedSource = intPair;")]
    [InlineData("IntPair entersAConstructedSourceByCast = (IntPair)pair;")]
    public void AConversionOfACopyableSource_IsAdmitted(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    [Theory]
    [InlineData("Position entersWithoutACast = point;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Coord crossesWithoutACast = position;", "MIST_PA_CAST_REQUIRED")]
    [InlineData("Entity leavesAMutableClass = actor;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    [InlineData("Actor entersAMutableClassByCast = (Actor)entity;", "MIST_PA_NEWTYPE_SOURCE_DEFINES_NO_COPY")]
    [InlineData("Pair<string, int> leavesOnlyForItsOwnArguments = intPair;", "MIST_PA_NO_IMPLICIT_CONVERSION")]
    public void AConversionTheSourceDoesNotAdmit_IsRefused(string statement, string expected)
    {
        Assert.Equal(expected, Assert.Single(Analyse(statement)).Id.Value);
    }

    [Fact]
    public void ACastToTheNewtype_CallsItsConversionFromTheSource()
    {
        var cast = InitializerOf<CastExpression>("var entered = (Position)point;");

        Assert.True(cast.Operator?.CopiesClone);
        Assert.Equal("Probe.Point", cast.ParameterType?.FullyQualifiedName);
        Assert.Equal("Probe.Position", cast.Type.FullyQualifiedName);
    }

    [Fact]
    public void ANewtypeArgument_RanksItsSourceAboveAWiderParameter()
    {
        var invocation = InitializerOf<InvocationExpression>("var chosen = Sink.Take(position);");

        Assert.Equal("Probe.Point", Assert.Single(invocation.Method!.ParametersList).Type.FullyQualifiedName);
    }

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out var buildDiagnostics);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. buildDiagnostics.Concat(findings.ToImmutableArray()).Where(d => Ids.Contains(d.Id.Value))];
    }
}