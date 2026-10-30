using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Tomlyn.Model;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class ProjectFileParserGeneratorTests
{
    private const string Manifest = "/probe/consumer/mist.toml";

    [Fact]
    public void AbsentSurfacePreservesOrdinaryConfiguration()
    {
        var bag = new DiagnosticBag();
        var config = Read(new TomlTable(), bag);
        Assert.NotNull(config);
        Assert.Equal(MistProjectType.Regular, config.ProjectType);
        Assert.Empty(config.Generators);
        Assert.Null(config.SeedSources);
        Assert.Empty(bag.Admitted);
    }

    [Fact]
    public void GeneratorAndConsumerExamplesResolveOrderedPaths()
    {
        var bag = new DiagnosticBag();
        var generator = Read(new TomlTable { ["project_type"] = "source_generator" }, bag);
        Assert.Equal(MistProjectType.SourceGenerator, generator!.ProjectType);
        var entry = Entry();
        entry["arguments"] = new TomlArray { "Mist.Common.Source.NodeOrigin", "second" };
        entry["seed"] = true;
        var config = Read(new TomlTable { ["source_generators"] = new TomlTable { ["StormMist"] = entry } }, bag);
        var reference = Assert.Single(config!.Generators);
        Assert.Equal("/probe/StormMist/mist.toml", reference.ProjectPath);
        Assert.Equal<string>(["/probe/consumer/Grammar/Main.storm", "/probe/consumer/Grammar/tokens.storm"], reference.Inputs);
        Assert.Equal<string>(["Mist.Common.Source.NodeOrigin", "second"], reference.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(600), reference.TimeLimit);
        Assert.True(reference.Seed);
        Assert.Empty(bag.Admitted);
    }

    [Theory]
    [InlineData("other")]
    [InlineData(42L)]
    public void UnknownProjectTypesAreRefused(object type)
    {
        var bag = new DiagnosticBag();
        Assert.Null(Read(new TomlTable { ["project_type"] = type }, bag));
        AssertFault(bag, DriverDiagnostics.GeneratorProjectInvalid);
    }

    [Fact]
    public void LibraryGeneratorIsRefused()
    {
        var bag = new DiagnosticBag();
        var config = new ProjectConfiguration("Consumer", "Consumer", [], OutputType: ProjectOutputType.Library);
        Assert.Null(ProjectFileParser.ReadGenerators(new TomlTable { ["project_type"] = "source_generator" }, Manifest, config, bag));
        AssertFault(bag, DriverDiagnostics.GeneratorProjectInvalid);
    }

    [Theory]
    [InlineData("path", null)]
    [InlineData("path", "")]
    [InlineData("path", "bad\npath")]
    [InlineData("inputs", null)]
    [InlineData("inputs", "wrong")]
    [InlineData("arguments", "wrong")]
    [InlineData("timeout_seconds", 0L)]
    [InlineData("timeout_seconds", -1L)]
    [InlineData("timeout_seconds", "600")]
    [InlineData("timeout_seconds", 2147483647L)]
    [InlineData("seed", "true")]
    public void MalformedEntryIsRefusedOnce(string key, object? value)
    {
        var entry = Entry();
        if(value is null)
        {
            _ = entry.Remove(key);
        }
        else
        {
            entry[key] = value;
        }

        Refuse(entry);
    }

    [Theory]
    [InlineData("inputs", 0)]
    [InlineData("inputs", 1)]
    [InlineData("inputs", 2)]
    [InlineData("arguments", 2)]
    public void BadArraysAreRefused(string key, int shape)
    {
        var entry = Entry();
        entry[key] = shape switch { 0 => new TomlArray(), 1 => new TomlArray { "" }, _ => new TomlArray { 42L } };
        Refuse(entry);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("................................................................................................................................")]
    public void InvalidNamesAreRefused(string name)
    {
        var bag = new DiagnosticBag();
        Assert.Null(Read(new TomlTable { ["source_generators"] = new TomlTable { [name] = Entry() } }, bag));
        AssertFault(bag, DriverDiagnostics.GeneratorReferenceInvalid);
    }

    [Theory]
    [InlineData("source_generators")]
    [InlineData("seed_sources")]
    public void NonTableDeclarationsAreRefused(string key)
    {
        var bag = new DiagnosticBag();
        Assert.Null(Read(new TomlTable { [key] = true }, bag));
        AssertFault(bag, DriverDiagnostics.GeneratorReferenceInvalid);
    }

    [Fact]
    public void SeedPathsAndExplicitTimeoutResolve()
    {
        var entry = Entry();
        entry["timeout_seconds"] = 12L;
        var bag = new DiagnosticBag();
        var config = Read(new TomlTable
        {
            ["source_generators"] = new TomlTable { ["StormMist"] = entry },
            ["seed_sources"] = new TomlTable { ["path"] = "capture", ["provenance"] = "capture.json" }
        }, bag);
        Assert.Equal(TimeSpan.FromSeconds(12), Assert.Single(config!.Generators).TimeLimit);
        Assert.Equal(new SeedSources("/probe/consumer/capture", "/probe/consumer/capture.json"), config.SeedSources);
        Assert.Empty(bag.Admitted);
    }

    private static ProjectConfiguration? Read(TomlTable table, DiagnosticBag bag)
    {
        return ProjectFileParser.ReadGenerators(table, Manifest, new ProjectConfiguration("Consumer", "Consumer", []), bag);
    }

    private static TomlTable Entry()
    {
        return new TomlTable { ["path"] = "../StormMist", ["inputs"] = new TomlArray { "Grammar/Main.storm", "Grammar/tokens.storm" } };
    }

    private static void Refuse(TomlTable entry)
    {
        var bag = new DiagnosticBag();
        Assert.Null(Read(new TomlTable { ["source_generators"] = new TomlTable { ["StormMist"] = entry } }, bag));
        AssertFault(bag, DriverDiagnostics.GeneratorReferenceInvalid);
    }

    private static void AssertFault(DiagnosticBag bag, MistDiagnostic descriptor)
    {
        var occurrence = Assert.Single(bag.Admitted);
        Assert.Same(descriptor, occurrence.Descriptor);
        Assert.Equal(new ProjectLocation(Manifest, null), occurrence.Location);
        Assert.All(occurrence.Arguments, Assert.NotNull);
    }
}