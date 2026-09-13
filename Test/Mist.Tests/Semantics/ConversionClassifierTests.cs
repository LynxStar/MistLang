// Tier 3 (Component): the kind of conversion ConversionClassifier finds between two types of a real
// model. Every row is a case whose answer is a decision rather than an accident -- the rules the partial
// ConversionRelation declines, stated -- so a changed answer is a changed language, and the reasons are
// recorded once, on order 4 of backlog/analysis-v2-capability-progression.yaml.

using Mist.ProgramModel;
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

        public enum Wide : long {
            One = 1
        }

        type Meters = new double;
        type Feet = new double;
        type AdultAge = int >= 18;
        type PositiveInt = int > 0;
        type Pred<T> = func<T, bool>;

        public class Sized<Count:PositiveInt>
        {
        }
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
        Wide wide = Wide.One;
        Meters meters = (Meters)1.0;
        Feet feet = (Feet)1.0;
        AdultAge adult = (AdultAge)20;
        Celsius celsius = new Celsius();
        Kelvin kelvin = new Kelvin();
        int[] arr = [1, 2];
        var lit = [1, 2];
        int[2] two = [1, 2];
        int[3] three = [1, 2, 3];
        func<int, bool> even = x => true;
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
    [InlineData("i", "color", "None")]
    [InlineData("b", "color", "Explicit")]
    [InlineData("d", "color", "None")]
    [InlineData("i", "priority", "Explicit")]
    [InlineData("b", "priority", "Explicit")]
    [InlineData("l", "priority", "None")]
    [InlineData("color", "priority", "Explicit")]
    [InlineData("priority", "wide", "Explicit")]
    [InlineData("wide", "priority", "None")]
    [InlineData("meters", "d", "Explicit")]
    [InlineData("d", "meters", "Explicit")]
    [InlineData("feet", "meters", "None")]
    [InlineData("adult", "i", "Implicit")]
    [InlineData("i", "adult", "Explicit")]
    [InlineData("celsius", "kelvin", "Explicit")]
    [InlineData("kelvin", "celsius", "None")]
    [InlineData("lit", "arr", "Implicit")]
    [InlineData("two", "arr", "Implicit")]
    [InlineData("arr", "lit", "None")]
    [InlineData("lit", "two", "Identity")]
    [InlineData("two", "three", "None")]
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
    public void AValueParameterIsWithinItsDomain()
    {
        var model = BuildModel("", extraTypes: "", extraPreamble: "", out _);
        var count = Assert.Single(((ClassType)model.TypesByFQN["Probe.Sized<>"]).TypeParametersList);
        var integer = model.TypesByFQN[WellKnownTypes.Int32.FullyQualifiedName];

        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(count, integer));
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(integer, count));
    }

    [Fact]
    public void AConstructedAliasIsWhatItNamesOnceAnInternerCanExpandIt()
    {
        var model = BuildModel("", extraTypes: "", extraPreamble: "", out _);
        var integer = model.TypesByFQN[WellKnownTypes.Int32.FullyQualifiedName];
        var unexpanded = model.TypeInterner.GetOrCreateConstructedType(model.TypesByFQN["Probe.Pred<>"], [integer]);
        var function = ((ClassType)model.TypesByFQN["Probe.Probe"]).MethodsList.Single(m => m.Name == "Run").Body!
            .Statements.OfType<LocalDeclarationStatement>()
            .Single(declaration => declaration.Local.Name == "even").Local.Type;

        Assert.Equal(ConversionKind.Identity, ConversionClassifier.Classify(function, unexpanded, model.TypeInterner));
        Assert.Equal(ConversionKind.Identity, ConversionClassifier.Classify(unexpanded, function, model.TypeInterner));
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(function, unexpanded));
    }

    [Fact]
    public void AFunctionTypeAndADelegateOfTheSameShapeReachEachOther()
    {
        var model = Build(
            """
            namespace System;

            public class MulticastDelegate
            {
            }

            public class Predicate<T> : MulticastDelegate
            {
                public bool Invoke(T value) { return true; }
            }

            public class Comparison<T> : MulticastDelegate
            {
                public int Invoke(T left, T right) { return 0; }
            }

            public class Probe
            {
                public static void Run()
                {
                    func<int, bool> even = x => true;
                    Predicate<int> named = new Predicate<int>();
                    Comparison<int> other = new Comparison<int>();
                }
            }
            """,
            out _);
        var locals = ((ClassType)model.TypesByFQN["System.Probe"]).MethodsList.Single(m => m.Name == "Run").Body!
            .Statements.OfType<LocalDeclarationStatement>()
            .ToDictionary(declaration => declaration.Local.Name, declaration => declaration.Local.Type);

        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(locals["even"], locals["named"]));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(locals["named"], locals["even"]));
        Assert.Equal(ConversionKind.None, ConversionClassifier.Classify(locals["even"], locals["other"]));
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