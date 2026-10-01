using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class DerivedDefaultEmissionTests
{
    private const string Marker = "Mist.Emission.CSharp.__DefaultInit";

    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly ClassType integer = new("Int32", "Standard", new Scope(null), null, null)
    {
        Modifiers = Public with { IsIntrinsic = true },
    };

    [Theory]
    [InlineData(false, "protected")]
    [InlineData(true, "private")]
    public void Sentinel_IsReachableFromADerivedDefaultUnlessNothingCanDerive(bool isSealed, string accessibility)
    {
        var meter = Meter(HandBuiltModel.Class("Meter"));
        meter.Modifiers = Public with { IsSealed = isSealed };

        Assert.Contains($"{accessibility} Meter({Marker} marker)\n", Emit(meter), StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructorlessDerivedClass_StartsFromTheBaseSentinelRatherThanItsConstructor()
    {
        var gauge = HandBuiltModel.Class("Gauge", Meter(HandBuiltModel.Class("Meter")));

        Assert.Contains($"public Gauge() : base(default({Marker}))\n", Emit(gauge), StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedClassWithAConstructor_PassesItsSentinelsMarkerToTheBaseSentinel()
    {
        var dial = HandBuiltModel.Class("Dial", Meter(HandBuiltModel.Class("Meter")));
        Declare(dial);
        dial.DefaultBlock = new DefaultBlock([], new DefaultBlockDeclarationNode());

        Assert.Contains($"protected Dial({Marker} marker) : base(marker)\n", Emit(dial), StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedClassOverAConstructorlessBase_ReachesTheBaseThroughItsPublicConstructor()
    {
        var gauge = HandBuiltModel.Class("Gauge", HandBuiltModel.Class("Meter"));

        Assert.Contains("public Gauge()\n", Emit(gauge), StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedStruct_AssignsTheBaseBlockToTheFieldItInherits()
    {
        var gauge = HandBuiltModel.Struct("Gauge");
        gauge.BaseType = Meter(HandBuiltModel.Struct("Meter"));

        var emitted = Emit(gauge);

        Assert.Contains("this.Reading = 0;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain(": base(", emitted, StringComparison.Ordinal);
    }

    private ConcreteType Meter(ConcreteType meter)
    {
        meter.FieldsList.Add(new Field("Reading", integer, OwnershipKind.Unrestricted, Public)
        {
            Initializer = HandBuiltModel.Literal(integer, 1L),
        });
        Declare(meter, integer);
        meter.DefaultBlock = new DefaultBlock(
            [new DefaultValueEntry("Reading", HandBuiltModel.Literal(integer, 0L))], new DefaultBlockDeclarationNode());
        return meter;
    }

    private static void Declare(ConcreteType owner, params IMistType[] parameters)
    {
        owner.ConstructorsList.Add(new Constructor(
            owner.Name, owner, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), Public)
        {
            DeclaringType = owner,
        });
    }

    private static string Emit(ConcreteType type)
    {
        var writer = new CSharpWriter();
        var anyEmitted = false;

        DefaultEmitter.Emit(type, writer, new DiagnosticBag(), ref anyEmitted);

        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}