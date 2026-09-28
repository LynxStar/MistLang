using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RegexFullMatchDecisionTests
{
    private static readonly ClassType Text = new("String", "Standard", null, null, null);

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    [InlineData("abc|xyz", "abc")]
    [InlineData("abc|xyz", "xyz")]
    [InlineData(@"abc\\n", "abc\n")]
    [InlineData(@"abc\\r?\\n", "abc\r\n")]
    [InlineData(@"(?m)^abc$\\n", "abc\n")]
    public void AnInputThePatternConsumesEntirely_IsAdmitted(string body, string input)
    {
        Assert.True(Decide(body, input));
    }

    [Theory]
    [InlineData("abc", "abc\n")]
    [InlineData("abc", "abc\r\n")]
    [InlineData("abc", "\nabc")]
    [InlineData("abc", "xabc")]
    [InlineData("abc", "abcx")]
    [InlineData("", "\n")]
    [InlineData("", "a")]
    [InlineData("abc|xyz", "abcz")]
    [InlineData("abc|xyz", "zxyz")]
    [InlineData("^abc$", "abc\n")]
    [InlineData("(?m)^abc$", "abc\n")]
    [InlineData("(?m)^abc$", "abc\nabc")]
    [InlineData("(?m)abc$", "xyz\nabc")]
    [InlineData(@"abc\\n?", "abc\n\n")]
    public void AnInputWithCharactersThePatternLeavesUnconsumed_IsRefused(string body, string input)
    {
        Assert.False(Decide(body, input));
    }

    private static bool? Decide(string body, string input)
    {
        var target = new RefinedType(Text, null!, "Shaped") { Predicate = new RegexPattern(null!, Text, body) };
        return LiteralPredicates.Decide(target, new LiteralValue(input, Text.FullyQualifiedName));
    }
}