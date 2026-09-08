using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 4, the boundary from an AMBIENT CLR signature to lambda inference: that a lambda passed to
/// a framework method taking a delegate reads its parameter types from that delegate.
/// <para>
/// AGAINST THE REAL CLR, because the shape only exists there. Ambient projection runs at Phase 1b,
/// before Phase 2a resolves any alias, so a projected System.Action maps onto Standard.Action while
/// its target is still null and can only be constructed OVER the alias. A Mist-written
/// <c>Action&lt;int&gt;</c> never takes that path -- it is expanded at resolution -- so no synthetic
/// fixture reproduces the case.
/// </para>
/// </summary>
public sealed class ClrDelegateLambdaTests : IDisposable
{
    private readonly string scratchDir;

    public ClrDelegateLambdaTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-clrlambda-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The parameter is UNANNOTATED, which is the whole test: annotating it always worked, because
    /// then nothing has to be read off the target.
    /// </summary>
    private const string PassesALambdaToAClrMethod = """
        using Standard;
        using System.Collections.Generic;

        namespace Clr;

        public class Program
        {
            public static void Main()
            {
                List<int> items = new List<int>();
                items.Add(10);
                items.Add(20);

                items.ForEach(item => Console.WriteLine($"item={item}"));
            }
        }
        """;

    [Fact]
    public void ALambdaTargetingAClrDelegateInfersItsParameters()
    {
        Assert.Equal("item=10\nitem=20", CompileAndRun(PassesALambdaToAClrMethod).ReplaceLineEndings("\n"));
    }

    private string CompileAndRun(string source)
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, source);

        var driver = new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            new SemanticModelBuilder(),
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Clr",
            NamespaceRoot: "Clr",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Pipeline: PipelineMode.Experimental));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Lambda inference failed:\n{string.Join("\n", errors)}");
        Assert.NotNull(result.PrimaryOutputArtifactPath);

        return Run(result.PrimaryOutputArtifactPath);
    }

    private static string Run(string artifact)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = artifact,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        _ = process.WaitForExit(60000);

        return stdout.Trim();
    }
}