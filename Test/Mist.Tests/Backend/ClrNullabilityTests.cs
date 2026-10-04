// Tier 1 (Unit): an attribute whose type the loader cannot resolve counts as absent for nullability
// rather than taking the projection down. A TestHost package exposed this shape through an undefined
// attribute type; it is reproduced with CustomAttributeData whose type throws on first touch, as a
// metadata-only load does.

using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;

namespace Mist.Tests.Backend;

public sealed class ClrNullabilityTests
{
    private static readonly MethodInfo Member = typeof(object).GetMethod(nameof(ToString))!;

    [Fact]
    public void AnAttributeWhoseTypeCannotBeResolvedCountsAsAbsent()
    {
        var withDangling = ClrNullability.ResultIsNonNull(new Annotated([new DanglingAttribute()]), Member);
        var withNone = ClrNullability.ResultIsNonNull(new Annotated([]), Member);

        Assert.Equal(withNone, withDangling);
    }

    private sealed class Annotated(IList<CustomAttributeData> attributes) : ParameterInfo
    {
        public override IList<CustomAttributeData> GetCustomAttributesData()
        {
            return attributes;
        }
    }

    private sealed class DanglingAttribute : CustomAttributeData
    {
        public override Type AttributeType => throw new TypeLoadException("Could not find type");

        public override ConstructorInfo Constructor => throw new TypeLoadException("Could not find type");
    }
}