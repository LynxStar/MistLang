using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class TemplateSyntaxTests
{
    [Theory]
    [InlineData("plain text", 1, 0)]
    [InlineData("'{0}' and '{1}' and '{0}'", 7, 3)]
    [InlineData("Reference '{0}' in '{1}' must be a table (e.g., {0} = {{ path = \"...\" }}).", 7, 3)]
    [InlineData("'{name}' is not callable.{namespace? An extension in '{namespace}' is not imported.}", 4, 3)]
    [InlineData("A 'with {{tier}}' clause.", 1, 0)]
    public void AWellFormedTemplateParses(string template, int segments, int placeholders)
    {
        Assert.True(TemplateSyntax.TryParse(template, out var parsed, out var error), error);
        Assert.Equal(segments, parsed.Length);
        Assert.Equal(placeholders, TemplateSyntax.Placeholders(parsed).Count());
    }

    [Theory]
    [InlineData("unclosed '{name")]
    [InlineData("stray '}' brace")]
    [InlineData("empty '{}' placeholder")]
    [InlineData("format '{0:X}' specifier")]
    [InlineData("nested '{a?{b?x}}' conditional")]
    [InlineData("unclosed '{a? text' conditional")]
    public void AMalformedTemplateIsRefused(string template)
    {
        Assert.False(TemplateSyntax.TryParse(template, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("'{0}' and '{1}'", true)]
    [InlineData("'{name}' and '{0}'", false)]
    [InlineData("no placeholders", false)]
    public void PositionalTemplatesUseOnlyIndices(string template, bool positional)
    {
        Assert.True(TemplateSyntax.TryParse(template, out var parsed, out _));
        Assert.Equal(positional, TemplateSyntax.IsPositional(parsed));
    }
}