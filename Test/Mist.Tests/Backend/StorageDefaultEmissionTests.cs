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
public sealed class StorageDefaultEmissionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    private readonly ClassType integer = new("Int32", "Standard", new Scope(null), null, null)
    {
        Modifiers = Public with { IsIntrinsic = true },
    };

    [Fact]
    public void AutoPropertyOfADefaultableClass_IsAssignedThatClassesDefault()
    {
        var holder = HandBuiltModel.Class("Holder");
        holder.PropertiesList.Add(AutoProperty("Defaulted", Inner()));

        Assert.Contains("this.Defaulted = Inner.__CreateDefault();", Emit(holder), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoPropertyWithAnInitializer_IsLeftToIt()
    {
        var holder = HandBuiltModel.Class("Holder");
        var count = AutoProperty("Count", integer);
        count.Initializer = HandBuiltModel.Literal(integer, 5L);
        holder.PropertiesList.Add(count);

        Assert.DoesNotContain("this.Count", Emit(holder), StringComparison.Ordinal);
    }

    [Fact]
    public void GetOnlyAutoProperty_IsAssignedInTheSentinel()
    {
        var holder = HandBuiltModel.Class("Holder");
        holder.Modifiers = Public with { IsSealed = true };
        holder.PropertiesList.Add(AutoProperty("Defaulted", Inner(), getOnly: true));
        holder.ConstructorsList.Add(new Constructor("Holder", holder, [new Parameter("seed", integer)], Public) { DeclaringType = holder });
        holder.DefaultBlock = new DefaultBlock([], new DefaultBlockDeclarationNode());

        Assert.Contains(
            "private Holder(Mist.Emission.CSharp.__DefaultInit marker)\n{\n    this.Defaulted = Inner.__CreateDefault();\n}",
            Emit(holder), StringComparison.Ordinal);
    }

    [Fact]
    public void StaticAutoProperty_IsNotAssigned()
    {
        var holder = HandBuiltModel.Class("Holder");
        holder.PropertiesList.Add(AutoProperty("Shared", Inner(), isStatic: true));

        Assert.DoesNotContain("Shared", Emit(holder), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefinementSlot_StartsFromItsBasesZero(bool property)
    {
        var holder = HandBuiltModel.Class("Factory");
        var refined = HandBuiltModel.Refined(integer, "NonNegativeInt");

        if(property)
        {
            holder.PropertiesList.Add(AutoProperty("Held", refined));
        }
        else
        {
            holder.FieldsList.Add(new Field("Held", refined, OwnershipKind.Unrestricted, Public));
        }

        Assert.Contains("this.Held = default(int);", Emit(holder), StringComparison.Ordinal);
    }

    [Fact]
    public void AncestorPropertyADerivedStructRedeclares_IsLeftToTheRedeclaration()
    {
        var meter = HandBuiltModel.Struct("Meter");
        meter.PropertiesList.Add(AutoProperty("Reading", integer));
        meter.PropertiesList.Add(AutoProperty("Scale", integer));
        var gauge = HandBuiltModel.Struct("Gauge");
        gauge.BaseType = meter;
        var reading = AutoProperty("Reading", integer);
        reading.Initializer = HandBuiltModel.Literal(integer, 9L);
        gauge.PropertiesList.Add(reading);

        var emitted = Emit(gauge);

        Assert.DoesNotContain("this.Reading", emitted, StringComparison.Ordinal);
        Assert.Contains("this.Scale = default(int);", emitted, StringComparison.Ordinal);
    }

    private ClassType Inner()
    {
        var inner = HandBuiltModel.Class("Inner");
        inner.FieldsList.Add(new Field("Value", integer, OwnershipKind.Unrestricted, Public) { Initializer = HandBuiltModel.Literal(integer, 7L) });
        return inner;
    }

    private static Property AutoProperty(string name, IMistType type, bool getOnly = false, bool isStatic = false)
    {
        return new Property(name, type, Public with { IsStatic = isStatic })
        {
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = getOnly ? null : new PropertyAccessor { IsAuto = true },
        };
    }

    private static string Emit(ConcreteType type)
    {
        var writer = new CSharpWriter();
        var anyEmitted = false;

        DefaultEmitter.Emit(type, writer, new DiagnosticBag(), ref anyEmitted);

        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}