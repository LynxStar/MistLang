using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class FactoryHidingEmissionTests
{
    [Fact]
    public void ParameterlessClassFactoryExplicitlyHidesTheInheritedFactory()
    {
        var type = HandBuiltModel.Class("Child", HandBuiltModel.Class("Parent"));

        Assert.Contains("public new static Child __CreateDefault()", Emit(type), StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryWithoutAClassAncestorDoesNotClaimToHideOne()
    {
        Assert.Contains("public static Root __CreateDefault()", Emit(HandBuiltModel.Class("Root")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ambient")]
    [InlineData("nondefaultable")]
    public void AncestorWithoutAnEmittedDefaultFactoryDoesNotAddNew(string shape)
    {
        var parent = HandBuiltModel.Class("Parent");

        if(shape == "ambient")
        {
            parent.Assembly = MistAssembly.Ambient;
        }
        else
        {
            parent.ConstructorsList.Add(new Constructor(parent.Name, parent, [], DeclarationFragments.Public)
            {
                DeclaringType = parent,
            });
        }

        var type = HandBuiltModel.Class("Child", parent);

        Assert.Contains("public static Child __CreateDefault()", Emit(type), StringComparison.Ordinal);
    }

    [Fact]
    public void FlattenedStructFactoryDoesNotHideItsMistAncestorsFactory()
    {
        var type = HandBuiltModel.Struct("Child");
        type.BaseType = HandBuiltModel.Struct("Parent");

        Assert.Contains("public static Child __CreateDefault()", Emit(type), StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedFactoryWithAWitnessDoesNotHideAParameterlessBaseFactory()
    {
        var type = HandBuiltModel.Class("Child", HandBuiltModel.Class("Parent"));
        _ = ZeroedParameter(type);

        Assert.Contains("public static Child<T> __CreateDefault(System.Func<T> __default_T)", Emit(type), StringComparison.Ordinal);
    }

    [Fact]
    public void DerivedFactoryWithAConstructorWitnessDoesNotHideAParameterlessBaseFactory()
    {
        var type = HandBuiltModel.Class("Child", HandBuiltModel.Class("Parent"));
        var parameter = DeclarationFragments.Parameter(type, type.Scope, "T");
        parameter.ConstraintsList.Add(new ConstructorConstraint([Integer()]));

        Assert.Contains("public static Child<T> __CreateDefault(System.Func<int, T> __construct_T_0)", Emit(type), StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterlessDerivedFactoryDoesNotHideABaseFactoryWithAWitness()
    {
        var parent = HandBuiltModel.Class("Parent");
        _ = ZeroedParameter(parent);
        var type = HandBuiltModel.Class("Child", new ConstructedType(parent, [Integer()]));

        Assert.Contains("public static Child __CreateDefault()", Emit(type), StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterlessFactoryFindsItsMatchingAncestorThroughAWitnessFactory()
    {
        var parent = HandBuiltModel.Class("Parent", HandBuiltModel.Class("Ancestor"));
        _ = ZeroedParameter(parent);
        var type = HandBuiltModel.Class("Child", new ConstructedType(parent, [Integer()]));

        Assert.Contains("public new static Child __CreateDefault()", Emit(type), StringComparison.Ordinal);
    }

    private static TypeParameter ZeroedParameter(ClassType owner)
    {
        var parameter = DeclarationFragments.Parameter(owner, owner.Scope, "T");
        owner.FieldsList.Add(new Field("Value", parameter, OwnershipKind.Unrestricted, DeclarationFragments.Public));
        return parameter;
    }

    private static ClassType Integer()
    {
        return new ClassType("Int32", "Standard", null, null, null)
        {
            Modifiers = DeclarationFragments.Public with { IsIntrinsic = true },
        };
    }

    private static string Emit(ConcreteType type)
    {
        var writer = new CSharpWriter();
        var anyEmitted = false;
        DefaultEmitter.Emit(type, writer, new DiagnosticBag(), ref anyEmitted);
        return writer.ToString();
    }
}