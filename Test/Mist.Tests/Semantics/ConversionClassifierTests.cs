// Tier 3 (Component): the kind of conversion ConversionClassifier finds between two types of a real
// model. Every row is a case whose answer is a decision rather than an accident -- the rules the partial
// ConversionRelation declines, stated -- so a changed answer is a changed language, and the reasons are
// recorded once, on order 4 of backlog/analysis-v2-capability-progression.yaml.

using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class ConversionClassifierTests : ProgramModelProbe
{
    public ConversionClassifierTests()
        : base("MistConversionClassifier_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Animal
        {
        }

        public class Dog : Animal
        {
        }

        public abstract IBox<T>
        {
        }

        public class Box<T> : IBox<T>
        {
        }

        public class Holder<T> where T : Animal
        {
        }

        public class Kelvin
        {
        }

        public class Celsius
        {
            public static explicit operator Kelvin(Celsius c)
            {
                return new Kelvin();
            }
        }

        public enum Color {
            Red,
            Green,
            Blue
        }

        public enum Priority : int {
            Low = 1,
            High = 2
        }

        type Meters = new double;
        type Feet = new double;
        type AdultAge = int >= 18;
        """;

    protected override string Preamble { get; } = """
        Animal animal = new Dog();
        Dog dog = new Dog();
        int i = 1;
        long l = 2;
        double d = 1.0;
        byte b = 1;
        unknown u = 1;
        int | string mixed = 1;
        Box<int> box = new Box<int>();
        IBox<int> intBox = box;
        IBox<string> textBox = new Box<string>();
        Color color = Color.Red;
        Priority priority = Priority.Low;
        Meters meters = (Meters)1.0;
        Feet feet = (Feet)1.0;
        AdultAge adult = (AdultAge)20;
        Celsius celsius = new Celsius();
        Kelvin kelvin = new Kelvin();
        """;

    [Theory]
    [InlineData("dog", "dog", "Identity")]
    [InlineData("dog", "animal", "Implicit")]
    [InlineData("animal", "dog", "None")]
    [InlineData("i", "l", "Implicit")]
    [InlineData("l", "i", "None")]
    [InlineData("d", "i", "None")]
    [InlineData("i", "mixed", "Implicit")]
    [InlineData("mixed", "i", "None")]
    [InlineData("i", "u", "Implicit")]
    [InlineData("u", "i", "None")]
    [InlineData("box", "intBox", "Implicit")]
    [InlineData("box", "textBox", "None")]
    [InlineData("color", "b", "Implicit")]
    [InlineData("color", "i", "Implicit")]
    [InlineData("i", "color", "Explicit")]
    [InlineData("b", "color", "Explicit")]
    [InlineData("d", "color", "Explicit")]
    [InlineData("color", "priority", "Explicit")]
    [InlineData("meters", "d", "Explicit")]
    [InlineData("d", "meters", "Explicit")]
    [InlineData("feet", "meters", "None")]
    [InlineData("adult", "i", "Implicit")]
    [InlineData("i", "adult", "Explicit")]
    [InlineData("celsius", "kelvin", "Explicit")]
    [InlineData("kelvin", "celsius", "None")]
    public void TheClassificationBetweenTwoLocalsIsTheStatedOne(string from, string to, string expected)
    {
        var types = LocalTypes();

        Assert.Equal(expected, ConversionClassifier.Classify(types[from], types[to]).ToString());
    }

    [Fact]
    public void ATypeParameterIsWithinItsConstraintAndNothingBelowIt()
    {
        var model = BuildModel("", extraTypes: "", extraPreamble: "", out _);
        var parameter = Assert.Single(((ClassType)model.TypesByFQN["Probe.Holder<>"]).TypeParametersList);
        var animal = model.TypesByFQN["Probe.Animal"];
        var dog = model.TypesByFQN["Probe.Dog"];

        Assert.Equal(ConversionKind.Identity, ConversionClassifier.Classify(parameter, parameter));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(parameter, animal));
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(animal, parameter));
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(parameter, dog));
    }

    [Fact]
    public void ASentinelIsUnclassifiedRatherThanRefused()
    {
        var types = LocalTypes();

        Assert.Equal(ConversionKind.Unclassified, ConversionClassifier.Classify(ErrorType.Instance, types["i"]));
        Assert.Equal(ConversionKind.Unclassified, ConversionClassifier.Classify(types["i"], ErrorType.Instance));
    }

    private Dictionary<string, IMistType> LocalTypes()
    {
        return BodyIn("", extraTypes: "", extraPreamble: "")
            .Statements.OfType<LocalDeclarationStatement>()
            .ToDictionary(declaration => declaration.Local.Name, declaration => declaration.Local.Type);
    }
}