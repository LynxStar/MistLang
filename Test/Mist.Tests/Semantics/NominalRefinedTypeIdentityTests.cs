using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class NominalRefinedTypeIdentityTests
{
    private const string PredicateText = "true";

    [Fact]
    public void NamedRefinementsWithTheSamePredicateHaveDistinctDeclarations()
    {
        var boolean = new ClassType("Boolean", "Standard", new Scope(null), null, null);
        var source = new SourceText("refinements.mist", "type First = bool true;\ntype Second = bool true;");
        var origin = new ParsedOrigin(source);
        var firstPredicate = TruePredicate(origin, source.Contents.IndexOf(PredicateText, StringComparison.Ordinal));
        var secondPredicate = TruePredicate(origin, source.Contents.LastIndexOf(PredicateText, StringComparison.Ordinal));

        var first = TypeInterner.CreateRefinedType(boolean, firstPredicate, "First");
        var second = TypeInterner.CreateRefinedType(boolean, secondPredicate, "Second");

        Assert.NotSame(first.Declaration, second.Declaration);
    }

    private static SimpleRefinementPredicateRefinementPredicateNode TruePredicate(ParsedOrigin origin, int start)
    {
        return new SimpleRefinementPredicateRefinementPredicateNode
        {
            Origin = origin,
            Span = new(start, PredicateText.Length),
            Predicate = new AndPatternNode
            {
                Left = new DirectPatternUnaryPatternNode
                {
                    Primary = new ConstantPatternPrimaryPatternNode { Literal = new TrueConstantConstantLiteralNode() },
                },
                UnaryPatterns = [],
            },
        };
    }
}