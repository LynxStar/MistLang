// Feature: anonymous-types, Property 12: Duplicate property names are rejected
// **Validates: Requirements 10.1**
//
// Property 12 (design.md):
//   For any anonymous type expression where the same property name appears more
//   than once (whether through explicit assignment or projection), the type checker
//   SHALL report a duplicate property diagnostic (MIST_ANON_DUPLICATE_PROP).
//
// Uses in-process CompilerDriver.Compile() with EmitToDisk:false — ZERO external
// process spawns per FsCheck iteration.

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

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based test verifying that anonymous type expressions with duplicate
/// property names produce the <c>MIST_ANON_DUPLICATE_PROP</c> diagnostic.
///
/// **Validates: Requirements 10.1**
///
/// Variations tested:
/// - Two explicit properties with the same name: <c>new { Name = 1, Name = 2 }</c>
/// - Explicit + projection with the same name: <c>new { name = 1, name }</c>
/// - Two projections of the same variable: <c>new { x, x }</c>
/// </summary>
public sealed class AnonymousTypeDuplicatePropertyPropertyTests
{
    private const string DuplicatePropId = "MIST_ANON_DUPLICATE_PROP";

    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_anon_dup_pbt_" + Guid.NewGuid().ToString("N"));
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
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types — FsCheck-friendly scenario + enum

    /// <summary>
    /// The kind of duplicate that is introduced into the anonymous type expression.
    /// </summary>
    public enum DuplicateKind
    {
        /// <summary>Two explicit assignments with the same property name.</summary>
        TwoExplicit,

        /// <summary>One explicit assignment and one projection with the same name.</summary>
        ExplicitAndProjection,

        /// <summary>Two projections of the same variable.</summary>
        TwoProjections,
    }

    /// <summary>
    /// A generated scenario: a Mist program containing an anonymous type expression
    /// with a duplicate property name, plus the name that should appear in the diagnostic.
    /// </summary>
    public sealed record DuplicateScenario(string Source, string DuplicatedName, DuplicateKind Kind)
    {
        public override string ToString() => $"{Kind}: duplicate '{DuplicatedName}'";
    }

#pragma warning restore CA1034

    // ─── Property name pool ──────────────────────────────────────────────

    /// <summary>
    /// Valid C# identifiers suitable for property names. Lower-case first char
    /// allows them to also serve as local variable names for projection scenarios.
    /// </summary>
    private static readonly string[] NamePool =
    [
        "alpha", "beta", "gamma", "delta", "epsilon",
        "zeta", "eta", "theta", "iota", "kappa",
        "name", "value", "count", "index", "label",
        "score", "width", "height", "color", "status",
    ];

    // ─── Scenario builders ───────────────────────────────────────────────

    /// <summary>
    /// Builds a scenario with two explicit properties sharing the same name.
    /// Generates: <c>var x = new { Name = 1, Name = 2 };</c>
    /// </summary>
    private static DuplicateScenario BuildTwoExplicit(string dupName, string otherName)
    {
        var source = $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var x = new { {{dupName}} = 1, {{otherName}} = "hello", {{dupName}} = 2 };
                }
            }
            """;

        return new DuplicateScenario(source, dupName, DuplicateKind.TwoExplicit);
    }

    /// <summary>
    /// Builds a scenario with one explicit property and one projection sharing the same name.
    /// Generates: <c>var name = 10; var x = new { name = 1, name };</c>
    /// </summary>
    private static DuplicateScenario BuildExplicitAndProjection(string dupName, string otherName)
    {
        var source = $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var {{dupName}} = 10;
                    var x = new { {{dupName}} = 1, {{otherName}} = "hello", {{dupName}} };
                }
            }
            """;

        return new DuplicateScenario(source, dupName, DuplicateKind.ExplicitAndProjection);
    }

    /// <summary>
    /// Builds a scenario with two projections of the same variable.
    /// Generates: <c>var name = 10; var x = new { name, name };</c>
    /// </summary>
    private static DuplicateScenario BuildTwoProjections(string dupName, string otherName)
    {
        var source = $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var {{dupName}} = 10;
                    var {{otherName}} = 20;
                    var x = new { {{dupName}}, {{otherName}}, {{dupName}} };
                }
            }
            """;

        return new DuplicateScenario(source, dupName, DuplicateKind.TwoProjections);
    }

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<DuplicateScenario> GenScenario()
    {
        // Pick two distinct names from the pool and a duplicate kind.
        var genTwoDistinctNames =
            from i in Gen.Choose(0, NamePool.Length - 1)
            from j in Gen.Choose(0, NamePool.Length - 1).Where(x => x != i)
            select (dup: NamePool[i], other: NamePool[j]);

        var genKind = Gen.Elements(
            DuplicateKind.TwoExplicit,
            DuplicateKind.ExplicitAndProjection,
            DuplicateKind.TwoProjections);

        return from names in genTwoDistinctNames
               from kind in genKind
               select kind switch
               {
                   DuplicateKind.TwoExplicit => BuildTwoExplicit(names.dup, names.other),
                   DuplicateKind.ExplicitAndProjection => BuildExplicitAndProjection(names.dup, names.other),
                   DuplicateKind.TwoProjections => BuildTwoProjections(names.dup, names.other),
                   _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
               };
    }

    public static Arbitrary<DuplicateScenario> ArbitraryDuplicateScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 12: Duplicate property names are rejected.
    //
    //   For any anonymous type expression where the same property name appears
    //   more than once, the type checker SHALL report MIST_ANON_DUPLICATE_PROP
    //   with the correct property name in the message.
    //
    // **Validates: Requirements 10.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypeDuplicatePropertyPropertyTests) })]
    public Property DuplicatePropertyName_ProducesDiagnostic(DuplicateScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var duplicateDiags = diagnostics
            .Where(d => d.Id.Value == DuplicatePropId)
            .ToList();

        // Must have at least one MIST_ANON_DUPLICATE_PROP diagnostic.
        if(duplicateDiags.Count == 0)
        {
            var allIds = string.Join(", ", diagnostics.Select(d => d.Id.Value));
            return false.ToProperty().Label(
                $"Expected MIST_ANON_DUPLICATE_PROP but got none.\n"
                + $"Scenario: {scenario}\nAll diagnostics: [{allIds}]\nSource:\n{scenario.Source}");
        }

        // The diagnostic message must mention the duplicated property name.
        var mentionsDupName = duplicateDiags.Any(d =>
            d.Message.Contains(scenario.DuplicatedName, StringComparison.Ordinal));

        if(!mentionsDupName)
        {
            var messages = string.Join("\n", duplicateDiags.Select(d => d.Message));
            return false.ToProperty().Label(
                $"MIST_ANON_DUPLICATE_PROP found but message does not mention '{scenario.DuplicatedName}'.\n"
                + $"Scenario: {scenario}\nMessages:\n{messages}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}