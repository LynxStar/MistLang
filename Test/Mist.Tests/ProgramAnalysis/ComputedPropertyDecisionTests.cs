using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ComputedPropertyDecisionTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void AFieldReferenceInABodiedGetterAlone_IsRefused(bool blockBody, bool expressionBody, bool usesField)
    {
        var property = Computed(blockBody, expressionBody, usesField);
        Assert.True(ComputedPropertyDecision.IsComputed(property));
        Assert.True(ComputedPropertyDecision.RefusesField(property));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ABodiedGetterAloneThatNeverNamesField_IsAdmitted(bool blockBody, bool expressionBody)
    {
        var property = Computed(blockBody, expressionBody, usesField: false);
        Assert.True(ComputedPropertyDecision.IsComputed(property));
        Assert.False(ComputedPropertyDecision.RefusesField(property));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ABodiedGetterBesideASetter_IsAdmittedWithField(bool autoSetter, bool init)
    {
        var property = Property(new PropertyAccessor { Body = HandBuiltModel.Block() },
            new PropertyAccessor { IsAuto = autoSetter, IsInit = init, Body = autoSetter ? null : HandBuiltModel.Block() }, usesField: true);
        Assert.False(ComputedPropertyDecision.IsComputed(property));
        Assert.False(ComputedPropertyDecision.RefusesField(property));
    }

    [Fact]
    public void AnAutoGetterBesideABodiedSetterNamingField_IsAdmitted()
    {
        var property = Property(new PropertyAccessor { IsAuto = true }, new PropertyAccessor { Body = HandBuiltModel.Block() }, usesField: true);
        Assert.False(ComputedPropertyDecision.RefusesField(property));
    }

    [Fact]
    public void AnAutoGetterAlone_IsNotComputed()
    {
        var property = Property(new PropertyAccessor { IsAuto = true }, null, usesField: false);
        Assert.False(ComputedPropertyDecision.IsComputed(property));
    }

    private static Property Computed(bool blockBody, bool expressionBody, bool usesField)
    {
        var getter = new PropertyAccessor
        {
            Body = blockBody ? HandBuiltModel.Block() : null,
            ExpressionBody = expressionBody ? HandBuiltModel.Literal(new HandBuiltModel().Type("Int32")) : null,
        };
        return Property(getter, null, usesField);
    }

    private static Property Property(PropertyAccessor? getter, PropertyAccessor? setter, bool usesField)
    {
        return new Property("Value", new HandBuiltModel().Type("Int32"), DeclarationFragments.Public)
        {
            Getter = getter,
            Setter = setter,
            UsesBackingField = usesField,
        };
    }
}