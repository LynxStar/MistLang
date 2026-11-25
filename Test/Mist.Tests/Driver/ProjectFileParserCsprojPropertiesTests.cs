using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// specs/csproj-property-passthrough, Requirements 1, 2 and 4. Expected messages are written out from
/// the descriptor templates, not rendered through them.
/// </summary>
[Trait("Tier", "2")]
public sealed class ProjectFileParserCsprojPropertiesTests : IDisposable
{
    private readonly string projectDir = Path.Combine(Path.GetTempPath(), $"mist_csproj_props_test_{Guid.NewGuid():N}");

    public ProjectFileParserCsprojPropertiesTests()
    {
        _ = Directory.CreateDirectory(projectDir);
    }

    private string ManifestPath => Path.Combine(projectDir, ProjectFileParser.FileName);

    public void Dispose()
    {
        Directory.Delete(projectDir, recursive: true);
    }

    private (ProjectConfiguration Configuration, IReadOnlyList<Diagnostic> Diagnostics) Parse(string table)
    {
        File.WriteAllText(ManifestPath, "name = \"MyApp\"\n" + table);
        var diagnostics = new DiagnosticBag();

        var configuration = ProjectFileParser.Parse(projectDir, diagnostics);

        Assert.NotNull(configuration);
        return (configuration, diagnostics.Admitted);
    }

    private void AssertOccurrence(Diagnostic occurrence, string number, DiagnosticSeverity severity, string message)
    {
        Assert.Equal(number, occurrence.Number.Value);
        Assert.Equal(severity, occurrence.Severity);
        Assert.Equal(new FileOnly(ManifestPath), occurrence.Location);
        Assert.Equal(message, occurrence.Message);
    }

    [Fact]
    public void AbsentTable_YieldsAnEmptyCollectionAndNoDiagnostic()
    {
        var (configuration, diagnostics) = Parse("");

        Assert.Empty(diagnostics);
        Assert.False(configuration.CsprojProperties.IsDefault);
        Assert.Empty(configuration.CsprojProperties);
    }

    [Fact]
    public void ValidEntries_AreKeptVerbatimInDeclarationOrder()
    {
        var (configuration, diagnostics) = Parse("""
            [csproj_properties]
            Zeta = "last-letter"
            Alpha_2 = "two words"
            "My.Prop-X" = "a<b"
            """);

        Assert.Empty(diagnostics);
        Assert.Equal<CsprojProperty>(
            [new CsprojProperty("Zeta", "last-letter"), new CsprojProperty("Alpha_2", "two words"), new CsprojProperty("My.Prop-X", "a<b")],
            configuration.CsprojProperties);
    }

    [Theory]
    [InlineData("csproj_properties = \"UserSecretsId\"")]
    [InlineData("csproj_properties = [\"UserSecretsId\", \"abc\"]")]
    [InlineData("csproj_properties = 5")]
    public void NonTableField_IsRefusedAndTheRestOfTheManifestIsStillRead(string field)
    {
        var (configuration, diagnostics) = Parse(field + "\noutput_type = \"library\"\n");

        var occurrence = Assert.Single(diagnostics);
        AssertOccurrence(occurrence, "MIST0092", DiagnosticSeverity.Error,
            $"The 'csproj_properties' field in '{ManifestPath}' must be a table ([csproj_properties]).");
        Assert.Equal<SlotValue>([new PathValue(ManifestPath)], occurrence.Arguments);
        Assert.Empty(configuration.CsprojProperties);
        Assert.Equal(ProjectOutputType.Library, configuration.OutputType);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("\"\\t\"")]
    public void EmptyOrWhitespaceKey_IsRefusedAndSkipped(string key)
    {
        var (configuration, diagnostics) = Parse($"""
            [csproj_properties]
            {key} = "value"
            After = "kept"
            """);

        var occurrence = Assert.Single(diagnostics);
        AssertOccurrence(occurrence, "MIST0093", DiagnosticSeverity.Error,
            $"Property key in '[csproj_properties]' of '{ManifestPath}' must not be empty or whitespace.");
        Assert.Equal<SlotValue>([new PathValue(ManifestPath)], occurrence.Arguments);
        Assert.Equal<CsprojProperty>([new CsprojProperty("After", "kept")], configuration.CsprojProperties);
    }

