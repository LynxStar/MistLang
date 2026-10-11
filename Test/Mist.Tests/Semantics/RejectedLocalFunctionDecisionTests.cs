using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class RejectedLocalFunctionDecisionTests
{
    [Theory]
    [InlineData("T")]
    [InlineData("U")]
    [InlineData("T<>")]
    [InlineData("T.Output")]
    public void OnlyTheRejectedDeclarationsTypeNamesCarryReportedFailure(string name)
    {
        var scope = RejectedScope();

        var found = Assert.IsType<TypeResult>(scope.Lookup(new LookupRequest(name, MemberVisibility.None, null)));

        Assert.True(found.IsFailed);
        Assert.Same(ErrorType.Instance, found.Type);
    }

    [Fact]
    public void AnUnrelatedNameStillReachesItsParent()
    {
        var parent = new ParentScope();
        var scope = RejectedScope(parent);

        Assert.Same(parent.Result, scope.Lookup(new LookupRequest("Present", MemberVisibility.None, null)));
        _ = Assert.IsType<NotFoundResult>(scope.Lookup(new LookupRequest("Missing", MemberVisibility.None, null)));
    }

    [Theory]
    [InlineData("group")]
    [InlineData("method")]
    [InlineData("function")]
    public void ReferencesToARejectedSignatureCarryPositiveFailureOwnership(string form)
    {
        var node = HandBuiltModel.Node();
        var method = Function(RejectedScope());
        Expression reference = form switch
        {
            "group" => new MethodGroupExpression(node, null, method.Name, [method], null),
            "method" => new MethodReferenceExpression(node, method),
            _ => new FunctionReferenceExpression(node, null, method, ErrorType.Instance),
        };

        Assert.True(PriorFailure.Of(reference));
    }

    [Fact]
    public void AnUnfinishedSignatureAndAnAdmittedCandidateHaveNoAssumedFailureOwner()
    {
        var node = HandBuiltModel.Node();
        var unfinished = Function(new Scope(null));
        var mixed = new MethodGroupExpression(node, null, "Same", [Function(RejectedScope()), unfinished], null);

        Assert.False(LocalFunctionBinder.IsRejected(unfinished));
        Assert.False(PriorFailure.Of(new MethodReferenceExpression(node, unfinished)));
        Assert.False(PriorFailure.Of(mixed));
    }

    [Fact]
    public void AnEmptyUnfinishedGroupHasNoReportedDeclarationFailure()
    {
        var group = new MethodGroupExpression(HandBuiltModel.Node(), null, "Unfinished", [], null);

        Assert.False(PriorFailure.Of(group));
    }

    private static Method Function(Scope scope)
    {
        return new Method("Same", ErrorType.Instance, [], new MemberModifiers()) { Scope = scope };
    }

    private static RejectedLocalFunctionScope RejectedScope(Scope? parent = null)
    {
        return new RejectedLocalFunctionScope(parent ?? new ParentScope(), new TypeParameterListNode
        {
            TypeParameter = new RegularTypeParameterTypeParameterNode { Name = BindingFragments.Token("T") },
            TypeParameters = [new RegularTypeParameterTypeParameterNode { Name = BindingFragments.Token("U") }],
        });
    }

    private sealed class ParentScope() : Scope(null)
    {
        public TypeResult Result { get; } = new(UnknownType.Instance);

        protected override LookupResult LookupSelf(LookupRequest request)
        {
            return request.Name == "Present" ? Result : new NotFoundResult();
        }
    }
}