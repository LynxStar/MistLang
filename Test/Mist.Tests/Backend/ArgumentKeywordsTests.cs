using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// The keyword an argument takes, one arm per by-ref direction. Mist has one by-ref spelling and C#
/// has three, so this mapping is the whole of what a call site cannot say for itself.
/// <para>
/// Thin on purpose. Deciding what a parameter IS belongs upstream, where the evidence is -- see the
/// truth table in AmbientTypeProviderV2AttributeProjectionTests, which drives the real projector
/// over real metadata. An earlier version of this file inferred the direction here from the
/// modifier flags, which cannot be done correctly: a genuine <c>in</c> and an <c>[In] ref</c> arrive
/// as the same flags and need opposite keywords.
/// </para>
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