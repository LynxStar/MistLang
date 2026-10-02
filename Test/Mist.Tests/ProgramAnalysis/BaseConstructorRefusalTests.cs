using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class BaseConstructorRefusalTests
{
    [Theory]
    [InlineData("argument-taking")]
    [InlineData("private-parameterless")]
    public void UnchainedConstructorOverABaseItCannotCallWithNothing_IsRefusedOnceAtTheConstructor(string parent)
    {
        var fragment = new DeclarationFragments();
        var meter = Base(fragment, parent);
        var dial = fragment.Class("Dial");
        dial.BaseType = meter;
        Declare(dial, ConstructorChainKind.None, 60);

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));

        Assert.Equal("MIST_PA_BASE_CTOR_UNSATISFIED", finding.Id.Value);
        Assert.Equal(61, finding.Location.Start.Column);
        Assert.Equal("Base class 'Meter' requires constructor arguments that are not provided.", finding.Message);
    }

    [Theory]
    [InlineData("parameterless")]
    [InlineData("params-only")]
    [InlineData("constructorless")]
    public void UnchainedConstructorOverABaseItCanCallWithNothing_IsAdmitted(string parent)
    {
        var fragment = new DeclarationFragments();
        var dial = fragment.Class("Dial");
        dial.BaseType = Base(fragment, parent);
        Declare(dial, ConstructorChainKind.None, 60);

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    [Fact]
    public void OnlyTheConstructorsThatWriteNoChain_AreRefused()
    {
        var fragment = new DeclarationFragments();
        var dial = fragment.Class("Dial");
        dial.BaseType = Base(fragment, "argument-taking");
        Declare(dial, ConstructorChainKind.Base, 40, fragment.Built.Type("Int32"));
        Declare(dial, ConstructorChainKind.This, 50);
        Declare(dial, ConstructorChainKind.None, 70, fragment.Built.Type("String"));

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));

        Assert.Equal(71, finding.Location.Start.Column);
    }

    [Fact]
    public void DerivedClassWithNoConstructor_IsLeftToDefaultability()
    {
        var fragment = new DeclarationFragments();
        var dial = fragment.Class("Dial");
        dial.BaseType = Base(fragment, "argument-taking");

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
        Assert.Equal("MIST_PA_BASE_NOT_DEFAULTABLE", Assert.Single(fragment.Run(new DefaultabilityAnalyzer())).Id.Value);
    }

    [Fact]
    public void UnchainedConstructorOverAConstructedGenericBase_NamesTheBaseAsWritten()
    {
        var fragment = new DeclarationFragments();
        var container = Base(fragment, "argument-taking", "BaseContainer");
        var derived = fragment.Class("DerivedContainer");
        var parameter = DeclarationFragments.Parameter(derived, derived.Scope, "T");
        derived.BaseType = fragment.Built.Interner.GetOrCreateConstructedType(container, [parameter]);
        Declare(derived, ConstructorChainKind.None, 60);

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));

        Assert.Contains("Base class 'BaseContainer<T>'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnchainedConstructorOverAnAmbientBase_IsLeftToRoslyn()
    {
        var fragment = new DeclarationFragments();
        var attribute = Base(fragment, "argument-taking", "Attribute");
        attribute.Assembly = MistAssembly.Ambient;
        var tag = fragment.Class("ParamTagAttribute");
        tag.BaseType = attribute;
        Declare(tag, ConstructorChainKind.None, 60, fragment.Built.Type("String"));

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    private static ClassType Base(DeclarationFragments fragment, string shape, string name = "Meter")
    {
        var meter = fragment.Class(name);
        var integer = fragment.Built.Type("Int32");

        if(shape is "argument-taking" or "private-parameterless")
        {
            Declare(meter, ConstructorChainKind.None, 20, integer);
        }

        if(shape is "parameterless" or "private-parameterless")
        {
            var access = shape == "parameterless" ? Accessibility.Public : Accessibility.Private;
            meter.ConstructorsList.Add(new Constructor(name, meter, [], DeclarationFragments.Public with { Accessibility = access })
            {
                DeclaringType = meter,
                DeclarationNode = HandBuiltModel.Node(30),
            });
        }

        if(shape == "params-only")
        {
            meter.ConstructorsList.Add(new Constructor(name, meter, [new Parameter("readings", integer) { IsFlattened = true }], DeclarationFragments.Public)
            {
                DeclaringType = meter,
                DeclarationNode = HandBuiltModel.Node(30),
            });
        }

        return meter;
    }

    private static void Declare(ConcreteType owner, ConstructorChainKind chain, int offset, params ClassType[] parameters)
    {
        owner.ConstructorsList.Add(new Constructor(
            owner.Name, owner, [.. parameters.Select((type, i) => new Parameter($"p{i}", type))], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(offset),
            ChainKind = chain,
        });
    }
}