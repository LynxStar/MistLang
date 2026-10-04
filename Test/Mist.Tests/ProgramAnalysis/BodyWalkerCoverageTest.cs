// Tier 1 (Unit): BodyWalker has a row for exactly the node kinds the model can hold. The walker is a
// table so that this test can exist -- see the class comment on BodyWalker -- and a node kind added to
// the model without a row is analysed by no rule at all, silently, until this fails.

using System.Runtime.CompilerServices;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class BodyWalkerCoverageTest
{
    private static readonly Type[] NodeBases =
        [typeof(Expression), typeof(Statement), typeof(Pattern), typeof(InterpolatedStringPart)];

    private static readonly string?[] NodeNamespaces =
        [typeof(Expression).Namespace, typeof(Statement).Namespace, typeof(Pattern).Namespace];

    [Fact]
    public void EveryConcreteNodeKindHasARowAndNothingElseDoes()
    {
        var nodeKinds = typeof(Expression).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
                && !type.Name.Contains('<')
                && (NodeBases.Any(nodeBase => nodeBase.IsAssignableFrom(type)) || NodeNamespaces.Contains(type.Namespace)))
            .ToHashSet();

        var withoutARow = nodeKinds.Except(BodyWalker.Kinds).Select(type => type.Name).Order().ToList();
        var notANode = BodyWalker.Kinds.Except(nodeKinds).Select(type => type.Name).Order().ToList();

        Assert.True(withoutARow.Count == 0, "Node kinds BodyWalker has no row for: " + string.Join(", ", withoutARow));
        Assert.True(notANode.Count == 0, "BodyWalker rows for types that are not nodes: " + string.Join(", ", notANode));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void RetainedFailureTreesVisitReceiversAndArgumentsInNestedSourceOrder()
    {
        var receiver = new UnresolvedExpression(HandBuiltModel.Node(1));
        var left = new UnresolvedExpression(HandBuiltModel.Node(2));
        var right = new ErrorExpression(HandBuiltModel.Node(3));
        var inner = new ErrorExpression(HandBuiltModel.Node(4), [left, right]);
        var lastArgument = new UnresolvedExpression(HandBuiltModel.Node(5));
        var outer = new ErrorExpression(HandBuiltModel.Node(6), [receiver, inner, lastArgument]);

        Assert.Equal([receiver, inner, lastArgument], BodyWalker.ChildrenOf(outer));
        Assert.Equal([outer, receiver, inner, left, right, lastArgument], BodyWalker.DescendantsAndSelf(outer));
    }

    [Fact]
    public void ATypedPropertyPatternVisitsItsTypeTestBeforeItsSubpatterns()
    {
        var tested = new TypeTestPattern(new BareTypePatternPrimaryPatternNode(), UnknownType.Instance, binding: null);
        var subpattern = new PropertySubpattern(
            new PropertySubpatternNode(),
            "Name",
            new CatchAllPattern(new DiscardPatternPrimaryPatternNode(), UnknownType.Instance, binding: null),
            UnknownType.Instance,
            new NotFoundResult());
        var pattern = new PropertyPattern(
            new PropertyPatternPrimaryPatternNode(), UnknownType.Instance, [subpattern], typeTest: tested);

        Assert.Equal([tested, subpattern], BodyWalker.ChildrenOf(pattern));
        Assert.Equal([pattern, tested, subpattern, subpattern.Pattern], BodyWalker.DescendantsAndSelf(pattern));
    }
}