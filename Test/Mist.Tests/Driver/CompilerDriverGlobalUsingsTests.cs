using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;

namespace Mist.Tests.Driver;

/// <summary>
/// The driver-to-binder seam for the root project's global usings. The program names <c>Console</c>
/// with no using of its own, so it binds only when the driver hands <c>Standard</c> to its unit.
/// </summary>
[Trait("Tier", "4")]
public sealed class CompilerDriverGlobalUsingsTests : IDisposable
{
    private const string Source = """
        namespace Probe;

        public class Program
        {
            public static void Main()
            {
                Console.WriteLine("reached");
            }
        }
        """;

    private static readonly CompilerDriver Driver = new(new MistLexer(), new MistParser(), ambientTypes: _ => StubAmbientTypeSource.Instance);

    private readonly string scratchDir = Path.Combine(Path.GetTempPath(), "mist-gu-driver-" + Guid.NewGuid().ToString("N"));

    public CompilerDriverGlobalUsingsTests()
    {
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch(IOException) { }
        }
    }

    [Fact]
    public void TheRootManifestsGlobalUsings_ReachItsOwnUnits()
    {
        Assert.Empty(Errors("name = \"Probe\"\n\n[global_usings]\nnamespaces = [\"Standard\"]\n", []));
    }

    [Fact]
    public void WithoutAManifest_TheOptionsGlobalUsingsApply()
    {
        Assert.Empty(Errors(manifest: null, ["Standard"]));
    }

    [Fact]
    public void AManifest_WinsOverTheOptions()
    {
        Assert.NotEmpty(Errors("name = \"Probe\"\n", ["Standard"]));
    }

    private ImmutableArray<Diagnostic> Errors(string? manifest, ImmutableArray<string> optionUsings)
    {
        if(manifest is not null)
        {
            File.WriteAllText(Path.Combine(scratchDir, "mist.toml"), manifest);
        }

        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, Source);

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: [sourcePath],
            Backend: BackendId.CSharp,
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            GlobalUsings: optionUsings);

        return [.. Driver.Compile(options).Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)];
    }
}