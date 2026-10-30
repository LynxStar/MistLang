using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

public sealed class GenericInitializationContractEmissionTests
{
    private readonly GenericDefaultShapes shapes = new();

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false)]
    [InlineData(true)]
    public void CompatibilityFactoryRejectsANullClrDefault(bool factory)
    {
        var type = shapes.Type("Box");
        _ = Parameter(type, defaultConstrained: true);

        Assert.Equal(["() => default(T) ?? throw new System.InvalidOperationException(\"A semantic default witness is required when the CLR default is null.\")"],
            DefaultWitnesses.CompatibilityArguments(type, factory));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void CompatibilityFactoryIncludesStorageWitnessesWithoutExpandingUserConstruction()
    {
        var type = shapes.Box();

        Assert.Empty(DefaultWitnesses.CompatibilityArguments(type, factory: false));
        Assert.Contains("default(T) ?? throw", Assert.Single(DefaultWitnesses.CompatibilityArguments(type, factory: true)), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(TypeParameterConstraintKind.Struct)]
    [InlineData(TypeParameterConstraintKind.Stampable)]
    public void ValueKindCompatibilityFactoryKeepsItsNonnullableClrDefault(TypeParameterConstraintKind kind)
    {
        var type = shapes.Type("Box");
        var parameter = Parameter(type, defaultConstrained: true);
        parameter.ConstraintsList.Add(new KeywordConstraint(kind));

        Assert.Equal(["() => default(T)"], DefaultWitnesses.CompatibilityArguments(type, factory: true));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void PartConstructorAndFactoryCarryTheStoredDefaultWitness()
    {
        var type = GenericStruct("Root");
        var parameter = Parameter(type, defaultConstrained: true);
        _ = Slot(type, "Value", parameter);
        _ = Constructor(type, shapes.Integer);

        var emitted = Emit(type);

        Assert.Contains("private Root(System.Func<T> __default_T, T p0, Mist.Emission.CSharp.__StructInit marker)\n    {\n        this.__default_T = __default_T;\n        this.Value = p0;", emitted, StringComparison.Ordinal);
        Assert.Contains("__FromParts(System.Func<T> __default_T, T p0, Mist.Emission.CSharp.__StructInit marker)", emitted, StringComparison.Ordinal);
        Assert.Contains("return new Root<T>(__default_T, p0, default(Mist.Emission.CSharp.__StructInit));", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConversionCopiesTheRenamedDefaultWitnessFromItsSource()
    {
        var root = GenericStruct("Root");
        _ = Slot(root, "Value", Parameter(root, defaultConstrained: true));
        _ = Constructor(root, shapes.Integer);
        var leaf = GenericStruct("Leaf");
        leaf.BaseType = Construct(root, Parameter(leaf, "U", defaultConstrained: true));

        Assert.Contains("return Probe.Root<U>.__FromParts(value.__default_U, value.Value, default);", Emit(leaf), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConversionToAClosedAncestorSuppliesItsConcreteDefaultFactory()
    {
        var root = GenericStruct("Root");
        _ = Slot(root, "Value", Parameter(root, defaultConstrained: true));
        _ = Constructor(root, shapes.Integer);
        var leaf = GenericStruct("Leaf");
        leaf.BaseType = Construct(root, shapes.Inner());

        Assert.Contains("return Probe.Root<Inner>.__FromParts(() => Inner.__CreateDefault(), value.Value, default);", Emit(leaf), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConversionComposesTheDefaultWitnessThroughTwoGenericBases()
    {
        var root = GenericStruct("Root");
        _ = Slot(root, "Value", Parameter(root, defaultConstrained: true));
        _ = Constructor(root, shapes.Integer);
        var middle = GenericStruct("Middle");
        middle.BaseType = Construct(root, Parameter(middle, "U", defaultConstrained: true));
        var leaf = GenericStruct("Leaf");
        leaf.BaseType = Construct(middle, Parameter(leaf, "V", defaultConstrained: true));

        Assert.Contains("return Probe.Root<V>.__FromParts(value.__default_V, value.Value, default);", Emit(leaf), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void PartConstructionCarriesConstructorWitnessesAsWellAsDefaults()
    {
        var root = GenericStruct("Root");
        var parameter = Parameter(root, defaultConstrained: true);
        parameter.ConstraintsList.Add(new ConstructorConstraint([shapes.Integer]));
        _ = Constructor(root, shapes.Integer);
        var leaf = GenericStruct("Leaf");
        var filling = Parameter(leaf, "U", defaultConstrained: true);
        filling.ConstraintsList.Add(new ConstructorConstraint([shapes.Integer]));
        leaf.BaseType = Construct(root, filling);

        var emitted = Emit(root);

        Assert.Contains("private Root(System.Func<T> __default_T, System.Func<int, T> __construct_T_0, Mist.Emission.CSharp.__StructInit marker)", emitted, StringComparison.Ordinal);
        Assert.Contains("return new Root<T>(__default_T, __construct_T_0, default(Mist.Emission.CSharp.__StructInit));", emitted, StringComparison.Ordinal);
        Assert.Contains("return Probe.Root<U>.__FromParts(value.__default_U, value.__construct_U_0, default);", Emit(leaf), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ReceiverAlsoQualifiesWitnessesInsideAConcreteConstructorFactory()
    {
        var root = GenericStruct("Root");
        var parameter = Parameter(root);
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        var leaf = GenericStruct("Leaf");
        var filling = Parameter(leaf, "U", defaultConstrained: true);
        leaf.BaseType = Construct(root, Construct(shapes.Box(), filling));

        Assert.Contains("return Probe.Root<Probe.Box<U>>.__FromParts(() => new Probe.Box<U>(value.__default_U), default);", Emit(leaf), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void UnwitnessedStructStorageExplicitlyCopiesItsClrZero(bool property)
    {
        var type = GenericStruct("Raw");
        var parameter = Parameter(type);
        if(property)
        {
            type.PropertiesList.Add(new Property("Value", parameter, DeclarationFragments.Public)
            {
                Getter = new PropertyAccessor { IsAuto = true },
            });
        }
        else
        {
            _ = Slot(type, "Value", parameter);
        }

        _ = Slot(type, "Mark", shapes.Integer, HandBuiltModel.Literal(shapes.Integer, 7L));
        var constructor = Constructor(type, shapes.Integer);
        var writer = new CSharpWriter();
        ConstructorEmitter.Emit(constructor, type, writer, new DiagnosticBag());
        var emitted = Emit(type);

        Assert.Contains("this.Value = default(Raw<T>).Value!;", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("int Mark = 7;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("this.Mark =", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("this = default", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void SemanticDefaultAndWrittenInitializerKeepTheirOwnRoutes()
    {
        var type = GenericStruct("Root");
        var parameter = Parameter(type, defaultConstrained: true);
        _ = Slot(type, "Value", parameter);
        _ = Slot(type, "Written", parameter, HandBuiltModel.Literal(parameter));
        _ = Constructor(type, shapes.Integer);

        var writer = new CSharpWriter();
        DefaultWitnesses.EmitInitializers(type, writer, new DiagnosticBag());

        Assert.Contains("this.Value = __default_T();", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("default(Root<T>)", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("this.Written", writer.ToString(), StringComparison.Ordinal);
    }

    private StructType GenericStruct(string name)
    {
        return (StructType)shapes.Type(name, isStruct: true);
    }

    private static string Emit(StructType type)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        TypeEmitter.EmitTypeBody(type, new ConstraintClosureMap(), new VariadicUseRegistry(), writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}