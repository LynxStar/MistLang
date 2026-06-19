using Mist.CLI.Args;

namespace Mist.CLI.Tests.Args;

public class CliArgumentParserTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsDefaultsWithCwd()
    {
        var result = CliArgumentParser.Parse([]);

        Assert.NotNull(result.Arguments);
        Assert.Equal(Directory.GetCurrentDirectory(), result.Arguments!.ProjectFolder);
        Assert.Equal("csharp", result.Arguments.BackendId);
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "out"), result.Arguments.OutputDirectory);
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "bin"), result.Arguments.BinDirectory);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_SingleDirectory_SetsProjectFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = CliArgumentParser.Parse([tempDir]);

            Assert.NotNull(result.Arguments);
            Assert.Equal(tempDir, result.Arguments!.ProjectFolder);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
    public void Parse_BinFlag_IsParsed()
    {
        var result = CliArgumentParser.Parse(["--bin", "/tmp/bin"]);

        Assert.NotNull(result.Arguments);
        Assert.Equal("/tmp/bin", result.Arguments!.BinDirectory);
    }

    [Fact]
    public void Parse_AllFlags_WithProjectFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = CliArgumentParser.Parse([tempDir, "--backend", "csharp", "--output", "./dist", "--bin", "./artifacts"]);

            Assert.NotNull(result.Arguments);
            Assert.Equal(tempDir, result.Arguments!.ProjectFolder);
            Assert.Equal("csharp", result.Arguments.BackendId);
            Assert.Equal("./dist", result.Arguments.OutputDirectory);
            Assert.Equal("./artifacts", result.Arguments.BinDirectory);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Parse_MissingBackendValue_ReportsError()
    {
        var result = CliArgumentParser.Parse(["--backend"]);

        Assert.Null(result.Arguments);
        _ = Assert.Single(result.Errors);
        Assert.Contains("--backend", result.Errors[0].Message);
    }

    [Fact]
    public void Parse_MissingOutputValue_ReportsError()
    {
        var result = CliArgumentParser.Parse(["--output"]);

        Assert.Null(result.Arguments);
        _ = Assert.Single(result.Errors);
        Assert.Contains("--output", result.Errors[0].Message);
    }

    [Fact]
    public void Parse_MissingBinValue_ReportsError()
    {
        var result = CliArgumentParser.Parse(["--bin"]);

        Assert.Null(result.Arguments);
        _ = Assert.Single(result.Errors);
        Assert.Contains("--bin", result.Errors[0].Message);
    }

    [Fact]
    public void Parse_UnknownFlag_ReportsError()
    {
        var result = CliArgumentParser.Parse(["--verbose"]);

        Assert.Null(result.Arguments);
        _ = Assert.Single(result.Errors);
        Assert.Contains("--verbose", result.Errors[0].Message);
    }

    [Fact]
    public void Parse_FilePath_ReportsError()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = CliArgumentParser.Parse([tempFile]);

            Assert.Null(result.Arguments);
            _ = Assert.Single(result.Errors);
            Assert.Contains("file paths are no longer accepted", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_MultiplePositionalArgs_ReportsError()
    {
        var result = CliArgumentParser.Parse(["dir1", "dir2"]);

        Assert.Null(result.Arguments);
        Assert.Contains(result.Errors, e => e.Message.Contains("single project folder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_DefaultDirectories_DerivedFromProjectFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = CliArgumentParser.Parse([tempDir]);

            Assert.NotNull(result.Arguments);
            Assert.Equal(Path.Combine(tempDir, "out"), result.Arguments!.OutputDirectory);
            Assert.Equal(Path.Combine(tempDir, "bin"), result.Arguments.BinDirectory);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Parse_SteeringCommand_SetsCommandAndDefaultsToCwd()
    {
        var result = CliArgumentParser.Parse(["steering"]);

        Assert.NotNull(result.Arguments);
        Assert.Equal(CliCommand.Steering, result.Arguments!.Command);
        Assert.Equal(Directory.GetCurrentDirectory(), result.Arguments.ProjectFolder);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Parse_SteeringCommand_WithProjectFolder()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = CliArgumentParser.Parse(["steering", tempDir]);

            Assert.NotNull(result.Arguments);
            Assert.Equal(CliCommand.Steering, result.Arguments!.Command);
            Assert.Equal(tempDir, result.Arguments.ProjectFolder);
            Assert.Empty(result.Errors);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Parse_BuildCommand_DefaultWhenNoVerb()
    {
        var result = CliArgumentParser.Parse([]);

        Assert.NotNull(result.Arguments);
        Assert.Equal(CliCommand.Build, result.Arguments!.Command);
    }
}
