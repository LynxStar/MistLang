using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Asserted on the projected signature rather than a diagnostic, because v2 runs no analyzers and a
/// plain dot on one of these fails as an ordinary member-lookup miss, whatever built the union.
/// </summary>
[Trait("Tier", "2")]
public sealed class ClrInboundOptionalProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    [Fact]
    public void ANullableClrReferenceReturn_ProjectsAsAUnionWithVoid()
    {
        var union = Assert.IsAssignableFrom<IUnionType>(Method(nameof(HasNullableMembers.GetNullableString)).ReturnType);

        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.Void");
        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.String");
    }

    /// <summary>
    /// Wrapping a result the CLR promises is never null makes a caller narrow something that cannot be
    /// absent, so a chained `xs.Skip(5).First()` would not resolve.
    /// </summary>
    [Fact]
    public void ANonNullClrReferenceReturn_IsNotWrapped()
    {
        var returnType = Method(nameof(HasNullableMembers.ValueOrFallback)).ReturnType;

        Assert.False(
            returnType is IUnionType,
            $"expected a plain reference type, projected as '{returnType.FullyQualifiedName}'");
    }

    [Fact]
    public void AClrValueTypeReturn_IsNotWrapped()
    {
        var returnType = Method(nameof(HasNullableMembers.GetPlainInt)).ReturnType;

        Assert.False(
            returnType is IUnionType,
            $"expected a plain value type, projected as '{returnType.FullyQualifiedName}'");
    }

    /// <summary>
    /// The invariant the generic half of this rests on. A result whose type is a type PARAMETER
    /// cannot be wrapped here -- List's element would take the wrap that List of Int32 must not
    /// have -- so the decision moves to substitution, and this flag is the only thing carrying
    /// "the CLR could have put a null here" to it.
    /// </summary>
    [Fact]
    public void AProjectedMember_IsMarkedAsClrProjected()
    {
        Assert.True(Method(nameof(HasNullableMembers.ValueOrFallback)).Modifiers.IsClrProjected);
    }

    /// <summary>
    /// The outbound half. <c>ValueOrFallback</c> takes a <c>string?</c>; Mist has no null to hand it,
    /// so a union there would only give overload resolution more candidates to be ambiguous between.
    /// </summary>
    [Fact]
    public void AClrReferenceParameter_IsNotWrapped()
    {
        var fallback = Method(nameof(HasNullableMembers.ValueOrFallback));

        Assert.All(
            fallback.ParametersList,
            parameter => Assert.False(
                parameter.Type is IUnionType,
                $"parameter '{parameter.Name}' projected as '{parameter.Type.FullyQualifiedName}'"));
    }

    private Method Method(string name)
    {
        return fixture.Type(nameof(HasNullableMembers)).MethodsList.Single(method => method.Name == name);
    }
}