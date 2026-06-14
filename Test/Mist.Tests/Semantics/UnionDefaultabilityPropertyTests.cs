// Feature: optional-defaultability, Property 9: Union defaultability based on void membership
// **Validates: Requirements 7.1, 7.2, 7.3, 7.4**

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
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 9: a union type declaration and a default expression usage.
/// The scenario tracks whether the union contains void (and thus should be defaultable).
/// </summary>
public sealed class UnionDefaultabilityScenario
{
    /// <summary>The Mist source code exercising the scenario.</summary>
    public string Source { get; }

    /// <summary>Whether the union type contains void as a constituent.</summary>
    public bool ContainsVoid { get; }

    /// <summary>A human-readable label for the scenario.</summary>
    public string Label { get; }

    public UnionDefaultabilityScenario(string source, bool containsVoid, string label)
    {
        Source = source;
        ContainsVoid = containsVoid;
        Label = label;
    }

    public override string ToString() => Label;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for union type defaultability based on void membership.
///
/// Property 9: For any union type, it is defaultable if and only if void is one
/// of its constituents. default(T | void) SHALL be valid; default(A | B) where
/// neither is void SHALL produce MIST_DEFAULT_NOT_SUPPORTED.
///
/// **Validates: Requirements 7.1, 7.2, 7.3, 7.4**
/// </summary>
public sealed class UnionDefaultabilityPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Primitive types that can be used as union constituents.
    /// </summary>
    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double"];

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios where a union type contains void and default is used on it.
    /// These should compile without MIST_DEFAULT_NOT_SUPPORTED.
    /// </summary>
    private static Gen<UnionDefaultabilityScenario> GenUnionWithVoid()
    {
        // Pattern 1: T? sugar (desugars to T | void) — use default(T?)
        var questionMarkGen = Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $$"""
                class Program
                {
                    public static void Main()
                    {
                        {{t}}? x = default;
                    }
                }
                """;
            return new UnionDefaultabilityScenario(source, containsVoid: true, $"{t}? = default (sugar)");
        });

        // Pattern 2: Explicit T | void as variable type with bare default
        var explicitUnionVoidGen = Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $$"""
                class Program
                {
                    public static void Main()
                    {
                        {{t}} | void x = default;
                    }
                }
                """;
            return new UnionDefaultabilityScenario(source, containsVoid: true, $"{t} | void = default (explicit)");
        });

        // Pattern 3: Multi-constituent union with void — T | U | void via type alias
        var multiUnionVoidGen =
            from t1 in Gen.Elements(PrimitiveTypes)
            from t2 in Gen.Elements(PrimitiveTypes)
            where t1 != t2
            select new UnionDefaultabilityScenario(
                $$"""
                type MyUnion = {{t1}} | {{t2}} | void;

                class Program
                {
                    public static void Main()
                    {
                        MyUnion x = default;
                    }
                }
                """,
                containsVoid: true,
                $"type MyUnion = {t1} | {t2} | void; default");

        return Gen.OneOf(questionMarkGen, explicitUnionVoidGen, multiUnionVoidGen);
    }

    /// <summary>
    /// Generates scenarios where a union type does NOT contain void and default is used on it.
    /// These should produce MIST_DEFAULT_NOT_SUPPORTED.
    /// </summary>
    private static Gen<UnionDefaultabilityScenario> GenUnionWithoutVoid()
    {
        // Pattern 1: type alias for T | U (no void) with bare default
        var twoTypeUnionGen =
            from t1 in Gen.Elements(PrimitiveTypes)
            from t2 in Gen.Elements(PrimitiveTypes)
            where t1 != t2
            select new UnionDefaultabilityScenario(
                $$"""
                type NoVoidUnion = {{t1}} | {{t2}};

                class Program
                {
                    public static void Main()
                    {
                        NoVoidUnion x = default;
                    }
                }
                """,
                containsVoid: false,
                $"type NoVoidUnion = {t1} | {t2}; default (no void)");

        // Pattern 2: Three-constituent union without void
        var threeTypeUnionGen =
            from t1 in Gen.Elements(PrimitiveTypes)
            from t2 in Gen.Elements(PrimitiveTypes)
            from t3 in Gen.Elements(PrimitiveTypes)
            where t1 != t2 && t2 != t3 && t1 != t3
            select new UnionDefaultabilityScenario(
                $$"""
                type TripleUnion = {{t1}} | {{t2}} | {{t3}};

                class Program
                {
                    public static void Main()
                    {
                        TripleUnion x = default;
                    }
                }
                """,
                containsVoid: false,
                $"type TripleUnion = {t1} | {t2} | {t3}; default (no void)");

        return Gen.OneOf(twoTypeUnionGen, threeTypeUnionGen);
    }

    public static Arbitrary<UnionDefaultabilityScenario> ArbitraryUnionDefaultabilityScenario()
    {
        var gen = Gen.Frequency(
            (1, GenUnionWithVoid()),
            (1, GenUnionWithoutVoid()));

        return gen.ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_union_default_pbt_" + Guid.NewGuid().ToString("N"));
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
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Union defaultability based on void membership
    //
    // **Validates: Requirements 7.1, 7.2, 7.3, 7.4**
    //
    // For any union type, it is defaultable if and only if void is one of
    // its constituents. default(T | void) SHALL be valid; default(A | B)
    // where neither is void SHALL produce MIST_DEFAULT_NOT_SUPPORTED.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 9a: Union types containing void are defaultable — using default on
    /// them produces no MIST_DEFAULT_NOT_SUPPORTED diagnostic.
    ///
    /// Tests T? sugar, explicit T | void, and multi-constituent unions with void.
    ///
    /// **Validates: Requirements 7.1, 7.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionDefaultabilityPropertyTests) })]
    public Property UnionWithVoid_IsDefaultable_NoError(UnionDefaultabilityScenario scenario)
    {
        if (!scenario.ContainsVoid)
            return true.ToProperty().Label("Skipped — not a void-containing scenario");

        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasDefaultNotSupported = diagnostics.Any(d =>
            d.Id.Value == "MIST_DEFAULT_NOT_SUPPORTED");

        return (!hasDefaultNotSupported).ToProperty()
            .Label($"Union with void should be defaultable but got MIST_DEFAULT_NOT_SUPPORTED for '{scenario.Label}'")
            .Label($"Diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// Property 9b: Union types without void are NOT defaultable — using default on
    /// them SHALL produce MIST_DEFAULT_NOT_SUPPORTED.
    ///
    /// Tests two-constituent and three-constituent unions without void.
    ///
    /// **Validates: Requirements 7.2, 7.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnionDefaultabilityPropertyTests) })]
    public Property UnionWithoutVoid_IsNotDefaultable_ProducesError(UnionDefaultabilityScenario scenario)
    {
        if (scenario.ContainsVoid)
            return true.ToProperty().Label("Skipped — not a no-void scenario");

        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasDefaultNotSupported = diagnostics.Any(d =>
            d.Id.Value == "MIST_DEFAULT_NOT_SUPPORTED");

        return hasDefaultNotSupported.ToProperty()
            .Label($"Union without void should NOT be defaultable but no MIST_DEFAULT_NOT_SUPPORTED for '{scenario.Label}'")
            .Label($"Diagnostics: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
