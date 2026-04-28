using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Diagnostics;
using Mist.Common.Diagnostics;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Invokes <c>dotnet build</c> on the emitted C# project directory,
/// captures stdout/stderr, and delegates diagnostic parsing to
/// <see cref="RoslynDiagnosticMapper"/>.
/// </summary>
public sealed class CSharpCompilationDriver
{
    public static BackendResult Compile(string projectDirectory, string binDirectory, string projectName, DiagnosticBag diagnostics)
    {
        var absoluteBinDir = Path.GetFullPath(binDirectory);

        if (!Directory.Exists(absoluteBinDir))
        {
            Directory.CreateDirectory(absoluteBinDir);
        }

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build --output \"{absoluteBinDir}\"",
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(psi);
            if(process is null)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_BUILD_ERROR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Backend,
                    "Failed to start dotnet build process.",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: null));

                return new BackendResult(false, null, diagnostics.ToImmutableArray());
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // Parse MSBuild diagnostic lines from output, keeping only
            // diagnostics whose file path is within the emitted project directory.
            var normalizedProjectDir = Path.GetFullPath(projectDirectory) + Path.DirectorySeparatorChar;
            var allOutput = stdout + "\n" + stderr;
            foreach(var line in allOutput.Split('\n'))
            {
                var diagnostic = RoslynDiagnosticMapper.TryParse(line);
                if(diagnostic is not null
                    && diagnostic.CompilationUnitPath is not null
                    && Path.GetFullPath(diagnostic.CompilationUnitPath).StartsWith(normalizedProjectDir, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(diagnostic);
                }
            }

            var succeeded = process.ExitCode == 0;
            string? artifactPath = succeeded
                ? Path.Combine(absoluteBinDir, $"{projectName}.dll")
                : null;

            return new BackendResult(succeeded, artifactPath, diagnostics.ToImmutableArray());
        }
        catch(Exception ex)
        {
            diagnostics.Add(new Diagnostic(
                new DiagnosticId("MIST_BUILD_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                $"dotnet build failed: {ex.Message}",
                Location: null,
                CompilationUnitPath: null,
                AnalyzerName: null));

            return new BackendResult(false, null, diagnostics.ToImmutableArray());
        }
    }
}
