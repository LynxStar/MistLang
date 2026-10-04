using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Containment into <c>unknown</c>, which is what decides whether an argument is APPLICABLE to an
/// <c>unknown</c> parameter. Order 81 calls unknown the type every non-void type is assignable to,
/// and the relation used to answer no for all of them because unknown is indeterminate and the
/// indeterminate bail ran first.
/// <para>
/// The cost of that was <c>Console.WriteLine(7)</c>: the stdlib declares WriteLine(String) and
/// WriteLine(unknown), so a non-string argument had two candidates of the same arity and neither
/// applicable. No sample caught it because every banked one routes output through interpolation, so
/// WriteLine is only ever called on a string.
/// </para>
/// </summary>
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

    /// <summary>
    /// Void has one inhabitant and no representation to widen, so it is excluded from unknown widening.
    /// </summary>
    [Fact]
    public void Void_IsNotWithinUnknown()
    {
        Assert.False(ConversionRelation.IsWithin(Named("Void"), UnknownType.Instance));
    }

    /// <summary>
    /// An error argument must not launder into a resolution. This is load-bearing rather than
    /// tidiness: samples/ExtensionMethods calls <c>Console.WriteLine(value.GetType())</c> where
    /// GetType does not resolve, and admitting the error type would turn one honest diagnostic into
    /// a call that binds and emits.
    /// </summary>
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

    /// <summary>
    /// Identity must keep excluding unknown even though applicability no longer does. The two ask
    /// different questions: applicability asks whether the argument fits, identity ranks candidates,
    /// and a type that admits everything ranks nothing.
    /// </summary>
    [Fact]
    public void Unknown_IsNotIdentityWithAValueType()
    {
        Assert.False(ConversionRelation.IsIdentity(Named("Int32"), UnknownType.Instance));
    }
}