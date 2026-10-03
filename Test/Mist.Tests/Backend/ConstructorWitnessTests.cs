using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

public sealed class ConstructorWitnessTests
{
    private readonly GenericDefaultShapes shapes = new();

    [Fact]
    [Trait("Tier", "1")]
    public void ConstructorConstraints_HaveSeparateFactoriesInDeclarationOrder()
    {
        var parameter = Parameter(shapes.Type("Factory"));
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        parameter.ConstraintsList.Add(new ConstructorConstraint([shapes.Integer]));

        var witnesses = ConstructorWitnesses.Of([parameter]);

        Assert.Equal(["__construct_T_0", "__construct_T_1"], ConstructorWitnesses.Names(witnesses));
        Assert.Equal(["System.Func<T> __construct_T_0", "System.Func<int, T> __construct_T_1"], ConstructorWitnesses.Parameters(witnesses));
        Assert.Equal(["() => new T()", "(__arg0_0) => T.__New(__arg0_0)"], ConstructorWitnesses.DefaultArguments(witnesses));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void SameArityConstraints_SelectByArgumentType()
    {
        var parameter = Parameter(shapes.Type("Factory"));
        var boolean = shapes.Type("Boolean", isIntrinsic: true);
        parameter.ConstraintsList.Add(new ConstructorConstraint([shapes.Integer]));
        parameter.ConstraintsList.Add(new ConstructorConstraint([boolean]));

        Assert.Equal("__construct_T_0", ConstructorWitnesses.Name(Assert.IsType<ConstructorFactoryWitness>(ConstructorWitnesses.For(parameter, [shapes.Integer]))));
        Assert.Equal("__construct_T_1", ConstructorWitnesses.Name(Assert.IsType<ConstructorFactoryWitness>(ConstructorWitnesses.For(parameter, [boolean]))));
        Assert.Null(ConstructorWitnesses.For(parameter, 1));
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(17, true)]
    [Trait("Tier", "1")]
    public void WideFactorySignature_UsesATupleOnlyBeyondTheDelegateInputCeiling(int width, bool tuple)
    {
        var parameter = Constrained(Enumerable.Repeat<IMistType>(shapes.Integer, width).ToList());
        var witness = Assert.Single(ConstructorWitnesses.Of([parameter]));
        var fallback = Assert.Single(ConstructorWitnesses.DefaultArguments([witness]));

        Assert.Equal(tuple, ConstructorWitnesses.UsesTuple(witness));
        Assert.StartsWith(tuple ? "System.Func<(int, " : "System.Func<int, ", ConstructorWitnesses.Parameter(witness), StringComparison.Ordinal);
        Assert.StartsWith(tuple ? "(__arguments0) => T.__New(" : "(__arg0_0, __arg0_1, ", fallback, StringComparison.Ordinal);

        for(var index = 0; index < width; index++)
        {
            Assert.Contains(tuple ? $"__arguments0.Item{index + 1}" : $"__arg0_{index}", fallback, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Tier", "2")]
    public void WideFactoryForADefaultConstrainedClass_UnpacksEveryArgumentAfterItsDefaultWitness()
    {
        var box = shapes.Type("Box");
        _ = Parameter(box, defaultConstrained: true);
        var signature = Enumerable.Repeat<IMistType>(shapes.Integer, 17).ToList();
        _ = Constructor(box, signature.ToArray());
        var parameter = Constrained(signature);

        var factory = Assert.Single(ConstructorWitnesses.Arguments([parameter], [Construct(box, shapes.Inner())]));

        Assert.StartsWith("(__arguments0) => new Probe.Box<Inner>(() => Inner.__CreateDefault(), __arguments0.Item1", factory, StringComparison.Ordinal);
        Assert.EndsWith("__arguments0.Item17)", factory, StringComparison.Ordinal);

        for(var index = 1; index <= 17; index++)
        {
            Assert.Contains($"__arguments0.Item{index}", factory, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Tier", "1")]
    public void NestedDeclaration_StoresItsEnclosingConstructorFactories()
    {
        var outer = shapes.Type("Outer");
        var parameter = Parameter(outer);
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        var fragment = new DeclarationFragments();
        var inner = fragment.Class("Inner", outer);
        var item = Parameter(inner, "U");
        item.ConstraintsList.Add(new ConstructorConstraint([shapes.Integer]));

        Assert.Equal(["__construct_T_0", "__construct_U_0"], ConstructorWitnesses.Names(ConstructorWitnesses.Stored(inner)));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ParameterlessFactoryForAConstructorlessGeneric_SuppliesItsSemanticDefaultWitness()
    {
        var product = Construct(shapes.Box(), shapes.Inner());
        var parameter = Constrained([]);

        var factory = Assert.Single(ConstructorWitnesses.Arguments([parameter], [product]));

        Assert.Equal("() => new Probe.Box<Inner>(() => Inner.__CreateDefault())", factory);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ParameterizedFactory_PreservesTheUserConstructorAndSuppliesTheClassDefaultWitness()
    {
        var box = shapes.Type("Box");
        _ = Parameter(box, defaultConstrained: true);
        _ = Constructor(box, shapes.Integer);
        var parameter = Constrained([shapes.Integer]);

        var factory = Assert.Single(ConstructorWitnesses.Arguments([parameter], [Construct(box, shapes.Inner())]));

        Assert.Equal("(__arg0_0) => new Probe.Box<Inner>(() => Inner.__CreateDefault(), __arg0_0)", factory);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ForwardedParameter_PassesItsExistingFactory()
    {
        var destination = Constrained([shapes.Integer]);
        var source = Parameter(shapes.Type("Source"), "U");
        source.ConstraintsList.Add(new ConstructorConstraint([shapes.Integer]));

        Assert.Equal(["__construct_U_0"], ConstructorWitnesses.Arguments([destination], [source]));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void FactoryForAConstructorConstrainedGeneric_SuppliesItsNestedConstructorFactory()
    {
        var holder = shapes.Type("Holder");
        var held = Parameter(holder);
        held.ConstraintsList.Add(new ConstructorConstraint([]));
        var parameter = Constrained([]);

        var factory = Assert.Single(ConstructorWitnesses.Arguments([parameter], [Construct(holder, shapes.Inner())]));

        Assert.Equal("() => new Probe.Holder<Inner>(() => new Inner())", factory);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConstructorlessTypeOverANativeBase_StoresItsWitnessWithoutGainingADefaultFactory()
    {
        var native = shapes.Type("Native");
        native.Assembly = MistAssembly.Ambient;
        _ = Constructor(native);
        var derived = shapes.Type("Derived", baseType: native);
        var item = Parameter(derived, defaultConstrained: true);
        _ = Slot(derived, "Value", item, new DefaultExpression(HandBuiltModel.Node(), item));

        var emitted = Emit(derived);

        Assert.Contains("public Derived(System.Func<T> __default_T)", emitted, StringComparison.Ordinal);
        Assert.Contains("this.__default_T = __default_T;", emitted, StringComparison.Ordinal);
        Assert.Contains("this.Value = __default_T();", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateDefault", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ConstructorFactoryInitializer_RunsOnceAfterTheWitnessIsStoredInTheUserConstructor()
    {
        var holder = shapes.Type("Holder");
        var item = Parameter(holder);
        item.ConstraintsList.Add(new ConstructorConstraint([]));
        _ = Slot(holder, "Held", item, new NewExpression(HandBuiltModel.Node(), item, [], null, [], null));
        _ = Constructor(holder, shapes.Integer);

        var emitted = Emit(holder);
        var assignment = "this.__construct_T_0 = __construct_T_0;";
        var initialization = "this.Held = __construct_T_0();";

        Assert.Contains("public T Held;", emitted, StringComparison.Ordinal);
        Assert.Contains(assignment, emitted, StringComparison.Ordinal);
        _ = Assert.Single(emitted.Split('\n'), line => line.Trim() == initialization);
        Assert.True(emitted.IndexOf(assignment, StringComparison.Ordinal) < emitted.IndexOf(initialization, StringComparison.Ordinal));
        Assert.DoesNotContain("__CreateDefault", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    [Trait("Tier", "2")]
    public void ConstructorFactoryInvocation_PacksWideArgumentsIntoItsSingleTupleInput(int width)
    {
        var signature = Enumerable.Repeat<IMistType>(shapes.Integer, width).ToList();
        var parameter = Constrained(signature);
        var arguments = Enumerable.Range(0, width).Select(index => HandBuiltModel.Literal(shapes.Integer, (long)index)).ToList();
        var expression = new NewExpression(HandBuiltModel.Node(), parameter, arguments, null, [], signature);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitExpression(expression, writer, diagnostics);

        var actual = writer.ToString();
        Assert.StartsWith(width == 17 ? "__construct_T_0((0, 1, " : "__construct_T_0(0, 1, ", actual, StringComparison.Ordinal);
        Assert.EndsWith(width == 17 ? "15, 16))" : "14, 15)", actual, StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AmbientDeclaration_RetainsItsNativeConstructorConstraintWithoutStoringASourceFactory()
    {
        var native = shapes.Type("Native");
        native.Assembly = MistAssembly.Ambient;
        var parameter = Parameter(native);
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));

        _ = Assert.Single(ConstructorWitnesses.Of(native.TypeParametersList));
        Assert.Empty(ConstructorWitnesses.Stored(native));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Tier", "2")]
    public void AmbientConstruction_DoesNotSupplySourceDefaultOrConstructorFactories(bool explicitConstructor)
    {
        var native = shapes.Type("Native", isStruct: true);
        native.Assembly = MistAssembly.Ambient;
        var parameter = Parameter(native);
        parameter.ConstraintsList.Add(new ConstructorConstraint([]));
        _ = Slot(native, "Value", parameter);
        var constructor = explicitConstructor ? Constructor(native) : null;
        var product = Construct(native, shapes.Inner());
        var allocation = new NewExpression(HandBuiltModel.Node(), product, [], constructor, [], []);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        BodyEmitter.EmitExpression(allocation, writer, diagnostics);

        Assert.Equal("new Probe.Native<Inner>()", writer.ToString());
        Assert.Equal(["() => new Probe.Native<Inner>()"], ConstructorWitnesses.Arguments([Constrained([])], [product]));
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    private static string Emit(ConcreteType type)
    {
        var built = new HandBuiltModel();
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        TypeEmitter.EmitTypeBody(type, ConstraintClosureMap.Build(built.Model, diagnostics), built.Model.VariadicUses, writer, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }

    private TypeParameter Constrained(IReadOnlyList<IMistType> signature)
    {
        var parameter = Parameter(shapes.Type("Maker"));
        parameter.ConstraintsList.Add(new ConstructorConstraint(signature));
        return parameter;
    }
}