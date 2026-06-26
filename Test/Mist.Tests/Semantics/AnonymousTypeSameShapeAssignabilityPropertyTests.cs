// Feature: anonymous-types, Property 6: Same-shape anonymous types are assignable
// **Validates: Requirements 4.6**
//
// Property 6 (design.md):
//   For any anonymous type shape (names, types, order), two variables whose inferred
//   types are structurally equal AnonymousType instances SHALL be assignable to each other.
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
/// Property-based test verifying that two variables whose inferred types are
/// structurally equal <c>AnonymousType</c> instances are assignable to each other
/// in both directions without producing any diagnostics.
///
/// **Validates: Requirements 4.6**
/// </summary>
public sealed class AnonymousTypeSameShapeAssignabilityPropertyTests
{
    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_anon_assign_pbt_" + Guid.NewGuid().ToString("N"));
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

            return SharedDriver.Compile(options).Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types — FsCheck-friendly scenario types

    /// <summary>
    /// A property definition with a name and two distinct value expressions of the same type.
    /// </summary>
    public sealed record PropertyDef(string Name, string FirstValue, string SecondValue);

    /// <summary>
    /// Scenario: two same-shape anonymous types with assignment in both directions.
    /// </summary>
    public sealed record SameShapeScenario(
        ImmutableArray<PropertyDef> Properties,
        string Source)
    {
        public override string ToString() =>
            $"Properties: [{string.Join(", ", Properties.Select(p => p.Name))}]";
    }

#pragma warning restore CA1034

    // ─── Property name and value pools ───────────────────────────────────

    /// <summary>
    /// Valid C# identifiers suitable for property names (PascalCase).
    /// </summary>
    private static readonly string[] NamePool =
    [
        "Alpha", "Beta", "Gamma", "Delta", "Epsilon",
        "Zeta", "Eta", "Theta", "Iota", "Kappa",
        "Name", "Value", "Count", "Index", "Label",
        "Score", "Width", "Height", "Color", "Status",
    ];

    /// <summary>
    /// Typed value pairs: two different values of the same type.
    /// Each entry is (firstValue, secondValue) so we can produce two distinct
    /// anonymous type instances with the same shape.
    /// </summary>
    private static readonly (string First, string Second)[] TypedValuePairs =
    [
        ("1", "99"),
        ("\"hello\"", "\"world\""),
        ("3.14", "2.71"),
        ("true", "false"),
        ("'a'", "'z'"),
    ];

    // ─── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// Generates 1-5 distinct property definitions with same-typed value pairs.
    /// </summary>
    private static Gen<ImmutableArray<PropertyDef>> GenPropertyDefs()
    {
        return from count in Gen.Choose(1, 5)
               from nameIndices in Gen.ListOf(Gen.Choose(0, NamePool.Length - 1))
               from valueIndices in Gen.ListOf(Gen.Choose(0, TypedValuePairs.Length - 1))
               let distinctNames = nameIndices.Select(i => NamePool[i]).Distinct().Take(count).ToArray()
               where distinctNames.Length >= 1
               select distinctNames
                   .Select((name, i) =>
                   {
                       var vi = valueIndices.Count > 0
                           ? valueIndices[i % valueIndices.Count]
                           : 0;
                       var pair = TypedValuePairs[vi];
                       return new PropertyDef(name, pair.First, pair.Second);
                   })
                   .ToImmutableArray();
    }

    /// <summary>
    /// Builds a Mist source with two same-shape anonymous types and assigns
    /// one to the other in both directions.
    /// </summary>
    private static string BuildSameShapeSource(ImmutableArray<PropertyDef> props)
    {
        var firstPropList = string.Join(", ", props.Select(p => $"{p.Name} = {p.FirstValue}"));
        var secondPropList = string.Join(", ", props.Select(p => $"{p.Name} = {p.SecondValue}"));

        return $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var first = new { {{firstPropList}} };
                    var second = new { {{secondPropList}} };
                    second = first;
                    first = second;
                }
            }
            """;
    }

    private static Gen<SameShapeScenario> GenScenario()
    {
        return from props in GenPropertyDefs()
               let source = BuildSameShapeSource(props)
               select new SameShapeScenario(props, source);
    }

    public static Arbitrary<SameShapeScenario> ArbitrarySameShapeScenario() =>
        GenScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Same-shape anonymous types are assignable
    //
    //   For any anonymous type shape (names, types, order), two variables whose
    //   inferred types are structurally equal AnonymousType instances SHALL be
    //   assignable to each other without producing any diagnostics.
    //
    // **Validates: Requirements 4.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypeSameShapeAssignabilityPropertyTests) })]
    public Property SameShapeAnonymousTypes_AreAssignableInBothDirections(SameShapeScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // Filter to only error-level diagnostics (ignore warnings if any).
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            var messages = string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty().Label(
                $"Expected zero errors for same-shape assignment but got {errors.Count}:\n"
                + $"{messages}\nScenario: {scenario}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}