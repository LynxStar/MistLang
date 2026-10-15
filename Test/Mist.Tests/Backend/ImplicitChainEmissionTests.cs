using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ImplicitChainEmissionTests
{
    private static readonly MemberModifiers Public = DeclarationFragments.Public;
    private readonly HandBuiltModel built = new();

    [Fact]
    public void OmittedChainToAPackOnlyBaseClosedEmpty_PassesTheEmptyTuple()
    {
        var (dial, _) = Derivation(isStruct: false, "pack");

        Assert.Contains("public Dial() : base(default(System.ValueTuple))", Emit(dial), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("parameterless")]
    [InlineData("homogeneous")]
    public void OmittedChainToABaseCallableWithNothingElse_LeavesTheClauseToCSharp(string shape)
    {
        var (dial, _) = Derivation(isStruct: false, shape);

        Assert.DoesNotContain(": base(", Emit(dial), StringComparison.Ordinal);
    }

    [Fact]
    public void OmittedChain_EmitsWhatAnEmptyWrittenChainEmits()
    {
        var (omitted, _) = Derivation(isStruct: false, "pack");
        var (written, meter) = Derivation(isStruct: false, "pack", ConstructorChainKind.Base);
        written.ConstructorsList[0].ChainedConstructor = meter.ConstructorsList[0];
        written.ConstructorsList[0].ChainParameterTypes = [new TupleType([])];

        Assert.Equal(Emit(written), Emit(omitted));
    }

    [Fact]
    public void FlattenedStructChain_PassesTheEmptyTupleToTheAncestorInitializer()
    {
        var (dial, meter) = Derivation(isStruct: true, "pack");
        var writer = new CSharpWriter();

        StructInitEmitter.EmitChainCall(
            dial.ConstructorsList[0], (StructType)dial, new Dictionary<StructType, string> { [(StructType)meter] = "__Meter_ctor" }, writer, new DiagnosticBag());

        Assert.Equal("__Meter_ctor(readings: new System.ValueTuple { });", writer.ToString().Trim());
    }

    private (ConcreteType Derived, ConcreteType Base) Derivation(bool isStruct, string shape, ConstructorChainKind chain = ConstructorChainKind.None)
    {
        ConcreteType meter = isStruct
            ? new StructType("Meter<>", "Probe", new Scope(null), null, null)
            : new ClassType("Meter<>", "Probe", new Scope(null), null, null);
        var pack = new TypeParameter("Ts", 0, meter.Scope, meter) { PackKind = TypePackKind.Unbracketed };
        meter.TypeParametersList.Add(pack);
        List<Parameter> parameters = shape switch
        {
            "pack" => [new Parameter("readings", new TupleType([new SpreadType(pack)])) { IsFlattened = true }],
            "homogeneous" => [new Parameter("readings", built.Type("Int32")) { IsFlattened = true }],
            _ => [],
        };
        meter.ConstructorsList.Add(new Constructor("Meter", meter, parameters, Public) { DeclaringType = meter, Body = HandBuiltModel.Block() });
        ConcreteType dial = isStruct ? HandBuiltModel.Struct("Dial") : HandBuiltModel.Class("Dial");
        dial.BaseType = built.Interner.GetOrCreateConstructedType(meter, [new LiftedType(built.Type("Type"), new TypePackValue([]))]);
        dial.ConstructorsList.Add(new Constructor("Dial", dial, [], Public) { DeclaringType = dial, ChainKind = chain, Body = HandBuiltModel.Block() });
        return (dial, meter);
    }

    private static string Emit(ConcreteType derived)
    {
        var writer = new CSharpWriter();
        var bag = new DiagnosticBag();
        ConstructorEmitter.Emit(derived.ConstructorsList[0], derived, writer, bag);
        Assert.Empty(bag.ToImmutableArray());
        return writer.ToString();
    }
}