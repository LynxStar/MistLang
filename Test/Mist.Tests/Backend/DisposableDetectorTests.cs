using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class DisposableDetectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectImplementationMarksClassesAndStructs(bool valueType)
    {
        ConcreteType type = valueType
            ? new StructType("Resource", "Probe", null, null, null)
            : Class("Resource");
        type.BaseAbstractTypes.Add(Disposable());

        DisposableDetector.Apply(type);

        Assert.True(type.HasDrop);
        Assert.True(type.IsDisposableDrop);
    }

    [Fact]
    public void MultiLevelBaseAndInterfaceClosureIsDisposable()
    {
        var root = Class("Root");
        var middle = Class("Middle");
        var leaf = Class("Leaf");
        var inherited = new AbstractType("IResource", "Probe", null, null, null);
        inherited.BaseAbstractTypes.Add(Disposable());
        root.BaseAbstractTypes.Add(inherited);
        middle.BaseType = root;
        leaf.BaseType = middle;

        DisposableDetector.Apply(leaf);

        Assert.True(leaf.HasDrop);
        Assert.True(leaf.IsDisposableDrop);
        Assert.False(root.HasDrop);
    }

    [Fact]
    public void ConstructedBasesAndInterfacesFollowTheirDefinitions()
    {
        var generic = Class("Resource<>");
        var contract = new AbstractType("IResource<>", "Probe", null, null, null);
        contract.BaseAbstractTypes.Add(Disposable());
        generic.BaseAbstractTypes.Add(new ConstructedType(contract, [Class("Argument")]));
        var leaf = Class("Leaf");
        leaf.BaseType = new ConstructedType(generic, [Class("Argument")]);

        DisposableDetector.Apply(leaf);

        Assert.True(leaf.HasDrop);
        Assert.True(leaf.IsDisposableDrop);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateAndCyclicGraphsTerminate(bool disposable)
    {
        var type = Class("Resource");
        var contract = new AbstractType("ICycle", "Probe", null, null, null);
        type.BaseType = new ConstructedType(type, [Class("Argument")]);
        type.BaseAbstractTypes.AddRange([contract, contract]);
        contract.BaseAbstractTypes.Add(contract);
        if(disposable)
        {
            contract.BaseAbstractTypes.Add(Disposable());
        }

        DisposableDetector.Apply(type);

        Assert.Equal(disposable, type.HasDrop);
        Assert.Equal(disposable, type.IsDisposableDrop);
    }

    [Theory]
    [InlineData("IAsyncDisposable", "System")]
    [InlineData("IDisposable", "Other")]
    [InlineData("IResource", "Probe")]
    public void UnrelatedInterfacesDoNotInferDrop(string name, string ns)
    {
        var type = Class("Resource");
        type.BaseAbstractTypes.Add(new AbstractType(name, ns, null, null, null));

        DisposableDetector.Apply(type);

        Assert.False(type.HasDrop);
        Assert.False(type.IsDisposableDrop);
    }

    [Fact]
    public void RepresentedExplicitDropTakesPrecedence()
    {
        var type = Class("Resource");
        type.BaseAbstractTypes.Add(Disposable());
        type.MethodsList.Add(new Method("Drop", Class("Result"), [], new MemberModifiers { Accessibility = Accessibility.Public }));

        DisposableDetector.Apply(type);

        Assert.True(type.HasDrop);
        Assert.False(type.IsDisposableDrop);
    }

    [Fact]
    public void ExistingExplicitDropFlagIsPreserved()
    {
        var type = Class("Resource");
        type.HasDrop = true;
        type.BaseAbstractTypes.Add(Disposable());

        DisposableDetector.Apply(type);

        Assert.True(type.HasDrop);
        Assert.False(type.IsDisposableDrop);
    }

    [Fact]
    public void MistSourceImplementationDoesNotInferDrop()
    {
        var type = Class("Resource");
        type.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        type.BaseAbstractTypes.Add(Disposable());

        DisposableDetector.Apply(type);

        Assert.False(type.HasDrop);
        Assert.False(type.IsDisposableDrop);
    }

    private static ClassType Class(string name)
    {
        return new ClassType(name, "Probe", null, null, null);
    }

    private static AbstractType Disposable()
    {
        return new AbstractType("IDisposable", "System", null, null, null);
    }
}