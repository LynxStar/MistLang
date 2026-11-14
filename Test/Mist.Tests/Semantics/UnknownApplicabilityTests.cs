using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

public sealed class UnknownApplicabilityTests
{
    private static ClassType Named(string name, string ns = "Standard")
    {
        return new ClassType(name, ns, new Scope(null), null, null);
    }

    [Fact]
    public void ValueType_IsWithinUnknown()
    {
        Assert.True(ConversionRelation.IsWithin(Named("Int32"), UnknownType.Instance));
    }

    [Fact]
    public void UserType_IsWithinUnknown()
    {
        Assert.True(ConversionRelation.IsWithin(Named("Greeting", "App"), UnknownType.Instance));
    }

    [Fact]
    public void Void_IsNotWithinUnknown()
    {
        Assert.False(ConversionRelation.IsWithin(Named("Void"), UnknownType.Instance));
    }

    [Fact]
    public void ErrorType_IsNotWithinUnknown()
    {
        Assert.False(ConversionRelation.IsWithin(ErrorType.Instance, UnknownType.Instance));
    }

    /// <summary>
    /// The direction is one-way. Unknown admits everything, so it discriminates nothing; letting it
    /// flow the other way would make every parameter accept an unknown argument.
    /// </summary>
    [Fact]
    public void Unknown_IsNotWithinAValueType()
    {
        Assert.False(ConversionRelation.IsWithin(UnknownType.Instance, Named("Int32")));
    }

    [Fact]
    public void Unknown_IsNotIdentityWithAValueType()
    {
        Assert.False(ConversionRelation.IsIdentity(Named("Int32"), UnknownType.Instance));
    }
}