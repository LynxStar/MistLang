using Mist.Backends.CSharpTranspiler;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class AssemblyConflictRuleTests
{
    [Theory]
    [InlineData("Newtonsoft.Json", "13.0.0.0", "Newtonsoft.Json", "12.0.0.0", true)]
    [InlineData("Newtonsoft.Json", "13.0.0.0", "newtonsoft.json", "12.0.0.0", true)]
    [InlineData("Newtonsoft.Json", "13.0.0.0", "Newtonsoft.Json", "13.0.0.0", false)]
    [InlineData("Newtonsoft.Json", "13.0.0.0", "Serilog", "2.0.0.0", false)]
    [InlineData("Newtonsoft.Json", null, "Newtonsoft.Json", "12.0.0.0", false)]
    [InlineData("Newtonsoft.Json", "13.0.0.0", "Newtonsoft.Json", null, false)]
    public void OneSimpleNameWithTwoKnownVersionsConflicts(
        string suppliedName, string? suppliedVersion, string candidateName, string? candidateVersion, bool conflicts)
    {
        Assert.Equal(
            conflicts,
            AssemblyConflictRule.Conflicts(
                (suppliedName, suppliedVersion is null ? null : Version.Parse(suppliedVersion)),
                (candidateName, candidateVersion is null ? null : Version.Parse(candidateVersion))));
    }
}