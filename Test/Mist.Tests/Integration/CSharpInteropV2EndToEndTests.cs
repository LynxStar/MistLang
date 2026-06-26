using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Dedicated end-to-end integration test for the CSharpInteropV2 sample.
/// This is the ONE APPROVED process-spawning test for the csharp-interop-v2 spec.
/// It compiles the sample in-process via CompilerDriver, then spawns dotnet to run
/// the output DLL and asserts exit code 0 and byte-for-byte stdout match.
/// </summary>
public sealed class CSharpInteropV2EndToEndTests : IDisposable
{
    private readonly string _outputDir;

    public CSharpInteropV2EndToEndTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistInteropV2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CSharpInteropV2_CompilesAndProducesExpectedOutput()
    {
        // Arrange
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "CSharpInteropV2");
        Assert.True(Directory.Exists(sampleDir), $"CSharpInteropV2 sample directory not found at {sampleDir}");

        var sourceFiles = Directory.EnumerateFiles(sampleDir, "*.mist", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToImmutableArray();
        Assert.NotEmpty(sourceFiles);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var binDir = Path.Combine(_outputDir, "bin");
        var options = new CompilerOptions(
            ProjectFolder: sampleDir,
            SourcePaths: sourceFiles,
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: binDir,
            ProjectName: "CSharpInteropV2",
            NamespaceRoot: "CSharpInteropV2",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Packages: ImmutableArray.Create(
                new NuGetPackageReference("Newtonsoft.Json", "13.0.3"),
                new NuGetPackageReference("Npgsql.EntityFrameworkCore.PostgreSQL", "9.0.4")),
            FrameworkReferences: ImmutableArray.Create(new FrameworkReference("Microsoft.AspNetCore.App")),
            TargetFramework: "net10.0");

        // Act: compile in-process
        var result = driver.Compile(options);

        // Assert: compilation succeeds with 0 errors
        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"CSharpInteropV2 compilation produced {errors.Count} error(s):\n" +
            string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}")));
        Assert.True(result.Succeeded,
            $"CSharpInteropV2 compilation failed. Diagnostics:\n" +
            string.Join("\n", result.Diagnostics.Select(d => $"  [{d.Severity}] {d.Id.Value}: {d.Message}")));
        Assert.NotNull(result.PrimaryOutputArtifactPath);
        Assert.True(File.Exists(result.PrimaryOutputArtifactPath),
            $"Expected artifact at {result.PrimaryOutputArtifactPath}");

        // Act: run the compiled DLL (single approved process spawn)
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = result.PrimaryOutputArtifactPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);
        Assert.True(process.HasExited, "Process did not exit within timeout");

        // Assert: exit code 0
        Assert.True(process.ExitCode == 0,
            $"Process exited with code {process.ExitCode}.\nStderr:\n{stderr}\nStdout:\n{stdout}");

        // Assert: byte-for-byte stdout match
        const string expectedOutput =
            """
            --- C# Interop V2 ---
            json=[1,2,3]
            query=?id=42
            hasValue=True
            streamLength=2
            canRead=True
            encodedLength=2
            decoded=Hi
            efConfigured=True
            --- C# Interop V2 Done ---
            """;

        var normalizedExpected = NormalizeLineEndings(expectedOutput);
        var normalizedActual = NormalizeLineEndings(stdout.TrimEnd());

        Assert.True(normalizedExpected == normalizedActual,
            $"Stdout mismatch.\n\nExpected:\n{normalizedExpected}\n\nActual:\n{normalizedActual}");
    }

    private static string NormalizeLineEndings(string s)
        => s.Replace("\r\n", "\n").Replace("\r", "\n");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Directory.GetCurrentDirectory();
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }
}