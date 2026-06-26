// Feature: stack-only-types, Task 14.1
// In-process Roslyn emission compile verification (zero CS warnings)
// **Validates: Requirements 6.7**

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Verifies that representative Mist Span programs emit C# that compiles
/// with zero errors and zero CS-prefixed warnings, validating R6.7
/// (zero-warnings policy) entirely in-process via Roslyn
/// <see cref="CSharpCompilation.Emit"/> — 0 <c>dotnet</c> spawns.
///
/// Strategy:
/// 1. Emit N representative Mist span programs through the CompilerDriver
///    (in-memory, no disk writes).
/// 2. Concatenate all emitted C# into ONE Roslyn compilation.
/// 3. Compile via CSharpCompilation.Emit against the same reference set
///    the CSharpCompilationDriver uses (CoreReferences).
/// 4. Assert zero Roslyn errors and zero CS-prefixed warnings.
/// </summary>
public sealed class StackOnlyTypesEmissionCompileTests : IDisposable
{
    private readonly string _outputDir;

    public StackOnlyTypesEmissionCompileTests()
    {
        _outputDir = Path.Combine(
            Path.GetTempPath(),
            "MistSpanEmitCompile_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across programs) ─────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Representative Mist programs ───────────────────────────────────

    /// <summary>
    /// Returns N representative Mist span programs exercising the span
    /// patterns that the current compiler implementation handles:
    /// stackalloc Span/ReadOnlySpan, indexer read/write, struct element,
    /// and Length/Slice/foreach/AsSpan identity.
    ///
    /// Each program uses a unique class name. Programs use `var` for
    /// stackalloc declarations (which infers Span&lt;T, N&gt;) and the
    /// arity-2 form for explicit ReadOnlySpan targets.
    /// </summary>
    private static IReadOnlyList<(string Name, string Source)> GetRepresentativePrograms()
    {
        return new (string, string)[]
        {
            // stackalloc Span<int> with indexer write and read
            ("StackallocSpanIndexer", """
                class StackallocSpanIndexerProgram
                {
                    static void Main()
                    {
                        var buffer = stackalloc int[4];
                        buffer[0] = 10;
                        buffer[1] = 20;
                        buffer[2] = 30;
                        buffer[3] = 40;
                        Console.WriteLine(buffer[0]);
                        Console.WriteLine(buffer[3]);
                    }
                }
                """),

            // Multiple indexer reads/writes to exercise thorough coverage
            ("IndexerReadWrite", """
                class IndexerReadWriteProgram
                {
                    static void Main()
                    {
                        var s = stackalloc int[3];
                        s[0] = 100;
                        s[1] = 200;
                        s[2] = 300;
                        var x = s[0];
                        var y = s[1];
                        var z = s[2];
                        Console.WriteLine($"{x},{y},{z}");
                    }
                }
                """),

            // Stampable user struct as stackalloc element type
            ("StructElement", """
                public struct PointForSpan
                {
                    public int x;
                    public int y;
                    public PointForSpan(int a, int b)
                    {
                        this.x = a;
                        this.y = b;
                    }
                }
                class StructElementProgram
                {
                    static void Main()
                    {
                        var pts = stackalloc PointForSpan[2];
                        pts[0] = new PointForSpan(1, 2);
                        pts[1] = new PointForSpan(3, 4);
                        Console.WriteLine(pts[0].x);
                        Console.WriteLine(pts[1].y);
                    }
                }
                """),

            // ReadOnlySpan target with indexer read (no write)
            ("ReadOnlySpanStackalloc", """
                class ReadOnlySpanProgram
                {
                    static void Main()
                    {
                        ReadOnlySpan<int, unknown> ro = stackalloc int[3];
                        Console.WriteLine(ro[0]);
                    }
                }
                """),

            // stackalloc with various primitive types (byte, char, double)
            ("MultiTypeStackalloc", """
                class MultiTypeStackallocProgram
                {
                    static void Main()
                    {
                        var bytes = stackalloc byte[8];
                        bytes[0] = 255;
                        Console.WriteLine(bytes[0]);

                        var chars = stackalloc char[4];
                        chars[0] = 'A';
                        Console.WriteLine(chars[0]);

                        var doubles = stackalloc double[2];
                        doubles[0] = 3.14;
                        Console.WriteLine(doubles[0]);
                    }
                }
                """),

            // Span.Length property access
            ("SpanLength", """
                class SpanLengthProgram
                {
                    static void Main()
                    {
                        var s = stackalloc int[7];
                        Console.WriteLine(s.Length);
                    }
                }
                """),

            // Span.Slice returns a span over the same memory
            ("SpanSlice", """
                class SpanSliceProgram
                {
                    static void Main()
                    {
                        var buffer = stackalloc int[5];
                        buffer[0] = 1;
                        buffer[1] = 2;
                        buffer[2] = 3;
                        buffer[3] = 4;
                        buffer[4] = 5;
                        var mid = buffer.Slice(1, 3);
                        Console.WriteLine(mid[0]);
                    }
                }
                """),

            // foreach iteration over a Span
            ("SpanForeach", """
                class SpanForeachProgram
                {
                    static void Main()
                    {
                        var s = stackalloc int[3];
                        s[0] = 10;
                        s[1] = 20;
                        s[2] = 30;
                        int total = 0;
                        foreach (int v in s)
                        {
                            total = total + v;
                        }
                        Console.WriteLine(total);
                    }
                }
                """),

            // Array.AsSpan() returning System.Span<int> unified with Mist Span
            ("AsSpanIdentity", """
                class AsSpanIdentityProgram
                {
                    static void Main()
                    {
                        int[] arr = new int[3];
                        arr[0] = 7;
                        arr[1] = 8;
                        arr[2] = 9;
                        var view = arr.AsSpan();
                        Console.WriteLine(view[2]);
                    }
                }
                """),
        };
    }

    // ─── Test ───────────────────────────────────────────────────────────

    [Fact]
    public void EmittedSpanPrograms_CompileWithZeroErrorsAndZeroCsWarnings()
    {
        var programs = GetRepresentativePrograms();
        var emittedSyntaxTrees = new List<SyntaxTree>();
        var succeededPrograms = new List<string>();
        var failedPrograms = new List<string>();

        // Step 1: Compile each Mist program via CompilerDriver (in-memory)
        // and collect the emitted C# files as individual syntax trees.
        // Infrastructure files (MistVoid.g.cs, Unknown.cs, IClonable.cs) are
        // emitted identically by each program, so we deduplicate by filename
        // to combine all programs into ONE valid compilation.
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach(var (name, source) in programs)
        {
            var result = CompileSource(source, name);

            if(!result.Succeeded || result.EmittedFiles is null)
            {
                var diagnostics = string.Join("\n",
                    result.Diagnostics
                        .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
                        .Select(d => $"  {d.Id.Value}: {d.Message}"));
                failedPrograms.Add($"[{name}]: {diagnostics}");
                continue;
            }

            succeededPrograms.Add(name);

            // Each emitted file becomes its own syntax tree.
            // Deduplicate shared infrastructure files (MistVoid.g.cs, Unknown.cs, etc.)
            // by only including the first occurrence of each filename.
            foreach(var (fileName, content) in result.EmittedFiles)
            {
                if(!seenFileNames.Add(fileName))
                {
                    continue; // already included from a previous program
                }

                emittedSyntaxTrees.Add(CSharpSyntaxTree.ParseText(
                    content,
                    CSharpParseOptions.Default.WithLanguageVersion(
                        Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview),
                    path: $"{name}/{fileName}"));
            }
        }

        // We need at least some programs to emit C# for this test to be meaningful
        Assert.True(
            succeededPrograms.Count >= 2,
            $"Fewer than 2 programs compiled successfully. " +
            $"Passed: [{string.Join(", ", succeededPrograms)}]. " +
            $"Failed:\n{string.Join("\n", failedPrograms)}");

        // Step 2: Compile via Roslyn using the same reference set as CSharpCompilationDriver.
        // Use DynamicallyLinkedLibrary to avoid the multiple-entry-point issue
        // (each program emits a Main, but we only care that the C# compiles cleanly).
        var compilation = CSharpCompilation.Create(
            assemblyName: "StackOnlyTypesEmissionTest",
            syntaxTrees: emittedSyntaxTrees,
            references: CSharpCompilationDriver.CoreReferences,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Enable,
                allowUnsafe: true,
                deterministic: true));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);

        // Step 3: Assert zero errors
        var errors = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(
            errors.Count == 0,
            $"Roslyn compilation produced {errors.Count} error(s) " +
            $"(programs: [{string.Join(", ", succeededPrograms)}]):\n" +
            string.Join("\n", errors.Select(e => $"  {e.Id}: {e.GetMessage()}\n    at {e.Location}")));

        // Step 4: Assert zero CS-prefixed warnings (R6.7 zero-warnings policy)
        var csWarnings = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning &&
                        d.Id.StartsWith("CS", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            csWarnings.Count == 0,
            $"Roslyn compilation produced {csWarnings.Count} CS-prefixed warning(s) (violates R6.7):\n" +
            string.Join("\n", csWarnings.Select(w => $"  {w.Id}: {w.GetMessage()}\n    at {w.Location}")));
    }

    // ─── Compilation helper ─────────────────────────────────────────────

    private CompilerResult CompileSource(string source, string projectName)
    {
        // Write the source to a temp file for the CompilerDriver
        var filePath = Path.Combine(_outputDir, $"{projectName}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false,
            GlobalUsings: ImmutableArray.Create("Standard"));

        return SharedDriver.Compile(options);
    }
}