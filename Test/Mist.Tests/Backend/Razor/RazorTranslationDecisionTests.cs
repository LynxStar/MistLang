using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class RazorTranslationDecisionTests
{
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("<p class=\"a\">", "\"<p class=\\\"a\\\">\"")]
    [InlineData("a\\b\n\r\t\0", "\"a\\\\b\\n\\r\\t\\0\"")]
    public void AStringLiteralEscapesWhatAMistStringCannotHoldRaw(string value, string literal)
    {
        Assert.Equal(literal, MistLiteral.String(value));
    }

    [Fact]
    public void ImportsFilesApplyFromTheProjectFolderDownToTheComponentsFolder()
    {
        var imports = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/app/Components/Pages"] = "pages",
            ["/app"] = "root",
            ["/app/Components"] = "components",
            ["/app/Shared"] = "shared",
            ["/"] = "outside",
        };

        Assert.Equal(["root", "components", "pages"], ImportsResolution.For("/app/Components/Pages/Counter.razor", "/app", imports).ToArray());
        Assert.Equal(["root"], ImportsResolution.For("/app/App.razor", "/app/", imports).ToArray());
    }

    [Fact]
    public void AComponentOutsideItsProjectFolderTakesOnlyItsOwnFoldersImports()
    {
        var imports = new Dictionary<string, string>(StringComparer.Ordinal) { ["/elsewhere"] = "own", ["/"] = "outside" };

        Assert.Equal(["own"], ImportsResolution.For("/elsewhere/Widget.razor", "/app", imports).ToArray());
    }

    [Theory]
    [InlineData("/app/_Imports.razor", true)]
    [InlineData("/app/Components/_imports.RAZOR", true)]
    [InlineData("/app/Imports.razor", false)]
    public void AnImportsFileIsNamedUnderscoreImports(string path, bool imports)
    {
        Assert.Equal(imports, ImportsResolution.IsImports(path));
    }
}