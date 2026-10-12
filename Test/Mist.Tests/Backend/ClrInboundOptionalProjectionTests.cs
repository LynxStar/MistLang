using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// The v2 half of the CLR null boundary: a reference-typed result the CLR says may be null projects
/// as <c>T | void</c>, and one it declares non-null does not.
/// <para>
/// ASSERTED ON THE PROJECTED SIGNATURE rather than on a diagnostic, because v2 runs no analyzers.
/// The refusal a user sees for a plain dot on one of these is an ordinary member-lookup miss, which
/// would read the same if the union had been built for an unrelated reason -- only the projected
/// type says the wrap happened and says which constituent it added.
/// </para>
/// </summary>
[Trait("Tier", "2")]
public sealed class ClrInboundOptionalProjectionTests(AmbientTypeProviderV2InteropFixture fixture)
    : IClassFixture<AmbientTypeProviderV2InteropFixture>
{
    /// <summary>
    /// <c>GetNullableString</c> is annotated <c>string?</c>, which is the only reason this one wraps --
    /// <c>ValueOrFallback</c> declares its result non-null.
    /// </summary>
    [Fact]
    public void ANullableClrReferenceReturn_ProjectsAsAUnionWithVoid()
    {
        var union = Assert.IsAssignableFrom<IUnionType>(Method(nameof(HasNullableMembers.GetNullableString)).ReturnType);

        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.Void");
        Assert.Contains(union.Constituents, c => c.FullyQualifiedName == "Standard.String");
    }

    /// <summary>
    /// The other direction, and the one that makes the rule worth having. Wrapping a result the CLR
    /// PROMISES is never null makes a caller narrow something that cannot be absent -- which is what
    /// stopped a chained `xs.Skip(5).First()` resolving at all.
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