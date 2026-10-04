// Tier 1 (Unit): how a constructed generic names itself. Definition and arguments are constructed
// directly; nothing upstream runs.
//
// This has no observable effect on emitted output today, which is exactly why it needs a test of its
// own. TypeNameEmitter and TypeDisplay both build their own spelling and strip the arity marker
// independently, and the only other consumer -- the overload shadowing key in CandidateSet -- only
// ever compares one of these names against another, so any format at all is self-consistent there.
// A wrong name is therefore invisible until something new reports it, and by then the cause is a
// long way from the symptom.

using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

public sealed class ConstructedTypeNamingTests
{
    /// <summary>
    /// A generic definition is REGISTERED under an arity-bearing name (<c>Box&lt;&gt;</c>), so the
    /// marker has to be replaced by the arguments rather than followed by them.
    /// </summary>
    [Fact]
    public void AConstructedGeneric_IsNamedWithItsArgumentsInPlaceOfTheArityMarker()
    {
        var constructed = new ConstructedType(Definition("Box<>"), [Definition("Int32", "Standard")]);

        Assert.Equal("Box<Int32>", constructed.Name);
    }

    [Fact]
    public void AConstructedGenericOfHigherArity_IsNamedWithEveryArgument()
    {
        var constructed = new ConstructedType(
            Definition("Map<,>"),
            [Definition("String", "Standard"), Definition("Int32", "Standard")]);

        Assert.Equal("Map<String, Int32>", constructed.Name);
    }

    [Fact]
    public void ANestedConstruction_IsNamedThroughItsArgument()
    {
        var inner = new ConstructedType(Definition("Box<>"), [Definition("Int32", "Standard")]);
        var outer = new ConstructedType(Definition("Box<>"), [inner]);

        Assert.Equal("Box<Box<Int32>>", outer.Name);
    }

    /// <summary>
    /// The qualified name carries the definition's namespace, which is what distinguishes two
    /// <c>Box&lt;int&gt;</c> constructed from different namespaces' definitions.
    /// </summary>
    [Fact]
    public void AConstructedGeneric_IsQualifiedByItsDefinitionsNamespace()
    {
        var constructed = new ConstructedType(Definition("Box<>", "Probe"), [Definition("Int32", "Standard")]);

        Assert.Equal("Probe.Box<Int32>", constructed.FullyQualifiedName);
    }

    /// <summary>
    /// A nested type reached through an instantiation is named under it, with no argument list of its
    /// own to write -- <c>Box&lt;Int32&gt;.Wrapper</c>, never <c>Wrapper&lt;&gt;</c>.
    /// </summary>
    [Fact]
    public void ANestedTypeThroughAnInstantiation_IsNamedUnderIt()
    {
        var box = Definition("Box<>", "Probe");
        var wrapper = new ClassType("Wrapper", "Probe", new Scope(null), box, null!);
        var nested = new ConstructedType(wrapper, [], new ConstructedType(box, [Definition("Int32", "Standard")]));

        Assert.Equal("Box<Int32>.Wrapper", nested.Name);
        Assert.Equal("Probe.Box<Int32>.Wrapper", nested.FullyQualifiedName);
    }

    /// <summary>
    /// A dependent value names itself by its digits whichever box holds it, so ulong's upper half and the
    /// long it would wrap to are two names as they are two types.
    /// </summary>
    [Fact]
    [Trait("Tier", "1")]
    public void AConstructionOverADependentValue_IsNamedByTheValuesDigits()
    {
        var int32 = Definition("Int32", "Standard");
        var top = new ConstructedType(Definition("Mask<>"), [new LiftedType(int32, ulong.MaxValue)]);
        var wrapped = new ConstructedType(Definition("Mask<>"), [new LiftedType(int32, -1L)]);

        Assert.Equal("Mask<18446744073709551615>", top.Name);
        Assert.Equal("Mask<-1>", wrapped.Name);
    }

    /// <summary>
    /// A non-generic definition has no marker to strip, so its name passes through whole.
    /// </summary>
    [Fact]
    public void ADefinitionWithNoArityMarker_KeepsItsWholeName()
    {
        Assert.Equal("Box", MistType.StripArity("Box"));
    }

    private static ClassType Definition(string name, string namespaceName = "Probe")
    {
        return new ClassType(name, namespaceName, new Scope(null), null, null!);
    }
}