// Feature: stack-only-types, Property 5: Known-to-unknown widening
//
// **Validates: Requirements 4.4, 4.5**
//
// In-process; assert initializing a K<T> (unknown) binding with a matching-kind
// stackalloc T[N] is accepted with no diagnostic, mirroring int[] x = new int[N].

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

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Describes a known-to-unknown widening scenario for property testing.
/// The target type uses the explicit <c>K&lt;T, unknown&gt;</c> form (equivalent
/// to the <c>K&lt;T&gt;</c> alias) to exercise the widening from a known-length
/// <c>stackalloc T[N]</c> (which produces <c>SpanType(T, KnownLength=N)</c>)
/// to the unknown-length target.
/// </summary>
public sealed record KnownToUnknownWideningScenario(
    string Source,
    bool IsReadOnly,
    string ElementType,
    int Size,
    string Description)
{
    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for known-to-unknown widening
/// (Property 5: Known-to-unknown widening).
///
/// Verifies that initializing a <c>Span&lt;T, unknown&gt;</c> (= <c>Span&lt;T&gt;</c>)
/// or <c>ReadOnlySpan&lt;T, unknown&gt;</c> (= <c>ReadOnlySpan&lt;T&gt;</c>) binding with
/// a matching-kind <c>stackalloc T[N]</c> (known-length) is accepted with no
/// diagnostic — a permitted widening that erases the known length, exactly as
/// <c>int[] x = new int[3]</c> erases the known length for arrays.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 4.4, 4.5**
/// </summary>
public class KnownToUnknownWideningPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Element type pool: stampable primitives ─────────────────────────

    private static readonly string[] StampableTypes =
        ["int", "uint", "long", "byte", "short", "float", "double", "bool", "char"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a known-length <c>stackalloc T[N]</c> initializes
    /// a <c>Span&lt;T, unknown&gt;</c> (unknown-length) binding.
    /// The RHS is <c>SpanType(T, KnownLength=N)</c>; the LHS is <c>SpanType(T, unknown)</c>.
    /// This exercises the known→unknown widening rule (R4.4).
    /// Expected: accepted (no diagnostic).
    /// </summary>
    private static Gen<KnownToUnknownWideningScenario> GenMutableWideningScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 512)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"Span<{elementType}, unknown> s = stackalloc {elementType}[{size}];")
               select new KnownToUnknownWideningScenario(
                   source,
                   IsReadOnly: false,
                   ElementType: elementType,
                   Size: size,
                   $"Span<{elementType}, unknown> s = stackalloc {elementType}[{size}] → widening accepted");
    }

    /// <summary>
    /// Generates a scenario where a known-length <c>stackalloc T[N]</c> initializes
    /// a <c>ReadOnlySpan&lt;T, unknown&gt;</c> (unknown-length) binding.
    /// The RHS is <c>ReadOnlySpanType(T, KnownLength=N)</c>; the LHS is
    /// <c>ReadOnlySpanType(T, unknown)</c>.
    /// This exercises the known→unknown widening rule for read-only spans (R4.5).
    /// Expected: accepted (no diagnostic).
    /// </summary>
    private static Gen<KnownToUnknownWideningScenario> GenReadOnlyWideningScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 512)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[{size}];")
               select new KnownToUnknownWideningScenario(
                   source,
                   IsReadOnly: true,
                   ElementType: elementType,
                   Size: size,
                   $"ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[{size}] → widening accepted");
    }

    /// <summary>
    /// Combined generator: mutable and read-only widening scenarios with equal probability.
    /// </summary>
    public static Arbitrary<KnownToUnknownWideningScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenMutableWideningScenario(),
            GenReadOnlyWideningScenario()).ToArbitrary();
    }

    // ─── Program wrapping ───────────────────────────────────────────────

    private static string WrapInProgram(string body)
    {
        return $$"""
            class Program {
                static void Main() {
                    {{body}}
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static CompilerResult CompileSource(string source)
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

            return SharedDriver.Compile(options);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: Known-to-unknown widening
    //
    // **Validates: Requirements 4.4, 4.5**
    //
    // Initializing a K<T, unknown> (unknown-length) binding with a matching-kind
    // stackalloc T[N] (known-length) is accepted with no diagnostic —
    // a permitted widening that erases the known length, exactly as
    // int[] x = new int[N] erases the known length for arrays.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 5: Initializing an unknown-length span binding with a known-length
    /// stackalloc expression is accepted without any diagnostic — the type checker
    /// permits the widening from <c>SpanType(T, KnownLength=N)</c> to
    /// <c>SpanType(T, unknown)</c> (and the corresponding ReadOnlySpan variant).
    ///
    /// **Validates: Requirements 4.4, 4.5**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(KnownToUnknownWideningPropertyTests) })]
    public Property KnownLengthStackalloc_WidensToUnknownLengthBinding_NoDiagnostic(
        KnownToUnknownWideningScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var passed = errors.Count == 0;

        return passed.ToProperty()
            .Label(
                $"Expected no errors (widening should be accepted), but got {errors.Count} error(s).\n" +
                $"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Scenario: {scenario}\n" +
                $"Source:\n{scenario.Source}");
    }

    /// <summary>
    /// Property 5 (parity): Known-to-unknown widening for spans mirrors the same
    /// behavior for arrays — <c>int[] x = new int[N]</c> is also accepted without
    /// any diagnostic. Uses the same random element type and size to confirm parity.
    ///
    /// **Validates: Requirement 4.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(KnownToUnknownWideningPropertyTests) })]
    public Property ArrayKnownLengthWidening_AlsoAccepted_Parity(
        KnownToUnknownWideningScenario scenario)
    {
        // Construct the analogous array widening scenario using the same element type
        // and size: T[] x = new T[N]; — known-length array assigned to unknown-length
        // array binding. This verifies the parity claim in R4.4.
        var source = WrapInProgram(
            $"{scenario.ElementType}[] x = new {scenario.ElementType}[{scenario.Size}];");

        var result = CompileSource(source);

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        var passed = errors.Count == 0;

        return passed.ToProperty()
            .Label(
                $"Array parity: {scenario.ElementType}[] x = new {scenario.ElementType}[{scenario.Size}] " +
                $"should be accepted (widening), but got {errors.Count} error(s).\n" +
                $"Errors: [{string.Join("; ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
