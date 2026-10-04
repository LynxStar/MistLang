// Tier 1 (Unit): whether a value crossing between Mist's structural func and a CLR delegate of the
// same signature needs constructing in C#. Hand-built types, no pipeline. The two sides are ONE
// type to Mist, so nothing in the model or in a program's output distinguishes them -- a sample
// passing a func where a Predicate is declared fails at the C# compiler, which is a whole pipeline
// away from the predicate deciding it.

using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class DelegateConstructionLoweringTests
{
    private static readonly ClassType Arg = Declare("Arg", "Probe");

    private static readonly ClassType Result = Declare("Result", "Probe");

    private static readonly FunctionType Function = new([Arg], Result);

    [Fact]
    public void AFunctionEnteringANamedDelegateOfTheSameSignatureIsConstructed()
    {
        Assert.True(DelegateLowering.NeedsConstruction(Function, Delegate(Arg, Result)));
    }

    [Fact]
    public void ANamedDelegateEnteringTheFunctionItNamesIsConstructed()
    {
        Assert.True(DelegateLowering.NeedsConstruction(Delegate(Arg, Result), Function));
    }

    [Fact]
    public void ADifferentSignatureIsNotConstructed()
    {
        Assert.False(DelegateLowering.NeedsConstruction(Function, Delegate(Result, Result)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TwoTypesOnTheSameSideAreLeftAlone(bool nominal)
    {
        var source = nominal ? Delegate(Arg, Result) : (IMistType)Function;
        var target = nominal ? Delegate(Arg, Result) : (IMistType)Function;

        Assert.False(DelegateLowering.NeedsConstruction(source, target));
    }

    [Fact]
    public void AClassThatIsNotADelegateIsLeftAlone()
    {
        Assert.False(DelegateLowering.NeedsConstruction(Function, Declare("Plain", "Probe")));
    }

    private static ClassType Delegate(IMistType parameter, IMistType result)
    {
        var declared = Declare("Named", "Probe");
        declared.BaseType = Declare("MulticastDelegate", "System");

        declared.MethodsList.Add(new Method(
            "Invoke", result, [new Parameter("value", parameter)], new MemberModifiers()));

        return declared;
    }

    private static ClassType Declare(string name, string namespaceName)
    {
        return new ClassType(name, namespaceName, parentScope: null, declaringType: null, declarationNode: null);
    }
}