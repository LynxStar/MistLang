using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class NativeSlotNullabilityEmissionTests
{
    private readonly HandBuiltModel built = new();
    private readonly List<ConcreteType> conformers = [];
    private readonly ClassType text;
    private readonly StructType number = new("Int32", "Standard", new Scope(null), null, null);
    private readonly AbstractType provider;
    private readonly AbstractType formattable;

    public NativeSlotNullabilityEmissionTests()
    {
        text = built.Type("String");
        provider = Native("IFormatProvider");
        formattable = Native("IFormattable");
        _ = Slot(formattable, "ToString", text, ("format", text, true), ("formatProvider", provider, true));
    }

    [Fact]
    public void AnImplementer_DeclaresTheSlotsNullableParametersNullable_AndAnOrdinaryMethodIsUntouched()
    {
        var formatter = Conformer("Formatter", formattable);
        var answer = Member(formatter, "ToString", text, ("format", text), ("provider", provider));
        var ordinary = Member(formatter, "Join", text, ("format", text), ("provider", provider));

        Assert.Equal("string ToString(string? format, IFormatProvider? provider)", Signature(answer));
        Assert.Equal("string Join(string format, IFormatProvider provider)", Signature(ordinary));
    }

    [Fact]
    public void AParameterTheSlotDeclaresNonNull_KeepsItsSpelling()
    {
        var partial = Native("IPartial");
        _ = Slot(partial, "Show", text, ("maybe", text, true), ("sure", text, false));
        var answer = Member(Conformer("Shown", partial), "Show", text, ("maybe", text), ("sure", text));

        Assert.Equal("string Show(string? maybe, string sure)", Signature(answer));
    }

    [Fact]
    public void AGenericSlot_IsAnnotatedOnlyWhereItsArgumentIsAReference()
    {
        var comparable = Native("IComparable");
        var compared = new TypeParameter("T", 0, comparable.Scope, comparable);
        comparable.TypeParametersList.Add(compared);
        _ = Slot(comparable, "CompareTo", number, ("other", compared, true));

        var pair = Conformer("Pair");
        pair.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(comparable, [pair]));
        var byReference = Member(pair, "CompareTo", number, ("other", pair));

        var count = Conformer("Count");
        count.BaseAbstractTypes.Add(built.Interner.GetOrCreateConstructedType(comparable, [number]));
        var byValue = Member(count, "CompareTo", number, ("other", number));

        Assert.Equal("int CompareTo(Pair? other)", Signature(byReference));
        Assert.Equal("int CompareTo(int other)", Signature(byValue));
    }

    [Fact]
    public void ASlotReachedThroughAMistAbstract_IsAnswered()
    {
        var pretty = new AbstractType("IPretty", "Probe", new Scope(null), null, null) { Modifiers = DeclarationFragments.Public };
        pretty.BaseAbstractTypes.Add(formattable);
        var answer = Member(Conformer("Pretty", pretty), "ToString", text, ("format", text), ("provider", provider));

        Assert.Equal("string ToString(string? format, IFormatProvider? provider)", Signature(answer));
    }

    [Fact]
    public void AnInheritedAnswer_IsAnnotatedOnTheBaseThatDeclaresIt()
    {
        var plain = Conformer("Plain");
        var inherited = Member(plain, "ToString", text, ("format", text), ("provider", provider));
        Conformer("Late", formattable).BaseType = plain;

        Assert.Equal("string ToString(string? format, IFormatProvider? provider)", Signature(inherited));
    }

    [Fact]
    public void AMappedMember_AndTheExplicitImplementationForwardingToIt_AreBothAnnotated()
    {
        var mapped = Conformer("Mapped");
        var render = Member(mapped, "Render", text, ("format", text), ("provider", provider));
        mapped.ConformancesList.Add(new ConformanceDeclaration(
            mapped, formattable, "Probe", [new MethodMapping(render, formattable.MethodsList.Single(), formattable)], [], HandBuiltModel.Node()));

        var writer = new CSharpWriter();
        var emitted = false;

        using(NativeSlotNullability.Enter(conformers, built.Interner))
        {
            ConformanceEmitter.Emit(mapped, writer, new DiagnosticBag(), ref emitted);
        }

        Assert.Equal("string Render(string? format, IFormatProvider? provider)", Signature(render));
        Assert.Equal(
            "string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => this.Render(format, formatProvider);",
            writer.ToString().Trim());
    }

    private string Signature(Method method)
    {
        using(NativeSlotNullability.Enter(conformers, built.Interner))
        {
            return MethodEmitter.EmitSignature(method, new DiagnosticBag());
        }
    }

    private static AbstractType Native(string name)
    {
        return new AbstractType(name, "System", new Scope(null), null, null) { Modifiers = DeclarationFragments.Public };
    }

    private static Method Slot(AbstractType owner, string name, IMistType returns, params (string Name, IMistType Type, bool Nullable)[] parameters)
    {
        var slot = new Method(
            name, returns, [.. parameters.Select(parameter => new Parameter(parameter.Name, parameter.Type) { ClrDeclaresNullable = parameter.Nullable })],
            DeclarationFragments.Public with { IsClrProjected = true })
        {
            DeclaringType = owner,
            Scope = new Scope(owner.Scope),
        };
        owner.MethodsList.Add(slot);
        return slot;
    }

    private ClassType Conformer(string name, AbstractType? implemented = null)
    {
        var conformer = new ClassType(name, "Probe", new Scope(null), null, null) { Modifiers = DeclarationFragments.Public };

        if(implemented is not null)
        {
            conformer.BaseAbstractTypes.Add(implemented);
        }

        conformers.Add(conformer);
        return conformer;
    }

    private static Method Member(ClassType owner, string name, IMistType returns, params (string Name, IMistType Type)[] parameters)
    {
        var method = new Method(name, returns, [.. parameters.Select(parameter => new Parameter(parameter.Name, parameter.Type))], DeclarationFragments.Public)
        {
            DeclarationNode = HandBuiltModel.Node(20),
            DeclaringType = owner,
            Scope = new Scope(owner.Scope),
        };
        owner.MethodsList.Add(method);
        return method;
    }
}