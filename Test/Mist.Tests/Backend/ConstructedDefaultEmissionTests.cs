using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ConstructedDefaultEmissionTests
{
    private const string Marker = "Mist.Emission.CSharp.__DefaultInit";

    private readonly GenericDefaultShapes shapes = new();

    [Fact]
    public void ConstructorlessGeneric_TakesAWitnessForTheParameterItsStorageZeroes()
    {
        var emitted = Emit(shapes.Box());

        Assert.Contains($"public Box() : this(default({Marker}), () => default(T))\n", emitted, StringComparison.Ordinal);
        Assert.Contains($"protected Box({Marker} marker, System.Func<T> __default_T)\n", emitted, StringComparison.Ordinal);
        Assert.Contains("this.Value = __default_T();", emitted, StringComparison.Ordinal);
        Assert.Contains("public static Box<T> __CreateDefault(System.Func<T> __default_T)\n", emitted, StringComparison.Ordinal);
        Assert.Contains($"return new Box<T>(default({Marker}), __default_T);", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericWithoutAZeroedParameter_KeepsItsPublicConstructorAsItsDefault()
    {
        var lifted = shapes.Type("Lifted");
        _ = Parameter(lifted, "K");
        _ = Slot(lifted, "Count", shapes.Integer);

        var emitted = Emit(lifted);

        Assert.Contains("public Lifted()\n", emitted, StringComparison.Ordinal);
        Assert.Contains("return new Lifted<K>();", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("constructorless", "public IntBox() : base(default(Mist.Emission.CSharp.__DefaultInit), () => default(")]
    [InlineData("constructor-and-a-block", "protected IntBox(Mist.Emission.CSharp.__DefaultInit marker) : base(marker, () => default(")]
    public void TypeOverAConstructedBase_StartsFromThatBasesSentinelWithItsWitnessFilled(string shape, string expected)
    {
        var intBox = shapes.Type("IntBox", baseType: Construct(shapes.Box("constructor-and-a-block"), shapes.Integer));

        if(shape != "constructorless")
        {
            _ = Constructor(intBox);
            Block(intBox);
        }

        var emitted = Emit(intBox);

        Assert.Contains(expected, emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("base()", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericOverAConstructionOfItsOwnParameter_PassesItsWitnessThrough()
    {
        var derived = shapes.Type("Derived");
        derived.BaseType = Construct(shapes.Box(), Parameter(derived, "U"));

        Assert.Contains(
            $"protected Derived({Marker} marker, System.Func<U> __default_U) : base(marker, __default_U)\n",
            Emit(derived), StringComparison.Ordinal);
    }

    [Fact]
    public void StructOverAConstructedStructBase_AssignsInheritedStorageItsSubstitutedDefault()
    {
        var container = shapes.Box("constructor-and-a-block", isStruct: true, name: "Container");
        var intBox = shapes.Type("IntBox", isStruct: true, baseType: Construct(container, shapes.Inner()));

        var emitted = Emit(intBox);

        Assert.Contains("this.Value = Inner.__CreateDefault();", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain(": base(", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultOfAConstruction_CallsTheOneFactoryWithItsArgumentsDefault(bool admitted)
    {
        var box = Construct(shapes.Box(), admitted ? shapes.Inner() : shapes.Connection());

        Assert.Equal(admitted ? "Probe.Box<Inner>.__CreateDefault(() => Inner.__CreateDefault())" : null, DefaultSynthesis.Expression(box));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultConstraintClosedOverAConstruction_IsWitnessedByItsDefault(bool admitted)
    {
        var parameter = Parameter(shapes.Type("Maker"), defaultConstrained: true);
        var box = Construct(shapes.Box(), admitted ? shapes.Inner() : shapes.Connection());

        Assert.Equal(
            [admitted ? "() => Probe.Box<Inner>.__CreateDefault(() => Inner.__CreateDefault())" : null],
            DefaultConstraintWitness.Arguments([parameter], [box]));
    }

    private static string Emit(ConcreteType type)
    {
        var writer = new CSharpWriter();
        var anyEmitted = false;

        DefaultEmitter.Emit(type, writer, new DiagnosticBag(), ref anyEmitted);

        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}