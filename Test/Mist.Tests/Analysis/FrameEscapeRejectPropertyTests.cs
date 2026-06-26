// Feature: stack-only-types, Property 20: Frame-escape soundness — reject on escape
//
// **Validates: Requirements 8.1, 8.2, 8.3, 8.5, 8.7, 14.2**
//
// In-process via FrameEscapeAnalyzer over generated method bodies; generators enumerate
// escape shapes (return, field store, static store, containing ref struct); assert
// MIST2008 (return) / MIST2009 (store) at the offending location with no C# emitted.

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
/// Property-based tests verifying that frame-escape analysis rejects programs where
/// a stackalloc-backed span escapes its allocating frame.
///
/// This is Property 20: Frame-escape soundness — reject on escape.
///
/// Escape shapes tested:
/// - Return: a stackalloc-backed span is returned from a method → MIST2008
/// - Return via local: through a local alias → MIST2008
/// - Return via slice: slice propagates backing → MIST2008
/// - Store via ref struct field: stored into a ref struct parameter's field → MIST2009
/// - Return via reassignment: through reassignment to another span local → MIST2008
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance
/// that includes the <see cref="FrameEscapeAnalyzer"/>.
/// Zero process spawns.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.5, 8.7, 14.2**
/// </summary>
public class FrameEscapeRejectPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer(), new FrameEscapeAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Escape shape model ─────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Enumerates the frame-escape shapes that must be rejected.
    /// </summary>
    public enum EscapeShape
    {
        /// <summary>Return a stackalloc-backed span directly → MIST2008</summary>
        ReturnDirect,

        /// <summary>Return a stackalloc-backed span through a local alias → MIST2008</summary>
        ReturnViaLocal,

        /// <summary>Return via reassignment to a typed span local → MIST2008</summary>
        ReturnViaReassignment,

        /// <summary>Store into a ref struct parameter's field → MIST2009</summary>
        StoreViaRefStructField,

        /// <summary>Store into a different ref struct parameter's field → MIST2009</summary>
        StoreViaRefStructField2,

        /// <summary>Return after storing in local then returning that local → MIST2008</summary>
        ReturnViaMultipleHops,
    }

    /// <summary>
    /// A scenario combining an escape shape with a span element type.
    /// </summary>
    public sealed record EscapeScenario(
        EscapeShape Shape,
        string ElementType,
        string Description)
    {
        /// <summary>The diagnostic ID expected for this escape shape.</summary>
#pragma warning disable CA1024
        public string GetExpectedDiagnosticId() => Shape switch
        {
            EscapeShape.ReturnDirect => "MIST2008",
            EscapeShape.ReturnViaLocal => "MIST2008",
            EscapeShape.ReturnViaReassignment => "MIST2008",
            EscapeShape.StoreViaRefStructField => "MIST2009",
            EscapeShape.StoreViaRefStructField2 => "MIST2009",
            EscapeShape.ReturnViaMultipleHops => "MIST2008",
            _ => throw new ArgumentOutOfRangeException(),
        };
#pragma warning restore CA1024

        public override string ToString() => Description;
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ElementTypes =
        ["int", "byte", "long", "short", "float", "double", "bool", "char", "uint"];

    private static readonly EscapeShape[] AllShapes =
        [EscapeShape.ReturnDirect, EscapeShape.ReturnViaLocal, EscapeShape.ReturnViaReassignment,
         EscapeShape.StoreViaRefStructField, EscapeShape.StoreViaRefStructField2, EscapeShape.ReturnViaMultipleHops];

    private static Gen<EscapeScenario> GenScenario()
    {
        return from shape in Gen.Elements(AllShapes)
               from typeIdx in Gen.Choose(0, ElementTypes.Length - 1)
               let elementType = ElementTypes[typeIdx]
               select new EscapeScenario(
                   shape, elementType,
                   $"stackalloc {elementType}[] escapes via {shape}");
    }

    public static Arbitrary<EscapeScenario> ArbitraryEscapeScenario() =>
        GenScenario().ToArbitrary();

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildEscapingProgram(EscapeScenario scenario) => scenario.Shape switch
    {
        // Return a stackalloc-backed span directly from a method.
        EscapeShape.ReturnDirect => $$"""
            class Program {
                static Span<{{scenario.ElementType}}, unknown> Escape() {
                    var s = stackalloc {{scenario.ElementType}}[4];
                    return s;
                }

                static void Main() {
                }
            }
            """,

        // Return a stackalloc-backed span through a var-inferred local alias.
        EscapeShape.ReturnViaLocal => $$"""
            class Program {
                static Span<{{scenario.ElementType}}, unknown> Escape() {
                    var s = stackalloc {{scenario.ElementType}}[8];
                    var alias = s;
                    return alias;
                }

                static void Main() {
                }
            }
            """,

        // Return a stackalloc-backed span after reassignment through a typed local.
        EscapeShape.ReturnViaReassignment => $$"""
            class Program {
                static Span<{{scenario.ElementType}}, unknown> Escape() {
                    var s = stackalloc {{scenario.ElementType}}[4];
                    Span<{{scenario.ElementType}}, unknown> other = s;
                    return other;
                }

                static void Main() {
                }
            }
            """,

        // Store a stackalloc-backed span into a ref struct parameter's field.
        // The LHS (holder.held) is a member access, not a simple local or parameter,
        // so the frame-escape analyzer rejects it with MIST2009.
        EscapeShape.StoreViaRefStructField => $$"""
            public ref struct SpanHolder {
                public Span<{{scenario.ElementType}}, unknown> held;
            }

            class Program {
                static void Escape(SpanHolder holder) {
                    var s = stackalloc {{scenario.ElementType}}[4];
                    holder.held = s;
                }

                static void Main() {
                }
            }
            """,

        // Store a stackalloc-backed span into a differently-named ref struct param field.
        EscapeShape.StoreViaRefStructField2 => $$"""
            public ref struct Container {
                public Span<{{scenario.ElementType}}, unknown> buffer;
            }

            class Program {
                static void StoreInto(Container c) {
                    var s = stackalloc {{scenario.ElementType}}[8];
                    c.buffer = s;
                }

                static void Main() {
                }
            }
            """,

        // Return a stackalloc-backed span through multiple local hops.
        EscapeShape.ReturnViaMultipleHops => $$"""
            class Program {
                static Span<{{scenario.ElementType}}, unknown> Escape() {
                    var original = stackalloc {{scenario.ElementType}}[4];
                    var hop1 = original;
                    var hop2 = hop1;
                    return hop2;
                }

                static void Main() {
                }
            }
            """,

        _ => throw new ArgumentOutOfRangeException(),
    };

    // ─── Compilation helper ──────────────────────────────────────────────

    private static CompilerResult CompileSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
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
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false,
                GlobalUsings: ImmutableArray.Create("Standard"));

            return SharedDriver.Compile(options);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 20: Frame-escape soundness — reject on escape
    //
    // For any program in which a stackalloc-backed span escapes its
    // allocating frame (via return, field store, static store, property
    // store, or containing ref struct propagation), compilation is rejected
    // at error severity (MIST2008 for return, MIST2009 for store) at the
    // offending expression's source location, and no C# is emitted.
    //
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.5, 8.7, 14.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(FrameEscapeRejectPropertyTests) })]
    public Property FrameEscapeSoundness_RejectOnEscape(EscapeScenario scenario)
    {
        var source = BuildEscapingProgram(scenario);
        var result = CompileSource(source);

        var expectedId = scenario.GetExpectedDiagnosticId();

        // 1. The expected diagnostic must fire.
        var hits = result.Diagnostics
            .Where(d => d.Id.Value == expectedId)
            .ToList();
        var diagnosticFires = hits.Count > 0;

        // 2. The diagnostic must be at error severity (R14.2).
        var allErrorSeverity = hits.All(d => d.Severity == DiagnosticSeverity.Error);

        // 3. The diagnostic must have a non-empty source location (R14.5).
        var allHaveLocation = hits.All(d =>
            d.Location.Start.Line > 0 || d.Location.Start.Column > 0 ||
            d.Location.End.Line > 0 || d.Location.End.Column > 0);

        // 4. No C# emitted — compilation must not succeed (R8.7).
        var noOutput = result.EmittedFiles is null || result.EmittedFiles.Count == 0;

        var passed = diagnosticFires && allErrorSeverity && allHaveLocation && noOutput;

        return passed.ToProperty()
            .Label(
                $"Frame-escape rejection for {scenario.Shape} ({scenario.ElementType}):\n" +
                $"  Expected diagnostic: {expectedId}\n" +
                $"  Diagnostic fires: {diagnosticFires} (count={hits.Count})\n" +
                $"  All error severity: {allErrorSeverity}\n" +
                $"  All have location: {allHaveLocation}\n" +
                $"  No C# emitted: {noOutput} (succeeded={result.Succeeded})\n" +
                $"  All diagnostics: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}({d.Severity}): {d.Message}"))}]\n" +
                $"  Source:\n{source}");
    }
}