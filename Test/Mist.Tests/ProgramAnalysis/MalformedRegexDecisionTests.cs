using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class MalformedRegexDecisionTests
{
    private static readonly ClassType Text = new("String", "Standard", null, null, null);

    [Theory]
    [InlineData("[", "offset 1")]
    [InlineData("a)|(?:b", "offset 2")]
    [InlineData("a)(b", "offset 2")]
    [InlineData(@"abc\\", "offset 4")]
    [InlineData(@"\\q", "offset 2")]
    [InlineData("(?x:abc #c)", "offset 11")]
    public void APatternTheEngineRefusesAlone_IsAnErrorNamingItsOffset(string body, string offset)
    {
        Assert.Contains(offset, RegexSyntax.Error(body), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc|xyz")]
    [InlineData(@"\\d+")]
    [InlineData("(?x)abc #c")]
    [InlineData(@"a\""b")]
    public void APatternTheEngineAccepts_IsNoError(string body)
    {
        Assert.Null(RegexSyntax.Error(body));
    }

    [Fact]
    public void ABodyThatDoesNotDecode_IsNotJudged()
    {
        Assert.Null(RegexSyntax.Error(@"\q"));
    }

    [Theory]
    [InlineData("regex")]
    [InlineData("negated")]
    [InlineData("either")]
    [InlineData("both")]
    public void ARefinementHoldingAMalformedRegexAnywhere_IsRefused(string form)
    {
        var target = Refined(Predicate(form, Regex("a)|(?:b")));

        _ = Assert.Single(RegexSyntax.Malformed(target.Predicate));
        Assert.True(LiteralPredicates.IsRefused(target));
        Assert.True(LiteralPredicates.Satisfied(target, Text, new LiteralValue("zzz", Text.FullyQualifiedName), null));
    }

    [Theory]
    [InlineData("[", false)]
    [InlineData("[", true)]
    [InlineData("a)|(?:b", false)]
    [InlineData("a)|(?:b", true)]
    public void AValueForARefinementHoldingAMalformedRegex_DrawsNoRefinementFinding(string body, bool onConstraint)
    {
        var target = new RefinedType(Text, HandBuiltModel.Node(), onConstraint ? null : "Shaped") { Predicate = Regex(body) };
        var value = new LiteralExpression(null!, "zzz", Text);

        Assert.Null(RefinementDecision.Judge(value, target, Admitted.Everything, LiteralValue.Of(value), onConstraint: onConstraint));
    }

    [Theory]
    [InlineData("regex")]
    [InlineData("negated")]
    [InlineData("either")]
    [InlineData("both")]
    public void ARefinementOfWellFormedRegexes_IsNotRefused(string form)
    {
        var target = Refined(Predicate(form, Regex("[a-z]+")));

        Assert.Empty(RegexSyntax.Malformed(target.Predicate));
        Assert.False(LiteralPredicates.IsRefused(target));
        Assert.False(LiteralPredicates.Satisfied(target, Text, new LiteralValue(form == "negated" ? "abc" : "123", Text.FullyQualifiedName), null));
    }

    private static Pattern Predicate(string form, RegexPattern regex)
    {
        var equal = new ComparisonPattern(null!, Text, ComparisonKind.Equal, new LiteralExpression(null!, "x", Text));

        return form switch
        {
            "negated" => new NegatedPattern(null!, Text, regex),
            "either" => new DisjunctionPattern(null!, Text, [equal, regex]),
            "both" => new ConjunctionPattern(null!, Text, [new NegatedPattern(null!, Text, equal), regex]),
            _ => regex,
        };
    }

    private static RegexPattern Regex(string body)
    {
        return new RegexPattern(null!, Text, body);
    }

    private static RefinedType Refined(Pattern predicate)
    {
        return new RefinedType(Text, null!, "Shaped") { Predicate = predicate };
    }
}