using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class DefaultFactoryInheritanceEmissionTests
{
    [Fact]
    public void AnIntrinsicBaseHasNoGeneratedFactoryToHide()
    {
        var parent = HandBuiltModel.Class("Parent");
        parent.Modifiers = DeclarationFragments.Public with { IsIntrinsic = true };
        var child = HandBuiltModel.Class("Child", parent);

        Assert.DoesNotContain("public new static", Emit(child), StringComparison.Ordinal);
    }

    [Fact]
    public void AConstructedBaseFactoryUsesItsClosedWitnessTypesWhenCheckingHiding()
    {
        var shapes = new GenericDefaultShapes();
        var parent = shapes.Box(name: "Parent");
        var child = shapes.Type("Child");
        child.BaseType = Construct(parent, Parameter(child, "U"));

        Assert.Contains("public new static Child<U> __CreateDefault(System.Func<U> __default_U)", Emit(child), StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentWitnessSignaturesDoNotClaimToHideAnInheritedFactory()
    {
        var shapes = new GenericDefaultShapes();
        var child = shapes.Type("Child", baseType: Construct(shapes.Box(name: "Parent"), shapes.Integer));
        _ = Slot(child, "Value", Parameter(child, "T"));

        Assert.Contains("public static Child<T> __CreateDefault(System.Func<T> __default_T)", Emit(child), StringComparison.Ordinal);
        Assert.DoesNotContain("public new static", Emit(child), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEarlierAncestorFactoryIsComparedThroughTheFullConstructionPath()
    {
        var shapes = new GenericDefaultShapes();
        var grandparent = shapes.Box(name: "Grandparent");
        var parent = shapes.Type("Parent");
        parent.BaseType = Construct(grandparent, Parameter(parent, "T"));
        _ = Slot(parent, "Second", Parameter(parent, "U"));
        var child = shapes.Type("Child");
        var value = Parameter(child, "V");
        child.BaseType = Construct(parent, value, shapes.Integer);

        Assert.Contains("public new static Child<V> __CreateDefault(System.Func<V> __default_V)", Emit(child), StringComparison.Ordinal);
    }

    private static string Emit(ConcreteType type)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;
        DefaultEmitter.Emit(type, writer, diagnostics, ref anyEmitted);
        Assert.Empty(diagnostics.ToImmutableArray());
        return writer.ToString();
    }
}