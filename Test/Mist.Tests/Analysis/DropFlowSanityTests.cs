using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

public sealed class DropFlowSanityTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_drop_flow_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "Program.mist");
            File.WriteAllText(sourcePath, source);
            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);
            return SharedDriver.Compile(options).Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void LinearUseAfterDrop_Reported()
    {
        var src = """
            linear class FileHandle
            {
                public void Drop()
                {
                }

                public int GetFd()
                {
                    return 1;
                }
            }
            class Program
            {
                public static void Main()
                {
                    FileHandle f = new FileHandle();
                    f.Drop();
                    int x = f.GetFd();
                }
            }
            """;
        var diags = Compile(src);
        Assert.True(diags.Any(d => d.Id.Value == "MIST_DROP_USE_AFTER_MOVE"),
            "Diagnostics: " + string.Join("; ", diags.Select(d => d.Id.Value + ":" + d.Message)));
    }

    [Fact]
    public void RelevantDropDoesNotConsume_NoError()
    {
        var src = """
            relevant class SharedResource
            {
                public void Drop()
                {
                }

                public int GetId()
                {
                    return 1;
                }
            }
            class Program
            {
                public static void Main()
                {
                    SharedResource r = new SharedResource();
                    r.Drop();
                    int x = r.GetId();
                }
            }
            """;
        var diags = Compile(src);
        Assert.DoesNotContain(diags, d => d.Id.Value is "MIST_DROP_USE_AFTER_MOVE" or "MIST_DROP_USE_AFTER_DROP");
    }
}