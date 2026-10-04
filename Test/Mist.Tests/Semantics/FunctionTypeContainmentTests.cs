// Tier 1 (Unit): ConversionRelation for a structural function type, with hand-built types and no
// pipeline. The rule is keyed on a CLR name, so naming the type is the setup. A lone call candidate
// is selected without consulting the relation; competing candidates exercise the containment
// distinction.

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