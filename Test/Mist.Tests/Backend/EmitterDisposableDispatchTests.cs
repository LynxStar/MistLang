// Unit tests for emitter dispatch: .Dispose() vs .Drop() at drop emission sites.
// **Validates: Requirements 3.1, 3.2, 3.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for the C# emitter's dispatch of <c>.Dispose()</c> vs <c>.Drop()</c>
/// at drop emission sites. Compiles minimal Mist programs in-process and inspects the
/// emitted C# text to verify correct dispatch.
///
/// Requirements: 3.1, 3.2, 3.4
///
/// Spawns 0 external processes. All compilation is in-process via <see cref="CompilerDriver"/>
/// with <c>EmitToDisk: false</c>.
/// </summary>
public sealed class EmitterDisposableDispatchTests : IDisposable
{
    private readonly string _outputDir;

    public EmitterDisposableDispatchTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistEmitterDisposableDispatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    // ─── Shared driver (includes DropAnalyzer for with-block emission) ───

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new Mist.Semantics.Model.SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Compilation helper ──────────────────────────────────────────────

    private string CompileAndGetEmittedCSharp(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);

        var errors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            $"Compilation produced errors:\n{string.Join("\n", errors.Select(e => $"  {e.Id.Value}: {e.Message}"))}");

        Assert.NotNull(result.EmittedFiles);
        foreach(var (_, content) in result.EmittedFiles!)
        {
            if(content.Contains("namespace TestProgram"))
            {
                return content;
            }
        }

        Assert.Fail("No emitted C# file containing 'namespace TestProgram' was found.");
        return ""; // unreachable
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: IsDisposableDrop=true → emitted string contains .Dispose()
    // Requirement 3.1
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_IDisposableType_EmitsDispose()
    {
        // MemoryStream implements IDisposable → the emitter should emit .Dispose()
        const string source = """
            using System.IO;

            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    with (var ms = new MemoryStream())
                    {
                        ms.WriteByte(42);
                    }
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(source);

        Assert.Contains(".Dispose()", emittedCSharp);
        Assert.DoesNotContain(".Drop()", emittedCSharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: IsDisposableDrop=false, HasDrop=true → emitted string contains .Drop()
    // Requirement 3.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_ExplicitDropType_EmitsDrop()
    {
        // A Mist type with explicit Drop() should emit .Drop(), not .Dispose()
        const string source = """
            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    with (var r = new MyResource())
                    {
                        r.DoWork();
                    }
                }
            }

            public linear class MyResource
            {
                public void Drop()
                {
                }

                public void DoWork()
                {
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(source);

        Assert.Contains(".Drop()", emittedCSharp);
        Assert.DoesNotContain(".Dispose()", emittedCSharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: ConstructedType wrapping IsDisposableDrop=true → .Dispose() emitted
    // Requirement 3.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_GenericIDisposableType_EmitsDispose()
    {
        // System.IO.StreamReader implements IDisposable (transitively via TextReader)
        // This tests that constructed/concrete instantiations of disposable types
        // still correctly dispatch to .Dispose()
        const string source = """
            using System.IO;

            namespace TestProgram;

            public class Program
            {
                public static void Main(string[] args)
                {
                    var inner = new MemoryStream();
                    with (var reader = new StreamReader(inner))
                    {
                        var line = reader.ReadLine();
                    }
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(source);

        Assert.Contains(".Dispose()", emittedCSharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test: TypeAliasType wrapping IsDisposableDrop=true → .Dispose() emitted
    // Requirement 3.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void WithBlock_TypeAlias_OfIDisposableType_EmitsDispose()
    {
        // A type alias wrapping an IDisposable CLR type should still emit .Dispose()
        const string source = """
            using System.IO;

            namespace TestProgram;

            type StreamAlias = MemoryStream;

            public class Program
            {
                public static void Main(string[] args)
                {
                    with (var ms = new StreamAlias())
                    {
                        ms.WriteByte(42);
                    }
                }
            }
            """;

        var emittedCSharp = CompileAndGetEmittedCSharp(source);

        Assert.Contains(".Dispose()", emittedCSharp);
        Assert.DoesNotContain(".Drop()", emittedCSharp);
    }
}