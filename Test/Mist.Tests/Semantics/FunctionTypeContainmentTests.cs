// Tier 1 (Unit): what ConversionRelation says about a structural function type. Hand-built types, no
// pipeline -- the rule is keyed on a CLR name, so naming that type directly is the whole of the setup.
//
// Asserted here rather than through a sample because a LONE candidate is selected without the relation
// being consulted at all, so a program calling a single delegate-taking method runs either way. Only a
// call with a second candidate reaches this, and order 109 of
// backlog/emitter-v2-capability-progression.yaml records where that was measured.

using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

public sealed class FunctionTypeContainmentTests
{
    private static readonly ClassType Marker = Declare("Marker", "Probe");

    private static readonly FunctionType Function = new([Marker], Marker);

    [Theory]
    [InlineData("Delegate")]
    [InlineData("MulticastDelegate")]
    public void AFunctionTypeIsContainedByTheClrDelegateRoot(string rootName)
    {
        Assert.True(ConversionRelation.IsWithin(Function, Declare(rootName, "System")));
    }

    [Fact]
    public void TheDelegateRootIsNotContainedByAFunctionType()
    {
        Assert.False(ConversionRelation.IsWithin(Declare("Delegate", "System"), Function));
    }

    [Fact]
    public void AClassMerelyNamedDelegateDoesNotContainAFunctionType()
    {
        Assert.False(ConversionRelation.IsWithin(Function, Declare("Delegate", "Probe")));
    }

    private static ClassType Declare(string name, string namespaceName)
    {
        return new ClassType(name, namespaceName, parentScope: null, declaringType: null, declarationNode: null);
    }
}