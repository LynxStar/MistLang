using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ClrObjectContainmentTests
{
    private static readonly ClassType ClrObject = Class("Object", "System");

    private static readonly StructType Int32 = new("Int32", "Standard", null, null, null);

    private static readonly StructType Void = new("Void", "Standard", null, null, null);

    private static readonly ClassType Marker = Class("Marker", "Probe");

    [Theory]
    [InlineData("int")]
    [InlineData("class")]
    [InlineData("tuple")]
    [InlineData("optional")]
    [InlineData("union")]
    [InlineData("type parameter")]
    [InlineData("function")]
    public void EveryValueIsWithinTheClrRoot(string value)
    {
        Assert.True(ConversionRelation.IsWithin(Value(value), ClrObject));
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(Value(value), ClrObject));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("class")]
    [InlineData("tuple")]
    [InlineData("optional")]
    [InlineData("union")]
    [InlineData("type parameter")]
    [InlineData("function")]
    public void EveryValueIsWithinUnknownAlike(string value)
    {
        Assert.True(ConversionRelation.IsWithin(Value(value), UnknownType.Instance));
    }

    [Fact]
    public void AnOptionalClrRootAcceptsAPresentValue()
    {
        Assert.True(ConversionRelation.IsWithin(Int32, new UnionType([ClrObject, Void])));
    }

    [Fact]
    public void NothingNarrowerIsReachedFromTheClrRoot()
    {
        Assert.False(ConversionRelation.IsWithin(ClrObject, Int32));
        Assert.False(ConversionRelation.IsWithin(ClrObject, Marker));
        Assert.NotEqual(ConversionKind.Implicit, ConversionClassifier.Classify(ClrObject, Int32));
    }

    [Fact]
    public void TheClrRootAndUnknownStayDistinctTypes()
    {
        Assert.False(ConversionRelation.IsIdentity(ClrObject, UnknownType.Instance));
    }

    [Fact]
    public void WhatIsNotAValueIsNotWithinTheClrRoot()
    {
        Assert.False(ConversionRelation.IsWithin(Void, ClrObject));
        Assert.False(ConversionRelation.IsWithin(ErrorType.Instance, ClrObject));
    }

    [Fact]
    public void AnUnrelatedProjectedClassStaysNominal()
    {
        Assert.False(ConversionRelation.IsWithin(Int32, Class("Uri", "System")));
        Assert.False(ConversionRelation.IsWithin(Marker, Class("Uri", "System")));
    }

    [Fact]
    public void AClassMerelyNamedObjectIsNotTheClrRoot()
    {
        Assert.False(ConversionRelation.IsWithin(Int32, Class("Object", "Probe")));
    }

    [Fact]
    public void ASpecificTargetBeatsTheClrRootForTheSameArgument()
    {
        Assert.True(OverloadResolution.CompareConversions(Int32, Int32, ClrObject) < 0);
        Assert.True(OverloadResolution.CompareConversions(Marker, ClrObject, Marker) > 0);
    }

    private static IMistType Value(string kind)
    {
        return kind switch
        {
            "int" => Int32,
            "class" => Marker,
            "tuple" => new TupleType([Int32, Marker]),
            "optional" => new UnionType([Int32, Void]),
            "union" => new UnionType([Int32, Marker]),
            "type parameter" => new TypeParameter("T", 0, new Scope(null), Marker),
            "function" => new FunctionType([Marker], Int32),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static ClassType Class(string name, string namespaceName)
    {
        return new ClassType(name, namespaceName, parentScope: null, declaringType: null, declarationNode: null);
    }
}