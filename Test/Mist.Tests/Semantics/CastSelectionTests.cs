// Tier 3 (Component): which conversion operator a cast selects, asserted on CastExpression.Operator
// against a real ProgramModelBuilder run. See ProgramModelProbe for the harness.
//
// WHY THIS CANNOT BE A SAMPLE. Emission writes `((T)operand)` whatever Mist selected, because C# will
// not let a conversion operator be called by name (CS0571), and Roslyn then resolves the cast again
// from the same declarations. So the generated code and the program's output are identical whether Mist
// chose the right operator, the wrong one, or none -- only the model separates them.
//
// Several fixtures declare a competing operator to the same target on ANOTHER type on purpose. Taking
// the first match found would pass a fixture where only one candidate exists.

using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class CastSelectionTests : ProgramModelProbe
{
    private const string FixtureTypes = """
        public class Fahrenheit
        {
            public Fahrenheit(double degrees) { }
        }

        public class Kelvin
        {
            public Kelvin(double degrees) { }
        }

        public class Celsius
        {
            public Celsius(double degrees) { }

            public static implicit operator Fahrenheit(Celsius c) { return new Fahrenheit(1.0); }

            public static explicit operator Kelvin(Celsius c) { return new Kelvin(1.0); }
        }

        public class Measurement
        {
            public Measurement() { }
        }

        public class Weight : Measurement
        {
            public Weight() { }

            public static explicit operator Kelvin(Weight w) { return new Kelvin(2.0); }
        }

        public class Meters
        {
            public Meters(double v) { }
        }

        public class Feet
        {
            public Feet(double v) { }

            public static implicit operator Feet(Meters m) { return new Feet(3.0); }
        }

        public class Unrelated
        {
            public Unrelated() { }
        }
        """;

    private const string EnumTypes = """
        public enum Rank : int
        {
            Low = 1,
            High = 2
        }

        public enum Rival : int
        {
            One = 1
        }
        """;

    public CastSelectionTests()
        : base("MistCast_")
    {
    }

    protected override string Fixtures { get; } = FixtureTypes;

    protected override string Preamble { get; } = """
        var celsius = new Celsius(1.0);
        var weight = new Weight();
        var meters = new Meters(1.0);
        Measurement asBase = new Weight();
        var unrelated = new Unrelated();
        """;

    [Fact]
    public void AnExplicitCast_SelectsTheExplicitOperatorOnTheSource()
    {
        var cast = SingleCast("var k = (Kelvin)celsius;");

        Assert.NotNull(cast.Operator);
        Assert.False(cast.Operator.IsImplicit);
        Assert.Equal("Probe.Celsius", cast.Operator.SourceParameter.Type.FullyQualifiedName);
        Assert.Equal("Probe.Kelvin", cast.Operator.TargetType.FullyQualifiedName);
    }

    /// <summary>
    /// Celsius ALSO declares an explicit operator to Kelvin, so a selection that stopped at the first
    /// operator reaching Kelvin anywhere in the program would pass on the wrong one.
    /// </summary>
    [Fact]
    public void AnExplicitCast_SelectsTheOperatorOnTheActualSourceType()
    {
        var cast = SingleCast("var k = (Kelvin)weight;");

        Assert.NotNull(cast.Operator);
        Assert.Equal("Probe.Weight", cast.Operator.SourceParameter.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnExplicitCast_MaySelectAnImplicitOperator()
    {
        var cast = SingleCast("var f = (Fahrenheit)celsius;");

        Assert.NotNull(cast.Operator);
        Assert.True(cast.Operator.IsImplicit);
        Assert.Equal("Probe.Fahrenheit", cast.Operator.TargetType.FullyQualifiedName);
    }

    /// <summary>
    /// The operator is declared by Feet, the TARGET, rather than by Meters. Both ends of a conversion
    /// may declare it, and searching only the source would miss this one entirely.
    /// </summary>
    [Fact]
    public void ACast_FindsAnOperatorDeclaredByTheTargetType()
    {
        var cast = SingleCast("var f = (Feet)meters;");

        Assert.NotNull(cast.Operator);
        Assert.Equal("Probe.Meters", cast.Operator.SourceParameter.Type.FullyQualifiedName);
        Assert.Equal("Probe.Feet", cast.Operator.TargetType.FullyQualifiedName);
    }

    /// <summary>
    /// Matching brackets the operator's parameter by standard convertibility rather than identity, which
    /// is the whole of what lets a derived source reach an operator its base declares.
    /// </summary>
    [Fact]
    public void ACastFromADerivedSource_ReachesAnOperatorItsBaseDeclares()
    {
        var cast = SingleCast(
            "var l = (Label)derived;",
            extraTypes: """
                public class Label
                {
                    public Label() { }
                }

                public class Vehicle
                {
                    public Vehicle() { }

                    public static implicit operator Label(Vehicle v) { return new Label(); }
                }

                public class Car : Vehicle
                {
                    public Car() { }
                }
                """,
            extraPreamble: "var derived = new Car();");

        Assert.NotNull(cast.Operator);
        Assert.Equal("Probe.Vehicle", cast.Operator.SourceParameter.Type.FullyQualifiedName);
        Assert.Equal("Probe.Label", cast.Operator.TargetType.FullyQualifiedName);
    }

    /// <summary>
    /// A downcast is a conversion the language provides, so there is no operator to record. Null here is
    /// the absence of a USER-DEFINED conversion, never the claim that the cast is illegal -- see
    /// ConversionRelation.FindOperator.
    /// </summary>
    [Fact]
    public void AnInheritanceDowncast_SelectsNoOperator()
    {
        var cast = SingleCast("var w = (Weight)asBase;");

        Assert.Null(cast.Operator);
    }

    [Fact]
    public void ACastWithNoConversionAvailable_SelectsNoOperatorAndIsNotRefused()
    {
        var diagnostics = BuildRun("var x = (Kelvin)unrelated;");

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Null(SingleCast("var x = (Kelvin)unrelated;").Operator);
    }

    [Fact]
    public void ACast_TypesTheExpressionAsItsTarget()
    {
        Assert.Equal("Probe.Kelvin", SingleLocal("var k = (Kelvin)celsius;").Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("var r = (Rank)1;")]
    [InlineData("var r = (Rank)Rival.One;")]
    public void ACastToAnEnumFromAValue_IsTheLookupAnsweringTheOptionalEnum(string statement)
    {
        var cast = SingleCast(statement, EnumTypes);

        Assert.Equal("Probe.Rank", cast.TargetType.FullyQualifiedName);
        Assert.Equal("Probe.Rank", OptionalUnion.InnerType(cast.Type)?.FullyQualifiedName);
        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, cast.LookupParameterType?.FullyQualifiedName);
    }

    [Fact]
    public void ACastToAnEnumFromTheEnum_IsNoLookup()
    {
        var cast = SingleCast("var r = (Rank)Rank.Low;", EnumTypes);

        Assert.Equal("Probe.Rank", cast.Type.FullyQualifiedName);
        Assert.Null(cast.LookupParameterType);
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private CastExpression SingleCast(string statement, string extraTypes = "", string extraPreamble = "")
    {
        return InitializerOf<CastExpression>(statement, extraTypes, extraPreamble);
    }
}