using System.Collections.Immutable;
using Mist.Driver.Contracts;

namespace Mist.CLI.Tests;

/// <summary>
/// Fixed cases for the missing-test-package warning (attributes-and-test-command Requirement 6.3) that the
/// generated scenarios in <see cref="TestPackageWarningDetectionPropertyTests"/> never produce: spacing inside
/// the attribute brackets, a project of several files and a configuration with no package list at all.
/// </summary>
[Trait("Tier", "1")]
public sealed class TestFrameworkDetectionTests
{
    [Theory]
    [InlineData("[ Fact]")]
    [InlineData("[Fact ]")]
    [InlineData("[Theory (1)]")]
    [InlineData("[\tTestMethod\t]")]
    public void HasTestAttributes_AllowsSpacingInsideTheBrackets(string attribute)
    {
        Assert.True(TestFrameworkDetection.HasTestAttributes($"class MyTests\n{{\n    {attribute}\n    public void Check() {{ }}\n}}"));
    }

    [Fact]
    public void ShouldWarn_WhenOnlyOneOfSeveralFilesHasATestAttribute()
    {
        string[] sources = ["class Helper\n{\n}", "class MyTests\n{\n    [Fact]\n    public void Check() { }\n}"];

        Assert.True(TestFrameworkDetection.ShouldWarn(sources, []));
        Assert.False(TestFrameworkDetection.ShouldWarn(sources, [new NuGetPackageReference("xunit", "2.9.3")]));
    }

    [Fact]
    public void ShouldWarn_WhenNoPackageListWasGiven()
    {
        ImmutableArray<NuGetPackageReference> packages = default;

        Assert.False(TestFrameworkDetection.HasFrameworkPackage(packages));
        Assert.True(TestFrameworkDetection.ShouldWarn(["[Fact]\npublic void Check() { }"], packages));
    }
}