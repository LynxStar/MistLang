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

/// <summary>
/// Verifies that the EntryPointAnalyzer's Rule 5 correctly emits MIST_EP_0006
/// when a top-level statement file contains a block namespace declaration, and
/// does NOT emit it when a file-scoped namespace is used instead.
///
/// Validates: Requirements 1.5, 1.6
/// </summary>
public sealed class EntryPointBlockNamespaceTests : IDisposable
{
    private readonly string scratchDir;

    public EntryPointBlockNamespaceTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-ep-block-ns-" + Guid.NewGuid().ToString("N"));
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

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer(), new EntryPointAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private CompilerResult CompileSource(string source, string fileName = "Program.mist", string? entryPoint = null)
    {
        var filePath = Path.Combine(scratchDir, fileName);
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EntryPoint: entryPoint);

        return SharedDriver.Compile(options);
    }

    [Fact]
    public void TlsWithBlockNamespace_EmitsMIST_EP_0006()
    {
        var source = """
            var x = 1;
            namespace Foo {
                class Bar { }
            }
            """;

        var result = CompileSource(source, entryPoint: "top-level");

        Assert.Contains(result.Diagnostics,
            d => d.Id.Value == "MIST_EP_0006");
    }

    [Fact]
    public void TlsWithFileScopedNamespace_DoesNotEmitMIST_EP_0006()
    {
        var source = """
            namespace Foo;
            var x = 1;
            class Bar { }
            """;

        var result = CompileSource(source, entryPoint: "top-level");

        Assert.DoesNotContain(result.Diagnostics,
            d => d.Id.Value == "MIST_EP_0006");
    }
}