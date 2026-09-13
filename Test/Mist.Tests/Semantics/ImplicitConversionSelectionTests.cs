// Tier 3 (Component): which conversion an implicit site calls, asserted on ImplicitConversionExpression against
// a real ProgramModelBuilder run. The same call a cast makes, written invisibly (cast-operators spec,
// Requirement 7): implicit operators only, and an operand reaching the parameter losslessly.

using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class ImplicitConversionSelectionTests : ProgramModelProbe
{
    public ImplicitConversionSelectionTests()
        : base("MistImplicitConversion_")
    {
    }

    protected override string Fixtures { get; } = """
        public enum Priority : int { Low = 1, High = 6 }

        public class Fahrenheit
        {
            public Fahrenheit() { }

            public static int operator +(Fahrenheit a, Fahrenheit b) { return 1; }
        }

        public class Kelvin
        {
            public Kelvin() { }
        }

        public class Celsius
        {
            public Celsius() { }

            public static implicit operator Fahrenheit(Celsius c) { return new Fahrenheit(); }

            public static explicit operator Kelvin(Celsius c) { return new Kelvin(); }
        }

        public class Meters
        {
            public Meters() { }
        }

        public class Reading
        {
            public Reading() { }

            public static implicit operator Meters(Reading r) { return new Meters(); }
        }

        public class Feet
        {
            public Feet() { }

            public static implicit operator Feet(Meters m) { return new Feet(); }

            public static implicit operator Feet(int value) { return new Feet(); }
        }

        public class Gauge
        {
            public Gauge() { }

            public static implicit operator Gauge(uint reading) { return new Gauge(); }

            public static implicit operator Gauge(short reading) { return new Gauge(); }
        }

        public class Tally
        {
            public Tally() { }

            public static implicit operator int(Tally t) { return 1; }

            public static implicit operator long(Tally t) { return 2L; }
        }

        public class Crate
        {
            public Crate() { }

            public static implicit operator Crate(byte size) { return new Crate(); }

            public static implicit operator Crate(string label) { return new Crate(); }
        }

        public class Sink
        {
            public Sink(Fahrenheit f) { }

            public static int Take(Fahrenheit f) { return 1; }

            public static Fahrenheit Back(Celsius c) { return c; }
        }

        public class Chained : Sink
        {
            public Chained(Celsius c) : base(c) { }
        }

        public class Store
        {
            public static Fahrenheit Kept = new Celsius();

            public Fahrenheit Held { get; set; } = new Celsius();

            public Fahrenheit Current => new Celsius();

            public Store() { }

            public int this[Fahrenheit f] { get { return 1; } }

            public void Add(Fahrenheit f) { }
        }
        """;

    protected override string Preamble { get; } = """
        var celsius = new Celsius();
        var reading = new Reading();
        var tally = new Tally();
        byte small = 2;
        func<Fahrenheit, int> take = f => 1;
        Fahrenheit held = new Fahrenheit();
        Fahrenheit? maybe = void;
        bool flag = true;
        var store = new Store();
        """;

    [Theory]
    [InlineData("declaration")]
    [InlineData("assignment")]
    [InlineData("return")]
    [InlineData("argument")]
    [InlineData("construction")]
    [InlineData("chain")]
    [InlineData("function value")]
    [InlineData("field initializer")]
    [InlineData("property initializer")]
    [InlineData("getter")]
    [InlineData("lambda result")]
    [InlineData("array element")]
    [InlineData("tuple element")]
    [InlineData("coalesce")]
    [InlineData("object initializer entry")]
    [InlineData("collection element")]
    [InlineData("switch arm")]
    [InlineData("operator operand")]
    [InlineData("indexer argument")]
    public void AValueReachingItsSlotThroughADeclaredImplicit_RecordsTheOperatorAtEverySite(string site)
    {
        var conversion = Assert.IsType<ImplicitConversionExpression>(ConvertedAt(site));

        Assert.Equal("Probe.Fahrenheit", conversion.Operator?.TargetType.FullyQualifiedName);
        Assert.Equal("Probe.Celsius", conversion.ParameterType?.FullyQualifiedName);
        Assert.Equal("Probe.Celsius", conversion.Operand.Type.FullyQualifiedName);
        Assert.Equal("Probe.Fahrenheit", conversion.Type.FullyQualifiedName);
    }

    [Fact]
    public void BetweenIntegralParametersNeitherContaining_TheConversionCallsTheSignedOne()
    {
        Assert.Equal(WellKnownTypes.Int16.FullyQualifiedName, Conversion("Gauge g = small;").ParameterType?.FullyQualifiedName);
    }

    [Fact]
    public void AnOperatorAnsweringTheSlotsType_OutranksOneWideningIntoIt()
    {
        Assert.Equal(WellKnownTypes.Int64.FullyQualifiedName, Conversion("long l = tally;").Operator?.TargetType.FullyQualifiedName);
    }

    [Fact]
    public void EquallyGoodOperators_AreRecordedAsATieAndNoneIsCalled()
    {
        var conversion = Conversion("double d = tally;");

        Assert.Null(conversion.Operator);
        Assert.Equal(2, conversion.AmbiguousOperators.Count);
    }

    [Fact]
    public void AnEnumReachesAnOperatorOnItsUnderlyingValue_WhichIsLossless()
    {
        var conversion = Conversion("Feet f = Priority.High;");

        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, conversion.ParameterType?.FullyQualifiedName);
        Assert.Equal("Probe.Priority", conversion.Operand.Type.FullyQualifiedName);
    }

    [Fact]
    public void AnOperatorWhoseResultReachesTheSlotLosslessly_IsCalled()
    {
        var conversion = Conversion("int | string u = tally;");

        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, conversion.Type.FullyQualifiedName);
        _ = Assert.IsType<UnionType>(conversion.TargetType);
    }

    [Fact]
    public void AnIntegerConstant_CallsTheOperatorWhoseParameterItFits()
    {
        Assert.Equal(WellKnownTypes.Byte.FullyQualifiedName, Conversion("Crate c = 2;").ParameterType?.FullyQualifiedName);
    }

    [Theory]
    [InlineData("unknown u = celsius;")]
    [InlineData("long l = 5;")]
    [InlineData("byte b = 2;")]
    [InlineData("Feet f = reading;")]
    [InlineData("Kelvin k = celsius;")]
    public void AValueNeedingNoDeclaredImplicitOrMoreThanOne_IsLeftUnwrapped(string statement)
    {
        var declaration = DeclarationIn(statement, extraTypes: "");

        Assert.IsNotType<ImplicitConversionExpression>(declaration.Initializer);
    }

    private ImplicitConversionExpression Conversion(string statement)
    {
        return InitializerOf<ImplicitConversionExpression>(statement);
    }

    private Expression? ConvertedAt(string site)
    {
        return site switch
        {
            "declaration" => DeclarationIn("Fahrenheit f = celsius;", extraTypes: "").Initializer,
            "assignment" => Assert.IsType<AssignmentExpression>(
                BodyIn("held = celsius;").Statements.OfType<ExpressionStatement>().Last().Expression).Value,
            "return" => MethodBody("Sink", "Back").Statements.OfType<ReturnStatement>().Single().Value,
            "argument" => InitializerOf<InvocationExpression>("var taken = Sink.Take(celsius);").Arguments[0],
            "construction" => InitializerOf<NewExpression>("var sink = new Sink(celsius);").Arguments[0],
            "chain" => Assert.IsType<ClassType>(Model().TypesByFQN["Probe.Chained"]).ConstructorsList.Single().ChainArgumentsList[0],
            "function value" => InitializerOf<InvocationExpression>("var called = take(celsius);").Arguments[0],
            "field initializer" => Store().FieldsList.Single().Initializer,
            "property initializer" => Store().PropertiesList.Single(p => p.Name == "Held").Initializer,
            "getter" => Store().PropertiesList.Single(p => p.Name == "Current").Getter?.ExpressionBody,
            "lambda result" => InitializerOf<LambdaExpression>("func<Celsius, Fahrenheit> convert = c => c;").Result,
            "array element" => InitializerOf<ArrayLiteralExpression>("Fahrenheit[] temps = [celsius];").Elements[0],
            "tuple element" => InitializerOf<TupleLiteralExpression>("(Fahrenheit, int) pair = (celsius, 1);").Elements[0],
            "coalesce" => InitializerOf<CoalesceExpression>("Fahrenheit f = maybe ?? celsius;").Right,
            "object initializer entry" => InitializerOf<NewExpression>("var stored = new Store { Held = celsius };").Initializer[0].Value,
            "collection element" => InitializerOf<NewExpression>("var stored = new Store { celsius };").Initializer[0].Value,
            "switch arm" => InitializerOf<SwitchExpression>("Fahrenheit f = flag switch { true => celsius, false => celsius };").Arms[0].Result,
            "operator operand" => InitializerOf<BinaryExpression>("var sum = held + celsius;").Right,
            "indexer argument" => InitializerOf<IndexExpression>("var item = store[celsius];").Index,
            _ => throw new ArgumentOutOfRangeException(nameof(site)),
        };
    }

    private Block MethodBody(string typeName, string methodName)
    {
        var body = Assert.IsType<ClassType>(Model().TypesByFQN[$"Probe.{typeName}"]).MethodsList.Single(m => m.Name == methodName).Body;

        Assert.NotNull(body);
        return body;
    }

    private ClassType Store()
    {
        return Assert.IsType<ClassType>(Model().TypesByFQN["Probe.Store"]);
    }

    private ProgramModel.Model.ProgramModel Model()
    {
        return BuildModel("", extraTypes: "", extraPreamble: "", out _);
    }
}