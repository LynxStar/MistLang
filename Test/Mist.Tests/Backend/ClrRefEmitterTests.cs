// Unit tests for CSharpEmitter ref/in keyword emission at CLR interop call sites.
//
// **Validates: Requirements 2.4, 3.4, 5.4**

using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests verifying that the CSharpEmitter emits the correct <c>ref</c> or <c>in</c>
/// keyword at call sites when Mist code calls CLR methods with ref/in parameters,
/// and that ref return call expressions are prefixed with <c>ref</c>.
///
/// These tests compile Mist source in-process using the shared CompilerDriver with
/// EmitToDisk: false, then inspect the emitted C# text for the expected keywords.
///
/// **Validates: Requirements 2.4, 3.4, 5.4**
/// </summary>
public sealed class ClrRefEmitterTests : IDisposable
{
    private readonly string _outputDir;

    public ClrRefEmitterTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistClrRefEmitter_" + Guid.NewGuid().ToString("N"));
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

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source, string namespaceName = "TestProgram")
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        // Write a minimal mist.toml so the compiler triggers the full project resolution
        // path (which enables proper global usings and assembly loading).
        var tomlPath = Path.Combine(_outputDir, "mist.toml");
        if(!File.Exists(tomlPath))
            File.WriteAllText(tomlPath, $"name = \"{namespaceName}\"\n");

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: namespaceName,
            NamespaceRoot: namespaceName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Gets the emitted C# content from the compilation result.
    /// Returns the content of the user file (the one containing the test namespace).
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result, string namespaceName = "TestProgram")
    {
        if(result.EmittedFiles is null)
            return null;

        foreach(var (_, content) in result.EmittedFiles)
        {
            if(content.Contains($"namespace {namespaceName}"))
                return content;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EmitterEmitsInForClrInParam
    //
    // Verifies that when Mist code calls a CLR method with an `in` parameter
    // (e.g., Volatile.Read(in int)), the emitted C# uses the `in` keyword
    // at the call site, not `ref`.
    //
    // **Validates: Requirement 3.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitterEmitsInForClrInParam()
    {
        // Interlocked.Read(in long location) is a CLR method with an `in` parameter.
        // In Mist, the user writes `ref` at the call site (since `in` is projected as `ref`),
        // but the emitter must emit `in` in the generated C#.
        var source = """
            using Standard;
            using System.Threading;

            namespace TestProgram;

            class Program {
                public static void Main() {
                    long x = 42;
                    long result = Interlocked.Read(ref x);
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        var emittedCSharp = GetEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        // The emitted C# should contain "in x" (not "ref x") at the Interlocked.Read call site.
        Assert.Contains("in x", emittedCSharp);
        // Verify the full call expression uses `in`.
        Assert.Contains("Interlocked.Read(in x)", emittedCSharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EmitterEmitsRefForClrRefParam
    //
    // Verifies that when Mist code calls a CLR method with a `ref` parameter
    // (e.g., Interlocked.Exchange(ref int, int)), the emitted C# uses the
    // `ref` keyword at the call site.
    //
    // **Validates: Requirement 2.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitterEmitsRefForClrRefParam()
    {
        // Interlocked.Add(ref int location1, int value) is a CLR method with a `ref` parameter.
        // It has no generic overload, avoiding ambiguity in overload resolution.
        // The emitter must emit `ref` at the call site in the generated C#.
        var source = """
            using Standard;
            using System.Threading;

            namespace TestProgram;

            class Program {
                public static void Main() {
                    int value = 42;
                    int result = Interlocked.Add(ref value, 10);
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        var emittedCSharp = GetEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        // The emitted C# should contain "ref value" at the Interlocked.Add call site.
        Assert.Contains("ref value", emittedCSharp);
        // Verify the full call expression uses `ref`.
        Assert.Contains("Interlocked.Add(ref value, 10)", emittedCSharp);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // EmitterEmitsRefOnCallExpressionForRefReturnMethod
    //
    // Verifies that when Mist code calls a method with a `ref` return and
    // binds the result to a `ref` local, the emitted C# uses `ref` on the
    // call expression (e.g., `ref int r = ref obj.GetRef()`).
    //
    // **Validates: Requirement 5.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EmitterEmitsRefOnCallExpressionForRefReturnMethod()
    {
        // A Mist method with a ref return: when the result is bound to a ref local,
        // the emitted C# must have `ref` on both the local declaration and the call expression.
        var source = """
            namespace TestProgram;

            class Container {
                int stored = 10;

                public ref int GetRef() {
                    return ref this.stored;
                }
            }

            class Program {
                public static void Main() {
                    var c = new Container();
                    ref int r = c.GetRef();
                }
            }
            """;

        var result = CompileSource(source);

        Assert.True(result.Succeeded,
            $"Compilation failed: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}");

        var emittedCSharp = GetEmittedCSharp(result);
        Assert.NotNull(emittedCSharp);

        // The emitted C# should contain a ref local declaration with ref on the initializer.
        // Expected pattern: "ref int r = ref c.GetRef()" or similar.
        Assert.Contains("ref int r", emittedCSharp);
        // The call expression should be prefixed with `ref` (the ref-return call site).
        Assert.Contains("ref c.GetRef()", emittedCSharp);
    }
}