    [Fact]
    public void KeyWithInvalidCharacters_ListsEachDistinctCharacterOnceInSortedOrder()
    {
        // '@' occurs first and twice, '$' later: distinct and ordinal order give '$' (U+0024) before '@' (U+0040).
        var (configuration, diagnostics) = Parse("""
            [csproj_properties]
            "Prop@Name$Two@" = "value"
            After = "kept"
            """);

        var occurrence = Assert.Single(diagnostics);
        AssertOccurrence(occurrence, "MIST0094", DiagnosticSeverity.Error,
            $"Property key 'Prop@Name$Two@' in '[csproj_properties]' of '{ManifestPath}' contains invalid characters: '$', '@'.");
        Assert.Equal(new NameValue("Prop@Name$Two@"), occurrence.Arguments[0]);
        Assert.Equal(new PathValue(ManifestPath), occurrence.Arguments[1]);
        var listed = Assert.IsType<SequenceValue>(occurrence.Arguments[2]);
        Assert.Equal<SlotValue>([new NameValue("$"), new NameValue("@")], listed.Items);
        Assert.Equal<CsprojProperty>([new CsprojProperty("After", "kept")], configuration.CsprojProperties);
    }

    [Theory]
    [InlineData("\"Has Space\"")]
    [InlineData("\"Caf\\u00e9\"")]
    public void KeyOutsideTheAsciiNameSet_IsRefusedAndSkipped(string key)
    {
        // Requirement 2.1 admits ASCII letters, digits, '-', '_' and '.' only. The character list is not
        // pinned: for a non-ASCII letter it is empty, contrary to Requirement 2.2 (MIST-1732).
        var (configuration, diagnostics) = Parse($"""
            [csproj_properties]
            {key} = "value"
            After = "kept"
            """);

        var occurrence = Assert.Single(diagnostics);
        Assert.Equal("MIST0094", occurrence.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Equal<CsprojProperty>([new CsprojProperty("After", "kept")], configuration.CsprojProperties);
    }

    [Fact]
    public void KeyLongerThan128Characters_IsRefusedWithTheLimitAndActualLength()
    {
        var longKey = new string('K', 129);

        var (configuration, diagnostics) = Parse($"""
            [csproj_properties]
            {longKey} = "value"
            After = "kept"
            """);

        var occurrence = Assert.Single(diagnostics);
        Assert.Equal("MIST0095", occurrence.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Equal(new FileOnly(ManifestPath), occurrence.Location);
        Assert.Equal(new PathValue(ManifestPath), occurrence.Arguments[1]);
        Assert.Equal(new QuantityValue(128), occurrence.Arguments[2]);
        Assert.Equal(new QuantityValue(129), occurrence.Arguments[3]);
        Assert.EndsWith(
            $"in '[csproj_properties]' of '{ManifestPath}' exceeds the maximum length of 128 characters (got 129).",
            occurrence.Message);
        Assert.Equal<CsprojProperty>([new CsprojProperty("After", "kept")], configuration.CsprojProperties);
    }

    [Fact]
    public void KeyOfExactly128Characters_IsAccepted()
    {
        var key = new string('K', 128);

        var (configuration, diagnostics) = Parse($"""
            [csproj_properties]
            {key} = "value"
            """);

        Assert.Empty(diagnostics);
        Assert.Equal<CsprojProperty>([new CsprojProperty(key, "value")], configuration.CsprojProperties);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[\"a\"]")]
    [InlineData("{ Inner = \"x\" }")]
    public void NonStringValue_IsRefusedAndSkipped(string value)
    {
        var (configuration, diagnostics) = Parse($"""
            [csproj_properties]
            UserSecretsId = {value}
            After = "kept"
            """);

        var occurrence = Assert.Single(diagnostics);
        AssertOccurrence(occurrence, "MIST0096", DiagnosticSeverity.Error,
            $"Property 'UserSecretsId' in '[csproj_properties]' of '{ManifestPath}' must have a string value.");
        Assert.Equal<SlotValue>([new NameValue("UserSecretsId"), new PathValue(ManifestPath)], occurrence.Arguments);
        Assert.Equal<CsprojProperty>([new CsprojProperty("After", "kept")], configuration.CsprojProperties);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    public void EmptyOrWhitespaceValue_IsRefusedAndSkipped(string value)
    {
        var (configuration, diagnostics) = Parse($"""
            [csproj_properties]
            UserSecretsId = {value}
            After = "kept"
            """);

        var occurrence = Assert.Single(diagnostics);
        AssertOccurrence(occurrence, "MIST0097", DiagnosticSeverity.Error,
            $"Property 'UserSecretsId' in '[csproj_properties]' of '{ManifestPath}' has an empty or whitespace value.");
        Assert.Equal<SlotValue>([new NameValue("UserSecretsId"), new PathValue(ManifestPath)], occurrence.Arguments);
        Assert.Equal<CsprojProperty>([new CsprojProperty("After", "kept")], configuration.CsprojProperties);
    }

    [Fact]
    public void EachCompilerManagedKey_InAnyCase_WarnsOnceAndIsStillKept()
    {
        // The near misses TargetFrameworks and NullableContext are ordinary properties and must not warn.
        var (configuration, diagnostics) = Parse("""
            [csproj_properties]
            outputtype = "Library"
            TARGETFRAMEWORK = "net9.0"
            TargetFrameworks = "net9.0;net10.0"
            ImplicitUsings = "disable"
            nullable = "disable"
            NullableContext = "x"
            Deterministic = "false"
            enableNetAnalyzers = "false"
            """);

        Assert.All(diagnostics, occurrence =>
        {
            Assert.Equal("MIST0098", occurrence.Number.Value);
            Assert.Equal(DiagnosticSeverity.Warning, occurrence.Severity);
            Assert.Equal(new FileOnly(ManifestPath), occurrence.Location);
        });
        Assert.Equal(
            ["outputtype", "TARGETFRAMEWORK", "ImplicitUsings", "nullable", "Deterministic", "enableNetAnalyzers"],
            diagnostics.Select(occurrence => ((NameValue)occurrence.Arguments[0]).Name));
        Assert.Equal(
            $"Property 'TARGETFRAMEWORK' in '[csproj_properties]' of '{ManifestPath}' overrides a compiler-managed property. This may cause unexpected build behavior.",
            diagnostics[1].Message);
        Assert.Equal<CsprojProperty>(
            [
                new CsprojProperty("outputtype", "Library"),
                new CsprojProperty("TARGETFRAMEWORK", "net9.0"),
                new CsprojProperty("TargetFrameworks", "net9.0;net10.0"),
                new CsprojProperty("ImplicitUsings", "disable"),
                new CsprojProperty("nullable", "disable"),
                new CsprojProperty("NullableContext", "x"),
                new CsprojProperty("Deterministic", "false"),
                new CsprojProperty("enableNetAnalyzers", "false"),
            ],
            configuration.CsprojProperties);
    }

    [Fact]
    public void EveryRefusal_SkipsOnlyItsEntry_AndTheSurvivorsKeepTheirOrder()
    {
        var (configuration, diagnostics) = Parse($"""
            [csproj_properties]
            First = "1"
            " " = "blank key"
            "Bad!" = "x"
            Second = "2"
            {new string('L', 130)} = "long"
            Number = 3
            Blank = ""
            Nullable = "enable"
            Third = "3"
            """);

        Assert.Equal(
            ["MIST0093", "MIST0094", "MIST0095", "MIST0096", "MIST0097", "MIST0098"],
            diagnostics.Select(occurrence => occurrence.Number.Value));
        Assert.Equal<CsprojProperty>(
            [new CsprojProperty("First", "1"), new CsprojProperty("Second", "2"), new CsprojProperty("Nullable", "enable"), new CsprojProperty("Third", "3")],
            configuration.CsprojProperties);
    }
}