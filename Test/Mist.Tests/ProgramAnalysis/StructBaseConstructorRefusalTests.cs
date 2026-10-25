using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class StructBaseConstructorRefusalTests
{
    [Theory]
    [InlineData("argument-taking")]
    [InlineData("private-parameterless")]
    public void UnchainedDerivedConstructor_ReportsOneStructDiagnosticAtItsDeclaration(string shape)
    {
        var fragment = new DeclarationFragments();
        var meter = Base(fragment, shape);
        var dial = Struct(fragment, "Dial");
        dial.BaseType = meter;
        _ = Declare(dial, ConstructorChainKind.None, 60);

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));

        Assert.Equal("MIST0147", finding.Number.Value);
        Assert.Equal(61, finding.Position().Start.Column);
        Assert.Equal("Struct 'Meter' requires constructor arguments. Add ': base(...)' to the constructor of 'Dial'.", finding.Message);
    }

    [Theory]
    [InlineData("constructorless")]
    [InlineData("parameterless")]
    [InlineData("params-only")]
    public void CallableWithNothingOrConstructorlessBase_IsAdmitted(string shape)
    {
        var fragment = new DeclarationFragments();
        var dial = Struct(fragment, "Dial");
        dial.BaseType = Base(fragment, shape);
        _ = Declare(dial, ConstructorChainKind.None, 60);

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    [Fact]
    public void ExplicitChainsAndStaticConstructor_AreAdmitted()
    {
        var fragment = new DeclarationFragments();
        var dial = Struct(fragment, "Dial");
        var meter = Base(fragment, "argument-taking");
        dial.BaseType = meter;
        var chainedToBase = Declare(dial, ConstructorChainKind.Base, 40);
        chainedToBase.ChainedConstructor = meter.ConstructorsList[0];
        chainedToBase.ChainArgumentsList.Add(HandBuiltModel.Literal(fragment.Built.Type("Int32"), 7L));
        var chainedToThis = Declare(dial, ConstructorChainKind.This, 50, parameterCount: 1);
        chainedToThis.ChainedConstructor = chainedToBase;
        _ = Declare(dial, ConstructorChainKind.None, 60, parameterCount: 2, isStatic: true);

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    private static StructType Struct(DeclarationFragments fragment, string name)
    {
        var type = HandBuiltModel.Struct(name);
        fragment.Types.Add(type);
        return type;
    }

    private static StructType Base(DeclarationFragments fragment, string shape)
    {
        var meter = Struct(fragment, "Meter");
        var integer = fragment.Built.Type("Int32");

        if(shape == "argument-taking")
        {
            meter.ConstructorsList.Add(new Constructor("Meter", meter, [new Parameter("reading", integer)], DeclarationFragments.Public)
            {
                DeclaringType = meter,
            });
        }
        else if(shape == "private-parameterless")
        {
            meter.ConstructorsList.Add(new Constructor("Meter", meter, [], DeclarationFragments.Public with { Accessibility = Accessibility.Private })
            {
                DeclaringType = meter,
            });
        }
        else if(shape is "parameterless" or "params-only")
        {
            var parameters = shape == "parameterless" ? new List<Parameter>() : [new Parameter("readings", integer) { IsFlattened = true }];
            meter.ConstructorsList.Add(new Constructor("Meter", meter, parameters, DeclarationFragments.Public)
            {
                DeclaringType = meter,
            });
        }

        return meter;
    }

    private static Constructor Declare(StructType owner, ConstructorChainKind chain, int offset, int parameterCount = 0, bool isStatic = false)
    {
        var integer = new HandBuiltModel().Type("Int32");
        var parameters = Enumerable.Range(0, parameterCount).Select(index => new Parameter($"p{index}", integer)).ToList();
        var constructor = new Constructor(owner.Name, owner, parameters, DeclarationFragments.Public with { IsStatic = isStatic })
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(offset),
            ChainKind = chain,
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }
}