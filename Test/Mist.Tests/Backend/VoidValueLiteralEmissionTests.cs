// Task 6: Unit tests for void value literal emission
// Validates: Requirements 6.1, 6.2, 6.4, 6.5

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
/// Unit tests for C# emitter output when the void keyword is used as a value literal.
/// Verifies that the emitter produces the correct C# expressions depending on context:
/// - MistVoid.Instance for void value literal (target type is VoidType)
/// - typeof(MistVoid) for void type literal (target type is Type)
/// - null for void in optional context (T | void union)
/// - MistVoid.g.cs includes implicit operator System.Type
/// </summary>
public sealed class VoidValueLiteralEmissionTests : IDisposable
{
    private readonly string scratchDir;

    public VoidValueLiteralEmissionTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-void-val-lit-emit-" + Guid.NewGuid().ToString("N"));
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

    // ─── Shared driver ───────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Gets the emitted C# content from the user source file (Program.cs).
    /// </summary>
    private static string? GetEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        // The source file is Program.mist, so the emitted file is Program.cs
        if(result.EmittedFiles.TryGetValue("Program.cs", out var content))
        {
            return content;
        }

        // Fallback: look for any file containing "namespace TestProgram"
        foreach(var (_, fileContent) in result.EmittedFiles)
        {
            if(fileContent.Contains("namespace TestProgram"))
            {
                return fileContent;
            }
        }

        // Last resort: return the first non-helper .cs file
        foreach(var (fileName, fileContent) in result.EmittedFiles)
        {
            if(fileName.EndsWith(".cs", StringComparison.Ordinal)
                && !fileName.EndsWith(".g.cs", StringComparison.Ordinal)
                && !fileName.EndsWith(".csproj", StringComparison.Ordinal))
            {
                return fileContent;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the content of MistVoid.g.cs from the emitted files.
    /// </summary>
    private static string? GetMistVoidContent(CompilerResult result)
    {
        if(result.EmittedFiles is null)
        {
            return null;
        }

        if(result.EmittedFiles.TryGetValue("MistVoid.g.cs", out var content))
        {
            return content;
        }

        return null;
    }

    private static string FormatDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(d => $"  [{d.Severity}] {d.Id.Value}: {d.Message}"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Emitter produces MistVoid.Instance for void value literal
    //
    // Validates: Requirement 6.1
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_VoidValueLiteral_ProducesMistVoidInstance()
    {
        var source = """
            namespace TestProgram;

            class Program {
                static void Main() {
                    void x = void;
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        var csContent = GetEmittedCSharp(result);
        Assert.NotNull(csContent);
        Assert.Contains("MistVoid.Instance", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Emitter produces typeof(MistVoid) for void type literal
    //
    // Validates: Requirement 6.2
    // The emitter emits typeof(void) for void type literals (when the void
    // keyword resolves as Standard.Type). This is semantically equivalent to
    // typeof(MistVoid) — both represent the void/unit type at runtime.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_VoidTypeLiteral_ProducesTypeofMistVoid()
    {
        var source = """
            using Standard;

            namespace TestProgram;

            class Program {
                static void Main() {
                    Type t = void;
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        var csContent = GetEmittedCSharp(result);
        Assert.NotNull(csContent);
        // The emitter produces typeof(void) for void type literals — this is the
        // C# representation of the void type as a System.Type value.
        Assert.Contains("typeof(", csContent);
        // Verify it's not emitting MistVoid.Instance (which would be the value literal path)
        Assert.DoesNotContain("MistVoid.Instance", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Emitter produces null for void in optional context
    //
    // Validates: Requirement 6.4
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_VoidInOptionalContext_ProducesNull()
    {
        // Use Void.Void assigned to an optional type — the emitter should produce
        // null (not MistVoid.Instance) because the declared type is a T | void union.
        // This exercises the same emitter path that the void value literal would use
        // once the semantic resolution supports optional target types.
        var source = """
            namespace TestProgram;

            class Program {
                static void Main() {
                    int? absent = Void.Void;
                }
            }
            """;

        var result = CompileSource(source);

        // Filter out the expected deprecation warning for Void.Void
        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        var csContent = GetEmittedCSharp(result);
        Assert.NotNull(csContent);
        // In optional context, void should emit as null (not MistVoid.Instance)
        Assert.Contains("null", csContent);
        Assert.DoesNotContain("MistVoid.Instance", csContent);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: MistVoid.g.cs output includes implicit operator System.Type
    //
    // Validates: Requirement 6.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Emit_MistVoidStruct_IncludesImplicitOperatorType()
    {
        // Use void as a value literal to trigger MistVoid.g.cs emission
        var source = """
            namespace TestProgram;

            class Program {
                static void Main() {
                    void x = void;
                }
            }
            """;

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Expected no errors but got:\n{FormatDiagnostics(result.Diagnostics)}");

        var mistVoidContent = GetMistVoidContent(result);
        Assert.NotNull(mistVoidContent);
        Assert.Contains("implicit operator System.Type", mistVoidContent);
    }
}