// Feature: stack-only-types, Property 21: Frame-escape soundness — accept on safe use
//
// **Validates: Requirements 8.4, 8.6, 9.4**
//
// In-process; generators enumerate safe shapes (local/read/write/slice/foreach/call-arg)
// and NotStackBacked sources (parameter, AsSpan(), inbound projection); assert no
// MIST2008/MIST2009 is emitted.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
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

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that safe in-frame uses of stackalloc-backed spans
/// and all NotStackBacked span values never trigger frame-escape diagnostics
/// (MIST2008/MIST2009).
///
/// This is Property 21: Frame-escape soundness — accept on safe use.
///
/// Safe shapes (StackBacked but in-frame only):
///   - Local variable declaration and use
///   - Indexer read
///   - Indexer write
///   - Slice within same frame
///   - foreach iteration
///   - Passing as method argument (borrowed for call duration)
///
/// NotStackBacked sources (never flagged):
///   - Parameters typed as spans
///   - Results from method calls returning spans (e.g., AsSpan())
///   - Inbound projection (array.AsSpan() identity)
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance
/// with both TypeCheckerAnalyzer and FrameEscapeAnalyzer enabled.
/// Zero process spawns.
///
/// **Validates: Requirements 8.4, 8.6, 9.4**
/// </summary>
public class FrameEscapeSafeUsePropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer(), new FrameEscapeAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Safe-use shape model ────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Enumerates the safe in-frame use shapes for stackalloc-backed spans
    /// and NotStackBacked span sources that must never trigger MIST2008/MIST2009.
    /// </summary>
    public enum SafeUseShape
    {
        /// <summary>stackalloc into a local variable, then read it (R8.6)</summary>
        LocalRead,
        /// <summary>stackalloc into a local, write through indexer (R8.6)</summary>
        IndexerWrite,
        /// <summary>stackalloc into a local, read through indexer (R8.6)</summary>
        IndexerRead,
        /// <summary>stackalloc into a local, slice within same frame (R8.6)</summary>
        SliceInFrame,
        /// <summary>stackalloc into a local, foreach over the span (R8.6)</summary>
        ForeachIteration,
        /// <summary>stackalloc into a local, pass as method argument (R8.6)</summary>
        CallArgument,
        /// <summary>Parameter typed as Span — NotStackBacked, never flagged (R9.4)</summary>
        ParameterSpan,
        /// <summary>Array.AsSpan() result — NotStackBacked, never flagged (R9.4)</summary>
        AsSpanResult,
        /// <summary>Parameter typed as ReadOnlySpan — NotStackBacked, never flagged (R9.4)</summary>
        ParameterReadOnlySpan,
    }

    /// <summary>
    /// A test scenario pairing a safe-use shape with an element type.
    /// </summary>
    public sealed record SafeUseScenario(
        SafeUseShape Shape,
        string ElementType,
        int Size,
        string Description)
    {
        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ElementTypes =
        ["int", "uint", "long", "byte", "short", "float", "double", "bool", "char"];

    private static Gen<SafeUseScenario> GenScenario()
    {
        var shapes = Enum.GetValues<SafeUseShape>();
        return from shape in Gen.Elements(shapes)
               from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               from size in Gen.Choose(1, 32)
               let elementType = ElementTypes[typeIdx]
               select new SafeUseScenario(
                   shape, elementType, size,
                   $"{shape} with Span<{elementType}>[{size}]");
    }

    public static Arbitrary<SafeUseScenario> ArbitrarySafeUseScenario() =>
        GenScenario().ToArbitrary();

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildProgram(SafeUseScenario scenario) => scenario.Shape switch
    {
        SafeUseShape.LocalRead => $$"""
            class Program {
                static void Main() {
                    var s = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    int len = s.Length;
                }
            }
            """,

        SafeUseShape.IndexerWrite => $$"""
            class Program {
                static void Main() {
                    var s = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    s[0] = default;
                }
            }
            """,

        SafeUseShape.IndexerRead => $$"""
            class Program {
                static void Main() {
                    var s = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    var x = s[0];
                }
            }
            """,

        SafeUseShape.SliceInFrame => $$"""
            class Program {
                static void Main() {
                    var s = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    var mid = s.Slice(0, {{Math.Min(scenario.Size, 1)}});
                    int len = mid.Length;
                }
            }
            """,

        SafeUseShape.ForeachIteration => $$"""
            class Program {
                static void Main() {
                    var s = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    foreach ({{scenario.ElementType}} v in s) {
                    }
                }
            }
            """,

        SafeUseShape.CallArgument => $$"""
            class Program {
                static void Consume(Span<{{scenario.ElementType}}> arg) {
                    int len = arg.Length;
                }
                static void Main() {
                    var s = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    Consume(s);
                }
            }
            """,

        SafeUseShape.ParameterSpan => $$"""
            class Program {
                static void UseParam(Span<{{scenario.ElementType}}> p) {
                    int len = p.Length;
                    var x = p[0];
                }
                static void Main() {
                    var s = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    UseParam(s);
                }
            }
            """,

        SafeUseShape.AsSpanResult => $$"""
            class Program {
                static void Main() {
                    {{scenario.ElementType}}[] arr = new {{scenario.ElementType}}[{{scenario.Size}}];
                    Span<{{scenario.ElementType}}> view = arr.AsSpan();
                    int len = view.Length;
                }
            }
            """,

        SafeUseShape.ParameterReadOnlySpan => $$"""
            class Program {
                static void UseParam(ReadOnlySpan<{{scenario.ElementType}}> p) {
                    int len = p.Length;
                    var x = p[0];
                }
                static void Main() {
                    ReadOnlySpan<{{scenario.ElementType}}> ro = stackalloc {{scenario.ElementType}}[{{scenario.Size}}];
                    UseParam(ro);
                }
            }
            """,

        _ => throw new ArgumentOutOfRangeException(),
    };

    // ─── Compilation helper ──────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
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
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 21: Frame-escape soundness — accept on safe use
    //
    // For any program in which a span value is used only within its
    // allocating frame (local/read/write/slice/foreach/call-arg) or in
    // which the span is NotStackBacked (parameter, AsSpan() result,
    // inbound projection), no MIST2008/MIST2009 is emitted.
    //
    // **Validates: Requirements 8.4, 8.6, 9.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(FrameEscapeSafeUsePropertyTests) })]
    public Property SafeUseAndNotStackBacked_NeverTriggerFrameEscapeDiagnostics(SafeUseScenario scenario)
    {
        var source = BuildProgram(scenario);
        var diagnostics = CompileAndGetDiagnostics(source);

        // Frame-escape diagnostics that must NOT appear.
        var frameEscapeIds = new HashSet<string> { "MIST2008", "MIST2009" };
        var frameEscapeHits = diagnostics
            .Where(d => frameEscapeIds.Contains(d.Id.Value))
            .ToList();

        var passed = frameEscapeHits.Count == 0;

        return passed.ToProperty()
            .Label(
                $"Safe-use shape: {scenario.Shape}\n" +
                $"  Element type: {scenario.ElementType}, Size: {scenario.Size}\n" +
                $"  MIST2008/MIST2009 hits: {frameEscapeHits.Count}\n" +
                $"  Frame-escape diagnostics: [{string.Join(", ", frameEscapeHits.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"  All errors: [{string.Join(", ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"  Source:\n{source}");
    }
}
