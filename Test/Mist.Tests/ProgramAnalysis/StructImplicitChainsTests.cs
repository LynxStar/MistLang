using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class StructImplicitChainsTests
{
    private static readonly MemberModifiers Public = DeclarationFragments.Public;

    [Theory]
    [InlineData("parameterless")]
    [InlineData("params")]
    public void CallableBaseConstructor_IsTheCanonicalImplicitTarget(string shape)
    {
        var meter = HandBuiltModel.Struct("Meter");
        var parameter = new Parameter("readings", new HandBuiltModel().Type("Int32")) { IsFlattened = true };
        var selected = new Constructor("Meter", meter, shape == "params" ? [parameter] : [], Public)
        {
            DeclaringType = meter,
        };
        meter.ConstructorsList.Add(selected);
        var dial = HandBuiltModel.Struct("Dial");
        dial.BaseType = meter;
        var constructor = Add(dial);

        Assert.Same(selected, ImplicitChains.Of(dial));
        Assert.Null(ImplicitChains.UnsatisfiedStructBase(constructor));
    }

    [Fact]
    public void ConstructorlessBase_IsAnImplicitTargetWithoutAnInitializer()
    {
        var meter = HandBuiltModel.Struct("Meter");
        var dial = HandBuiltModel.Struct("Dial");
        dial.BaseType = meter;
        var constructor = Add(dial);

        Assert.Same(meter, ImplicitChains.Of(dial));
        Assert.Null(ImplicitChains.UnsatisfiedStructBase(constructor));
    }

    [Theory]
    [InlineData(Accessibility.Public, false)]
    [InlineData(Accessibility.Private, true)]
    public void BaseWithoutACallableZeroArgumentConstructor_IsUnsatisfied(Accessibility access, bool parameterless)
    {
        var meter = HandBuiltModel.Struct("Meter");
        var parameters = parameterless ? new List<Parameter>() : [new Parameter("reading", new HandBuiltModel().Type("Int32"))];
        meter.ConstructorsList.Add(new Constructor("Meter", meter, parameters, Public with { Accessibility = access })
        {
            DeclaringType = meter,
        });
        var dial = HandBuiltModel.Struct("Dial");
        dial.BaseType = meter;
        var constructor = Add(dial);

        Assert.Null(ImplicitChains.Of(dial));
        Assert.Same(meter, ImplicitChains.UnsatisfiedStructBase(constructor));
    }

    [Theory]
    [InlineData(ConstructorChainKind.Base, false)]
    [InlineData(ConstructorChainKind.This, false)]
    [InlineData(ConstructorChainKind.None, true)]
    public void ExplicitChainsAndStaticConstructor_DoNotRequireAnImplicitTarget(ConstructorChainKind chain, bool isStatic)
    {
        var meter = HandBuiltModel.Struct("Meter");
        meter.ConstructorsList.Add(new Constructor("Meter", meter, [new Parameter("reading", new HandBuiltModel().Type("Int32"))], Public)
        {
            DeclaringType = meter,
        });
        var dial = HandBuiltModel.Struct("Dial");
        dial.BaseType = meter;
        var constructor = Add(dial, chain, isStatic);

        Assert.Null(ImplicitChains.UnsatisfiedStructBase(constructor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedGenericBase_KeepsTheSelectedDefinitionAndWrittenBaseType(bool requiresArgument)
    {
        var built = new HandBuiltModel();
        var meter = new StructType("Meter<>", "Probe", new Scope(null), null, null);
        var parameter = new TypeParameter("T", 0, meter.Scope, meter);
        meter.TypeParametersList.Add(parameter);
        var selected = new Constructor("Meter", meter,
            requiresArgument ? [new Parameter("reading", parameter)] : [], Public)
        { DeclaringType = meter };
        meter.ConstructorsList.Add(selected);
        var dial = HandBuiltModel.Struct("Dial");
        var constructed = built.Interner.GetOrCreateConstructedType(meter, [built.Type("Int32")]);
        dial.BaseType = constructed;
        var constructor = Add(dial);

        if(requiresArgument)
        {
            Assert.Null(ImplicitChains.Of(dial));
            Assert.Same(constructed, ImplicitChains.UnsatisfiedStructBase(constructor));
        }
        else
        {
            Assert.Same(selected, ImplicitChains.Of(dial));
            Assert.Null(ImplicitChains.UnsatisfiedStructBase(constructor));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ConstructedPackBase_IsAnImplicitTargetOnlyWhereTheBaseClauseClosesThePackEmpty(int width)
    {
        var built = new HandBuiltModel();
        var meter = new StructType("Meter<>", "Probe", new Scope(null), null, null);
        var pack = new TypeParameter("Ts", 0, meter.Scope, meter) { PackKind = TypePackKind.Unbracketed };
        meter.TypeParametersList.Add(pack);
        var selected = new Constructor("Meter", meter, [new Parameter("readings", new TupleType([new SpreadType(pack)])) { IsFlattened = true }], Public)
        {
            DeclaringType = meter,
        };
        meter.ConstructorsList.Add(selected);
        var dial = HandBuiltModel.Struct("Dial");
        var elements = Enumerable.Repeat<IMistType>(built.Type("Int32"), width).ToList();
        var constructed = built.Interner.GetOrCreateConstructedType(meter, [new LiftedType(built.Type("Type"), new TypePackValue(elements))]);
        dial.BaseType = constructed;
        var constructor = Add(dial);

        if(width == 0)
        {
            Assert.Same(selected, ImplicitChains.Of(dial));
            Assert.Null(ImplicitChains.UnsatisfiedStructBase(constructor));
        }
        else
        {
            Assert.Null(ImplicitChains.Of(dial));
            Assert.Same(constructed, ImplicitChains.UnsatisfiedStructBase(constructor));
        }
    }

    private static Constructor Add(StructType owner, ConstructorChainKind chain = ConstructorChainKind.None, bool isStatic = false)
    {
        var constructor = new Constructor(owner.Name, owner, [], Public with { IsStatic = isStatic })
        {
            DeclaringType = owner,
            ChainKind = chain,
        };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }
}