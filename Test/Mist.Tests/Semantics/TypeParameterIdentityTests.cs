// Tier 1 (Unit): the one containment an indeterminate type has. Before it, a method taking `T` was
// never applicable to a `T` argument once a second overload existed, so `Forward(T v) { Set(v); }`
// matched no overload the moment `Set(string)` was declared beside `Set(T)`.

using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class TypeParameterIdentityTests
{
    [Fact]
    public void ATypeParameterIsWithinItselfAndNothingElseIndeterminate()
    {
        var built = new HandBuiltModel();
        var box = new ClassType("Box<>", "Probe", new Scope(null), null, null);
        var other = new ClassType("Other<>", "Probe", new Scope(null), null, null);
        var t = new TypeParameter("T", 0, box.Scope, box);
        var u = new TypeParameter("T", 0, other.Scope, other);

        Assert.True(ConversionRelation.IsWithin(t, t));
        Assert.False(ConversionRelation.IsWithin(t, u));
        Assert.False(ConversionRelation.IsWithin(t, built.Type("Int32")));
        Assert.False(ConversionRelation.IsWithin(built.Type("Int32"), t));
        Assert.True(ConversionRelation.IsWithin(t, UnknownType.Instance));
        Assert.False(ConversionRelation.IsIdentity(t, t));
    }
}