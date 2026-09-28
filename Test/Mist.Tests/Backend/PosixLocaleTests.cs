using System.Globalization;
using Mist.Backends.CSharpTranspiler.Runtime;

namespace Mist.Tests.Backend;

// The culture a program compiled for Windows adopts from the POSIX locale variables, decided from a
// hand-built environment. Apply() is deliberately not called here: it sets the process-wide culture,
// which every other test class in this testhost reads. CultureInvariantEmissionTests runs it for real.
[Trait("Tier", "1")]
public sealed class PosixLocaleTests
{
    [Theory]
    [InlineData("tr_TR.UTF-8", "de_DE.UTF-8", "sv_SE.UTF-8", "tr-TR")]
    [InlineData(null, "de_DE.UTF-8", "sv_SE.UTF-8", "de-DE")]
    [InlineData(null, null, "sv_SE.UTF-8", "sv-SE")]
    [InlineData("", "", "sv_SE.UTF-8", "sv-SE")]
    public void TheFirstNonEmptyVariable_OfLcAllLcMessagesLang_Decides(
        string? lcAll, string? lcMessages, string? lang, string expected)
    {
        Assert.Equal(expected, CultureOf(lcAll, lcMessages, lang)?.Name);
    }

    [Theory]
    [InlineData("tr_TR.UTF-8", "tr-TR")]
    [InlineData("de_DE@euro", "de-DE")]
    [InlineData("en_US.ISO-8859-1", "en-US")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("de", "de")]
    [InlineData("C", "")]
    [InlineData("POSIX", "")]
    [InlineData("C.UTF-8", "")]
    public void APosixLocaleName_NamesItsCulture(string lang, string expected)
    {
        Assert.Equal(expected, CultureOf(null, null, lang)?.Name);
    }

    [Theory]
    [InlineData("not a locale")]
    [InlineData("xx_YY")]
    [InlineData(".UTF-8")]
    [InlineData("@euro")]
    public void AnUnusableName_KeepsTheSystemCulture(string lang)
    {
        Assert.Null(CultureOf(null, null, lang));
    }

    [Fact]
    public void AnUnusableFirstVariable_IsNotSkippedForTheNext()
    {
        Assert.Null(CultureOf("not a locale", null, "de_DE.UTF-8"));
    }

    [Fact]
    public void NoVariable_KeepsTheSystemCulture()
    {
        Assert.Null(CultureOf(null, null, null));
    }

    private static CultureInfo? CultureOf(string? lcAll, string? lcMessages, string? lang)
    {
        var environment = new Dictionary<string, string?>
        {
            ["LC_ALL"] = lcAll,
            ["LC_MESSAGES"] = lcMessages,
            ["LANG"] = lang,
        };
        return PosixLocale.Culture(name => environment.GetValueOrDefault(name));
    }
}