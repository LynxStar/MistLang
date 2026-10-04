using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// A dependent parameter named in expression position is a value of its domain: its type is the domain,
/// so a local, an array literal or an inferred type argument built from it is the domain rather than
/// the parameter, and it answers its domain's instance members.
/// </summary>
public sealed class DependentValueReadTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("UInt64", true)]
    [InlineData("Type", false)]
    public void AParameterReadAsAValueHasItsDomainsTypeOnlyWhenDependent(string domain, bool dependent)
    {
        var built = new HandBuiltModel();
        var parameter = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = dependent, Domain = built.Type(domain) };

        var read = new TypeReferenceExpression(HandBuiltModel.Node(), parameter);

        Assert.Same(dependent ? built.Type(domain) : parameter, read.Type);
        Assert.Same(parameter, read.ReferencedType);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void APackReadAsAValueKeepsThePackAsItsType()
    {
        var built = new HandBuiltModel();
        var pack = new TypeParameter("Ts", 0, new Scope(null), null)
        {
            IsDependent = true,
            PackKind = TypePackKind.Unbracketed,
            Domain = built.Type("Type"),
        };

        Assert.Same(pack, new TypeReferenceExpression(HandBuiltModel.Node(), pack).Type);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ADependentValueAnswersItsDomainsInstanceMembers()
    {
        var f = new BindingFragments();
        var int32 = f.Built.Type("Int32");
        _ = BindingFragments.Member(int32, "ToString", f.Built.Type("String"));
        f.Types.DeclareTopLevelType("N", new TypeParameter("N", 0, f.Scope, null) { IsDependent = true, Domain = int32 });

        var call = Assert.IsType<InvocationExpression>(f.Bind(BindingFragments.Identifier("N"), new MethodCallSuffixPostfixSuffixNode
        {
            Name = BindingFragments.Token("ToString"),
            Arguments = BindingFragments.Arguments(),
        }));

        Assert.Equal("ToString", call.Method?.Name);
        Assert.Same(f.Built.Type("String"), call.Type);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }
}