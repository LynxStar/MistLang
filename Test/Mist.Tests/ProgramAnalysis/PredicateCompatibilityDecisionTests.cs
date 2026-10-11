// Tier 1: whether a constant can be compared with a value of a type, asked of a declared predicate exactly as a body pattern asks it.

using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class PredicateCompatibilityDecisionTests
{
    [Theory]
    [InlineData("String", "Int32", 0, true)]
    [InlineData("Boolean", "Int32", 0, true)]
    [InlineData("Int32", "String", 2, true)]
    [InlineData("Int32", "Int32", 0, false)]
    [InlineData("Int64", "Int32", 0, false)]
    [InlineData("Byte", "Int32", 0, false)]
    [InlineData("Double", "Double", 0, false)]
    [InlineData("String", "String", 0, false)]
    [InlineData("Boolean", "Boolean", 0, false)]
    public void AConstantNoValueOfTheTypeCouldMeetIsMismatchedWhereverItSits(string constrained, string bound, int kind, bool mismatched)
    {
        var h = new HandBuiltRefinements();
        var input = h.Built.Type(constrained);
        var literal = HandBuiltModel.Literal(h.Built.Type(bound), bound switch { "String" => "a", "Boolean" => true, "Double" => 1.5, _ => 18L });
        var comparison = new ComparisonPattern(HandBuiltModel.Node(), input, (ComparisonKind)kind, literal);
        var predicate = new NegatedPattern(HandBuiltModel.Node(), input, new ConjunctionPattern(HandBuiltModel.Node(), input, [comparison]));

        Assert.Equal(mismatched, ReferenceEquals(comparison, Comparability.Mismatched(predicate)));
        Assert.Equal(mismatched, Comparability.IsRefused(HandBuiltRefinements.RefinePattern(input, predicate)));
    }

    [Fact]
    public void ARelationalPredicateOverAnUnorderedTypeIsMisorderedRatherThanMismatched()
    {
        var h = new HandBuiltRefinements();
        var predicate = h.Compare(h.String, ComparisonKind.GreaterThan, HandBuiltModel.Literal(h.Int, 18L));

        Assert.Null(Comparability.Mismatched(predicate));
        Assert.Same(predicate, Comparability.Misordered(predicate));
        Assert.True(Comparability.IsRefused(HandBuiltRefinements.RefinePattern(h.String, predicate)));
    }

    [Fact]
    public void AnIndeterminateInputASymbolicBoundAnOptionalOrAnEnumVariantIsNotMismatched()
    {
        var h = new HandBuiltRefinements();
        var parameter = new TypeParameter("K", 0, new Scope(null), null);
        var color = HandBuiltModel.Enum("Color", h.Int);
        color.MembersList.Add(new EnumMember("Red", false, null));
        var field = new Field("Bound", h.Int, default, new MemberModifiers { IsReadonly = true });
        var optional = h.Built.Interner.GetOrCreateUnionType([h.Int, h.Built.Type("Void")]);
        Pattern[] predicates =
        [
            h.Compare(parameter, ComparisonKind.Equal, HandBuiltModel.Literal(h.Int, 18L)),
            h.Compare(h.Int, ComparisonKind.LessThan, new FieldReferenceExpression(HandBuiltModel.Node(), field)),
            new ComparisonPattern(HandBuiltModel.Node(), color, ComparisonKind.Equal, new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(color), "Red", color)),
            h.Compare(optional, ComparisonKind.Equal, HandBuiltModel.Literal(h.Int, 18L)),
        ];

        Assert.All(predicates, predicate => Assert.Null(Comparability.Mismatched(predicate)));
    }
}