using Mist.CLI.Args;

namespace Mist.CLI.Tests.Args;

public class CliArgumentParserTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsDefaults()
    {
        var result = CliArgumentParser.Parse([]);

        Assert.NotNull(result.Arguments);
        Assert.Empty(result.Arguments!.SourceFiles);
        Assert.Equal("csharp", result.Arguments.BackendId);
        Assert.Equal("./out", result.Arguments.OutputDirectory);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_SourceFiles_AreParsed()
    {
        var result = CliArgumentParser.Parse(["file1.mist", "file2.mist"]);

        Assert.NotNull(result.Arguments);
        Assert.Equal(["file1.mist", "file2.mist"], result.Arguments!.SourceFiles);
    }

    [Fact]
    public void Parse_BackendFlag_IsParsed()
    {
        var result = CliArgumentParser.Parse(["--backend", "js"]);

        Assert.NotNull(result.Arguments);
        Assert.Equal("js", result.Arguments!.BackendId);
    }

    [Fact]
    public void Parse_OutputFlag_IsParsed()
    {
        var result = CliArgumentParser.Parse(["--output", "/tmp/build"]);

        Assert.NotNull(result.Arguments);
        Assert.Equal("/tmp/build", result.Arguments!.OutputDirectory);
    }

    [Fact]
    public void Parse_AllFlags_WithSourceFiles()
    {
        var result = CliArgumentParser.Parse(["main.mist", "--backend", "csharp", "--output", "./dist"]);

        Assert.NotNull(result.Arguments);
        Assert.Equal(["main.mist"], result.Arguments!.SourceFiles);
        Assert.Equal("csharp", result.Arguments.BackendId);
        Assert.Equal("./dist", result.Arguments.OutputDirectory);
    }

    [Fact]
    public void Parse_MissingBackendValue_ReportsError()
    {
        var result = CliArgumentParser.Parse(["--backend"]);

        Assert.Null(result.Arguments);
        Assert.Single(result.Errors);
        Assert.Contains("--backend", result.Errors[0].Message);
    }

    [Fact]
    public void Parse_MissingOutputValue_ReportsError()
    {
        var result = CliArgumentParser.Parse(["--output"]);

        Assert.Null(result.Arguments);
        Assert.Single(result.Errors);
        Assert.Contains("--output", result.Errors[0].Message);
    }

    [Fact]
    public void Parse_UnknownFlag_ReportsError()
    {
        var result = CliArgumentParser.Parse(["--verbose"]);

        Assert.Null(result.Arguments);
        Assert.Single(result.Errors);
        Assert.Contains("--verbose", result.Errors[0].Message);
    }
}
