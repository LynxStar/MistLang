using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "1")]
public sealed class OutputListValidationTests
{
    [Theory]
    [InlineData("a.mist", null)]
    [InlineData("./a.mist", null)]
    [InlineData("dir/../a.mist", null)]
    [InlineData("/a.mist", "relative")]
    [InlineData("C:\\a.mist", "relative")]
    [InlineData("../a.mist", "escapes")]
    [InlineData("dir/../../a.mist", "escapes")]
    [InlineData("a.cs", ".mist")]
    [InlineData("missing.mist", "missing")]
    [InlineData("out/a.mist", "excluded")]
    [InlineData("dir/bin/a.mist", "excluded")]
    [InlineData("marked/a.mist", "excluded")]
    public void EveryPathRefusalAndSafeNormalization(string path, string? reason)
    {
        var fault = OutputListValidation.Validate([path], new[] { "a.mist", "out/a.mist", "dir/bin/a.mist", "marked/a.mist" }.Where(actual => reason is not null || actual == "a.mist"), ["marked"]);
        if(reason is null)
        {
            Assert.Null(fault);
        }
        else
        {
            Assert.NotNull(fault);
            Assert.Contains(reason, fault.Reason);
            Assert.Equal(path, fault.Path);
        }
    }

    [Fact]
    public void EmptyDuplicateUnlistedAndRootMarkerAreRefused()
    {
        Assert.Contains("empty", OutputListValidation.Validate([], [], [])!.Reason);
        Assert.Contains("duplicate", OutputListValidation.Validate(["a.mist", "./a.mist"], ["a.mist"], [])!.Reason);
        Assert.Equal("b.mist", OutputListValidation.Validate(["a.mist"], ["a.mist", "b.mist"], [])!.Path);
        Assert.Contains("excluded", OutputListValidation.Validate(["a.mist"], ["a.mist"], [""])!.Reason);
    }
}