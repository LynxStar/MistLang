using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class CatalogTypeTextTests
{
    [Theory]
    [InlineData(" Base ", "Base", "")]
    [InlineData("Base<string>", "Base", "string")]
    [InlineData("App.Base< string, int >", "App.Base", "string|int")]
    [InlineData("Base<Dictionary<int, string>, T[,]>", "Base", "Dictionary<int, string>|T[,]")]
    [InlineData("Base<(int, string), List<T[]>>", "Base", "(int, string)|List<T[]>")]
    public void BaseArgumentsKeepNestedSeparators(string text, string name, string arguments)
    {
        var parsed = CatalogTypeText.Parse(text);

        Assert.Equal(name, parsed.Name);
        Assert.Equal(arguments, string.Join('|', parsed.Arguments));
    }

    [Fact]
    public void SubstitutionReplacesWholeUnqualifiedNamesSimultaneously()
    {
        var parameters = CatalogTypeText.Substitute(
            [ParameterFacts.Declared("Value", "Pair<T, U, TT, App.T, T[]>")], ["T", "U"], ["U", "string"]);

        Assert.Equal("Pair<U, string, TT, App.T, U[]>", Assert.Single(parameters).TypeText);
    }
}