using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class MethodScopeLookupTests
{
    [Fact]
    public void AStaticMethodKeepsOutwardTypeLookupTypeOnly()
    {
        var parent = new RecordingParentScope();
        var method = new MethodScope(parent) { IsStatic = true };
        var location = BindingFragments.TypeName("Gauge").First;
        var request = new LookupRequest("Gauge", MemberVisibility.None, location);

        _ = method.Lookup(request);

        Assert.Equal(request, parent.Request);
    }

    [Fact]
    public void AStaticMethodNarrowsOutwardValueLookupToStaticMembers()
    {
        var parent = new RecordingParentScope();
        var method = new MethodScope(parent) { IsStatic = true };
        var location = BindingFragments.Identifier("Gauge");
        var request = new LookupRequest("Gauge", MemberVisibility.InstanceAndStatic, location);

        _ = method.Lookup(request);

        Assert.Equal(new LookupRequest("Gauge", MemberVisibility.StaticOnly, location), parent.Request);
    }

    private sealed class RecordingParentScope() : Scope(null)
    {
        public LookupRequest? Request { get; private set; }

        internal override LookupResult Lookup(LookupRequest request)
        {
            Request = request;
            return new NotFoundResult();
        }
    }
}