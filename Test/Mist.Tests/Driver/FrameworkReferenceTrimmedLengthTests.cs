using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class FrameworkReferenceTrimmedLengthTests
{
    [Fact]
    public void PaddedNameAtTrimmedLimit_IsAcceptedAndNormalized()
    {
        var expectedName = new string('a', 256);
        var projectDir = Directory.CreateTempSubdirectory("mist-fwref-trim-");
        try
        {
            File.WriteAllText(Path.Combine(projectDir.FullName, ProjectFileParser.FileName), $"""
                name = "MyApp"

                [framework_references]
                "  {expectedName}  " = true
                """);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir.FullName, diagnostics);

            Assert.NotNull(result);
            Assert.Empty(diagnostics.ToImmutableArray());
            Assert.Equal(expectedName, Assert.Single(result.FrameworkReferences).Name);
        }
        finally
        {
            projectDir.Delete(recursive: true);
        }
    }
}