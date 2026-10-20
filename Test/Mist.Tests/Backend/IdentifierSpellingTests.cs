using Mist.Backends.CSharpTranspiler.Emitter;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class IdentifierSpellingTests
{
    [Theory]
    [InlineData("fixed")]
    [InlineData("lock")]
    [InlineData("checked")]
    [InlineData("goto")]
    [InlineData("event")]
    [InlineData("unsafe")]
    [InlineData("delegate")]
    public void AReservedCSharpKeywordIsWrittenVerbatim(string name)
    {
        Assert.Equal("@" + name, IdentifierSpelling.Of(name));
    }

    [Theory]
    [InlineData("var")]
    [InlineData("value")]
    [InlineData("nameof")]
    [InlineData("fixedValue")]
    [InlineData("Fixed")]
    public void AContextualKeywordOrOrdinaryNameIsWrittenAsItIs(string name)
    {
        Assert.Equal(name, IdentifierSpelling.Of(name));
    }

    [Theory]
    [InlineData("HelloMist.unsafe", "HelloMist.@unsafe")]
    [InlineData("event.Inner", "@event.Inner")]
    [InlineData("HelloMist", "HelloMist")]
    public void ADottedNameIsSpelledSegmentBySegment(string name, string expected)
    {
        Assert.Equal(expected, IdentifierSpelling.Qualified(name));
    }
}