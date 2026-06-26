// Feature: anonymous-types, Property 11: Equality operators require same-shape anonymous types
// **Validates: Requirements 9.1, 9.3**
//
// Property 11 (design.md):
//   For any two values where at least one has an AnonymousType, the == and !=
//   operators SHALL be accepted if and only if both values have structurally equal
//   AnonymousType instances.
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
/// Property-based tests verifying that:
/// - Same-shape anonymous types can be compared with == and != without error.
/// - Different-shape anonymous types compared with == or != produce
///   MIST_ANON_EQUALITY_MISMATCH.
///
/// **Validates: Requirements 9.1, 9.3**
/// </summary>
public sealed class AnonymousTypeEqualityOperatorPropertyTests
{
    private const string EqualityMismatchId = "MIST_ANON_EQUALITY_MISMATCH";

    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_anon_eq_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario models ─────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types — FsCheck-friendly scenario types

    /// <summary>
    /// A property definition with a name and two distinct value expressions of the same type.
    /// </summary>
    public sealed record PropertyDef(string Name, string FirstValue, string SecondValue);

    /// <summary>
    /// Scenario: two same-shape anonymous types compared with == or !=.
    /// Should produce ZERO equality mismatch diagnostics.
    /// </summary>
    public sealed record SameShapeEqualityScenario(
        ImmutableArray<PropertyDef> Properties,
        string Operator,
        string Source)
    {
        public override string ToString() =>
            $"Properties: [{string.Join(", ", Properties.Select(p => p.Name))}], Op: {Operator}";
    }

    /// <summary>
    /// Scenario: two different-shape anonymous types compared with == or !=.
    /// Should produce MIST_ANON_EQUALITY_MISMATCH.
    /// </summary>
    public sealed record DifferentShapeEqualityScenario(
        ImmutableArray<PropertyDef> LeftProperties,
        ImmutableArray<PropertyDef> RightProperties,
        string Operator,
        string Source)
    {
        public override string ToString() =>
            $"Left: [{string.Join(", ", LeftProperties.Select(p => p.Name))}], " +
            $"Right: [{string.Join(", ", RightProperties.Select(p => p.Name))}], Op: {Operator}";
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
    /// </summary>
    private static readonly (string First, string Second)[] TypedValuePairs =
    [
        ("1", "99"),
        ("\"hello\"", "\"world\""),
        ("3.14", "2.71"),
        ("true", "false"),
        ("'a'", "'z'"),
    ];

    /// <summary>
    /// The equality operators to test.
    /// </summary>
    private static readonly string[] EqualityOperators = ["==", "!="];

    // ─── Generators ──────────────────────────────────────────────────────

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
    /// Generates two property def arrays that are guaranteed to have different shapes.
    /// Different shape means: different property names OR different property count.
    /// </summary>
    private static Gen<(ImmutableArray<PropertyDef> Left, ImmutableArray<PropertyDef> Right)> GenDifferentShapePair()
    {
        return from leftProps in GenPropertyDefs()
               from rightProps in GenPropertyDefs()
               let leftNames = leftProps.Select(p => p.Name).ToArray()
               let rightNames = rightProps.Select(p => p.Name).ToArray()
               where !leftNames.SequenceEqual(rightNames)
               select (leftProps, rightProps);
    }

    private static Gen<string> GenOperator() => Gen.Elements(EqualityOperators);

    /// <summary>
    /// Builds a Mist source with two same-shape anonymous types compared with
    /// the given operator.
    /// </summary>
    private static string BuildSameShapeSource(ImmutableArray<PropertyDef> props, string op)
    {
        var firstPropList = string.Join(", ", props.Select(p => $"{p.Name} = {p.FirstValue}"));
        var secondPropList = string.Join(", ", props.Select(p => $"{p.Name} = {p.SecondValue}"));

        return $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var a = new { {{firstPropList}} };
                    var b = new { {{secondPropList}} };
                    var result = a {{op}} b;
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist source with two different-shape anonymous types compared with
    /// the given operator.
    /// </summary>
    private static string BuildDifferentShapeSource(
        ImmutableArray<PropertyDef> leftProps,
        ImmutableArray<PropertyDef> rightProps,
        string op)
    {
        var leftPropList = string.Join(", ", leftProps.Select(p => $"{p.Name} = {p.FirstValue}"));
        var rightPropList = string.Join(", ", rightProps.Select(p => $"{p.Name} = {p.FirstValue}"));

        return $$"""
            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var a = new { {{leftPropList}} };
                    var b = new { {{rightPropList}} };
                    var result = a {{op}} b;
                }
            }
            """;
    }

    private static Gen<SameShapeEqualityScenario> GenSameShapeScenario()
    {
        return from props in GenPropertyDefs()
               from op in GenOperator()
               let source = BuildSameShapeSource(props, op)
               select new SameShapeEqualityScenario(props, op, source);
    }

    private static Gen<DifferentShapeEqualityScenario> GenDifferentShapeScenario()
    {
        return from pair in GenDifferentShapePair()
               from op in GenOperator()
               let source = BuildDifferentShapeSource(pair.Left, pair.Right, op)
               select new DifferentShapeEqualityScenario(pair.Left, pair.Right, op, source);
    }

    public static Arbitrary<SameShapeEqualityScenario> ArbitrarySameShapeEqualityScenario() =>
        GenSameShapeScenario().ToArbitrary();

    public static Arbitrary<DifferentShapeEqualityScenario> ArbitraryDifferentShapeEqualityScenario() =>
        GenDifferentShapeScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11 (same-shape): == and != accepted for same-shape anonymous types
    //
    //   For any two values of the same AnonymousType (structurally equal), the ==
    //   and != operators SHALL be accepted without producing
    //   MIST_ANON_EQUALITY_MISMATCH.
    //
    // **Validates: Requirements 9.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypeEqualityOperatorPropertyTests) })]
    public Property SameShapeAnonymousTypes_EqualityOperatorAccepted(SameShapeEqualityScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == EqualityMismatchId)
            .ToList();

        if(mismatchDiags.Count > 0)
        {
            var messages = string.Join("\n", mismatchDiags.Select(d => $"  {d.Message}"));
            return false.ToProperty().Label(
                $"Expected no {EqualityMismatchId} for same-shape equality but got:\n"
                + $"{messages}\nScenario: {scenario}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11 (different-shape): == and != rejected for different-shape
    //
    //   For any two values with different AnonymousType shapes, the == and !=
    //   operators SHALL be rejected with MIST_ANON_EQUALITY_MISMATCH.
    //
    // **Validates: Requirements 9.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypeEqualityOperatorPropertyTests) })]
    public Property DifferentShapeAnonymousTypes_EqualityOperatorRejected(DifferentShapeEqualityScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var mismatchDiags = diagnostics
            .Where(d => d.Id.Value == EqualityMismatchId)
            .ToList();

        if(mismatchDiags.Count == 0)
        {
            var allDiags = string.Join("\n", diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty().Label(
                $"Expected {EqualityMismatchId} for different-shape equality but got none.\n"
                + $"Scenario: {scenario}\nAll diagnostics: [{allDiags}]\nSource:\n{scenario.Source}");
        }

        // Verify the message mentions "different shapes"
        var mentionsShape = mismatchDiags.Any(d =>
            d.Message.Contains("different shapes", StringComparison.OrdinalIgnoreCase));

        if(!mentionsShape)
        {
            var messages = string.Join("\n", mismatchDiags.Select(d => d.Message));
            return false.ToProperty().Label(
                $"{EqualityMismatchId} found but message does not mention 'different shapes'.\n"
                + $"Scenario: {scenario}\nMessages:\n{messages}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}