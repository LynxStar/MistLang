using Mist.Generated;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// A member a receiver lacks is reported naming the receiver as Mist writes it -- the construction it is,
/// never the definition's registered name with its arity marker.
/// </summary>
[Trait("Tier", "2")]
public sealed class UnresolvedMemberNamingTests
{
    [Theory]
    [InlineData(true, "'Box<int>' has no member named 'Missing'")]
    [InlineData(false, "'Plain' has no member named 'Missing'")]
    public void TheMissingMemberIsReportedAgainstTheReceiverAsWritten(bool generic, string expected)
    {
        var f = new BindingFragments();
        var definition = new ClassType(generic ? "Box<>" : "Plain", "Probe", new Scope(null), null, null);

        if(generic)
        {
            definition.TypeParametersList.Add(new TypeParameter("T", 0, definition.Scope, definition));
        }

        _ = f.Local("held", generic ? new ConstructedType(definition, [f.Built.Type("Int32")]) : definition);

        _ = f.Bind(BindingFragments.Identifier("held"), new MemberAccessSuffixPostfixSuffixNode { Name = BindingFragments.Token("Missing") });

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_MEMBER", diagnostic.Number.Value);
        Assert.Contains(expected, diagnostic.Message, StringComparison.Ordinal);
    }
}