using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Thin on purpose: what a parameter IS is decided upstream, where the evidence is, and
/// AmbientTypeProviderV2AttributeProjectionTests drives that projector over real metadata.
/// </summary>
public sealed class ArgumentKeywordsTests
{
    private static Parameter Param(ByRefDirection direction)
    {
        var isRef = direction != ByRefDirection.None;

        return new Parameter(
            "p",
            new ClassType("Int32", "Standard", new Scope(null), null, null),
            new ParameterModifier(
                direction == ByRefDirection.In, isRef, OwnershipKind.Unrestricted, direction));
    }

    [Theory]
    [InlineData(ByRefDirection.None, "")]
    [InlineData(ByRefDirection.Ref, "ref ")]
    [InlineData(ByRefDirection.In, "in ")]
    [InlineData(ByRefDirection.Out, "out ")]
    public void EachDirectionTakesItsKeyword(ByRefDirection direction, string expected)
    {
        Assert.Equal(expected, ArgumentKeywords.For(Param(direction)));
    }
}