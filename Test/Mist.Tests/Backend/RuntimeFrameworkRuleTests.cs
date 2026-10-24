using Mist.Backends.CSharpTranspiler;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class RuntimeFrameworkRuleTests
{
    [Fact]
    public void TheSingleFrameworkFormNamesOneFramework()
    {
        const string json = """{ "runtimeOptions": { "tfm": "net10.0", "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" } } }""";

        Assert.Equal(["Microsoft.NETCore.App"], RuntimeFrameworkRule.Frameworks(json).AsEnumerable());
    }

    [Fact]
    public void TheFrameworksArrayNamesEveryFramework()
    {
        const string json = """
            {
              // written by the SDK
              "runtimeOptions": {
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
                  { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" },
                ]
              }
            }
            """;

        Assert.Equal(["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"], RuntimeFrameworkRule.Frameworks(json).AsEnumerable());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "runtimeOptions": 3 }""")]
    public void AnUnreadableConfigurationNamesNoFramework(string json)
    {
        Assert.Empty(RuntimeFrameworkRule.Frameworks(json));
    }

    [Fact]
    public void AFrameworkIsMissingUnlessDeclaredOrImplied()
    {
        var missing = RuntimeFrameworkRule.Missing(
            ["Microsoft.NETCore.App", "microsoft.aspnetcore.app", "Microsoft.WindowsDesktop.App"],
            [new FrameworkReference("Microsoft.AspNetCore.App")]);

        Assert.Equal(["Microsoft.WindowsDesktop.App"], missing.AsEnumerable());
    }
}