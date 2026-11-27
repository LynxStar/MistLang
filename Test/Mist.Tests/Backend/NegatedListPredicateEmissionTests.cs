using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class NegatedListPredicateEmissionTests
{
    [Fact]
    public void NegatedRegexList_NegatesTheShapeAndItsShortCircuitedFullMatch()
    {
        var text = SizedSequenceTypes.StringType;
        var array = SizedSequenceTypes.ArrayOf(text, length: null);
        var regex = new RegexPattern(null!, text, "cat|dog");
        var list = new ListPattern(null!, [new ListPatternElement(regex, IsSlice: false, ScrutineeType: text)], array);
        var tested = new IsPatternExpression(null!, PropertyPatternEmissionTests.Reference("values", array),
            new NegatedPattern(null!, array, list), SizedSequenceTypes.BooleanType);

        const string expected = "(values is var __e1 && !((__e1 is [var __e2] && "
            + """System.Text.RegularExpressions.Regex.IsMatch(__e2, "\\A(?:" + "cat|dog" + "(?#\n(?#))\\z", """
            + "System.Text.RegularExpressions.RegexOptions.CultureInvariant))))";

        Assert.Equal(expected, PropertyPatternEmissionTests.Emit(tested));
    }
}