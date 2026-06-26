// Feature: anonymous-types, Property 10: Property patterns validate against AnonymousType
// Feature: anonymous-types, Property 13: Property patterns on unknown accept any property names
// **Validates: Requirements 5.1, 5.3, 8.3**
//
// Property 10 (design.md):
//   For any AnonymousType and a property pattern, if the pattern references a
//   property name that exists in the type, the sub-pattern SHALL be validated
//   against that property's type; if the name does not exist, the type checker
//   SHALL report an error.
//
// Property 13 (design.md):
//   For any set of property names in a property pattern and a scrutinee of type
//   `unknown`, the type checker SHALL accept the pattern without reporting
//   missing-property errors, deferring validation to runtime.
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
/// Property-based tests verifying:
/// - Property 10: Property patterns on anonymous types validate property names
///   against the AnonymousType's property list — existing names are accepted,
///   non-existent names produce MIST_TC_0053.
/// - Property 13: Property patterns on an `unknown`-typed scrutinee accept any
///   property names without reporting missing-property errors.
///
/// **Validates: Requirements 5.1, 5.3, 8.3**
/// </summary>
public sealed class AnonymousTypePatternMatchingPropertyTests
{
    private const string PatternUndefinedPropertyId = "MIST_TC_0053";

    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_anon_pattern_pbt_" + Guid.NewGuid().ToString("N"));
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
    /// A property definition with a name and a value expression.
    /// </summary>
    public sealed record PropertyDef(string Name, string ValueExpr);

    /// <summary>
    /// Scenario for Property 10 (valid pattern): pattern references an existing
    /// property name on the AnonymousType. No MIST_TC_0053 expected.
    /// </summary>
    public sealed record ValidPatternScenario(
        ImmutableArray<PropertyDef> Properties,
        string PatternedName,
        string Source)
    {
        public override string ToString() =>
            $"Properties: [{string.Join(", ", Properties.Select(p => p.Name))}], Pattern: '{PatternedName}'";
    }

    /// <summary>
    /// Scenario for Property 10 (invalid pattern): pattern references a
    /// non-existent property name on the AnonymousType. MIST_TC_0053 expected.
    /// </summary>
    public sealed record InvalidPatternScenario(
        ImmutableArray<PropertyDef> Properties,
        string NonExistentName,
        string Source)
    {
        public override string ToString() =>
            $"Properties: [{string.Join(", ", Properties.Select(p => p.Name))}], Missing: '{NonExistentName}'";
    }

    /// <summary>
    /// Scenario for Property 13 (unknown scrutinee): pattern references arbitrary
    /// property names on an `unknown`-typed scrutinee. No MIST_TC_0053 expected.
    /// </summary>
    public sealed record UnknownPatternScenario(
        ImmutableArray<PropertyDef> AnonProperties,
        ImmutableArray<string> PatternNames,
        string Source)
    {
        public override string ToString() =>
            $"AnonProps: [{string.Join(", ", AnonProperties.Select(p => p.Name))}], " +
            $"PatternNames: [{string.Join(", ", PatternNames)}]";
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
    /// Names guaranteed to NOT be in the NamePool (for invalid pattern tests).
    /// </summary>
    private static readonly string[] NonExistentNames =
    [
        "Foo", "Bar", "Baz", "Qux", "Quux",
        "Missing", "Unknown", "Absent", "Void", "None",
    ];

    /// <summary>
    /// Typed value expressions for anonymous type properties.
    /// </summary>
    private static readonly string[] ValueExpressions =
    [
        "42",
        "\"hello\"",
        "3.14",
        "true",
        "'x'",
    ];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates 1-5 distinct property definitions (name + value).
    /// </summary>
    private static Gen<ImmutableArray<PropertyDef>> GenPropertyDefs()
    {
        return from count in Gen.Choose(1, 5)
               from nameIndices in Gen.ListOf(Gen.Choose(0, NamePool.Length - 1))
               from valueIndices in Gen.ListOf(Gen.Choose(0, ValueExpressions.Length - 1))
               let distinctNames = nameIndices.Select(i => NamePool[i]).Distinct().Take(count).ToArray()
               where distinctNames.Length >= 1
               select distinctNames
                   .Select((name, i) =>
                   {
                       var vi = valueIndices.Count > 0
                           ? valueIndices[i % valueIndices.Count]
                           : 0;
                       return new PropertyDef(name, ValueExpressions[vi]);
                   })
                   .ToImmutableArray();
    }

    /// <summary>
    /// Builds a Mist source with an anonymous type and a property pattern matching
    /// an existing property name using `var` binding.
    /// </summary>
    private static string BuildValidPatternSource(ImmutableArray<PropertyDef> props, string patternedName)
    {
        var propList = string.Join(", ", props.Select(p => $"{p.Name} = {p.ValueExpr}"));

        return $$"""
            using Standard;

            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var anon = new { {{propList}} };
                    if (anon is { {{patternedName}}: var bound })
                    {
                        Console.WriteLine(bound);
                    }
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist source with an anonymous type and a property pattern matching
    /// a NON-existent property name.
    /// </summary>
    private static string BuildInvalidPatternSource(ImmutableArray<PropertyDef> props, string nonExistentName)
    {
        var propList = string.Join(", ", props.Select(p => $"{p.Name} = {p.ValueExpr}"));

        return $$"""
            using Standard;

            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    var anon = new { {{propList}} };
                    if (anon is { {{nonExistentName}}: var bound })
                    {
                        Console.WriteLine(bound);
                    }
                }
            }
            """;
    }

    /// <summary>
    /// Builds a Mist source with an anonymous type stored in `unknown`, then
    /// pattern-matched with arbitrary property names. Since the scrutinee is
    /// `unknown`, any property names should be accepted.
    /// </summary>
    private static string BuildUnknownPatternSource(
        ImmutableArray<PropertyDef> anonProps,
        ImmutableArray<string> patternNames)
    {
        var propList = string.Join(", ", anonProps.Select(p => $"{p.Name} = {p.ValueExpr}"));
        var patternBindings = string.Join(", ", patternNames.Select((n, i) => $"{n}: var v{i}"));

        return $$"""
            using Standard;

            namespace TestProgram;

            class Program
            {
                public static void Main()
                {
                    unknown box = new { {{propList}} };
                    if (box is { {{patternBindings}} })
                    {
                        Console.WriteLine("matched");
                    }
                }
            }
            """;
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    private static Gen<ValidPatternScenario> GenValidPattern()
    {
        return from props in GenPropertyDefs()
               from accessIndex in Gen.Choose(0, props.Length - 1)
               let patternedName = props[accessIndex].Name
               let source = BuildValidPatternSource(props, patternedName)
               select new ValidPatternScenario(props, patternedName, source);
    }

    private static Gen<InvalidPatternScenario> GenInvalidPattern()
    {
        return from props in GenPropertyDefs()
               from nonExistentIdx in Gen.Choose(0, NonExistentNames.Length - 1)
               let nonExistentName = NonExistentNames[nonExistentIdx]
               where !props.Any(p => p.Name == nonExistentName)
               let source = BuildInvalidPatternSource(props, nonExistentName)
               select new InvalidPatternScenario(props, nonExistentName, source);
    }

    private static Gen<UnknownPatternScenario> GenUnknownPattern()
    {
        // Generate an anonymous type with known properties, then use a mix of
        // names (some from the type, some NOT from the type) in the property pattern.
        // All should be accepted because the scrutinee is `unknown`.
        return from props in GenPropertyDefs()
               from patternCount in Gen.Choose(1, 3)
               from existingIndices in Gen.ListOf(Gen.Choose(0, props.Length - 1))
               from nonExistentIndices in Gen.ListOf(Gen.Choose(0, NonExistentNames.Length - 1))
               let existingNames = existingIndices.Take(patternCount).Select(i => props[i % props.Length].Name)
               let nonExistentPortion = nonExistentIndices.Take(patternCount).Select(i => NonExistentNames[i])
               // Mix: take some from existing, some non-existent
               let allCandidates = existingNames.Concat(nonExistentPortion).Distinct().Take(patternCount).ToArray()
               where allCandidates.Length >= 1
               let patternNames = allCandidates.ToImmutableArray()
               let source = BuildUnknownPatternSource(props, patternNames)
               select new UnknownPatternScenario(props, patternNames, source);
    }

    public static Arbitrary<ValidPatternScenario> ArbitraryValidPattern() =>
        GenValidPattern().ToArbitrary();

    public static Arbitrary<InvalidPatternScenario> ArbitraryInvalidPattern() =>
        GenInvalidPattern().ToArbitrary();

    public static Arbitrary<UnknownPatternScenario> ArbitraryUnknownPattern() =>
        GenUnknownPattern().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10 (valid): Property patterns referencing existing names are accepted
    //
    //   For any AnonymousType and a property pattern referencing a property name
    //   that EXISTS in the type, the type checker SHALL NOT report MIST_TC_0053
    //   (pattern undefined property).
    //
    // **Validates: Requirements 5.1**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypePatternMatchingPropertyTests) })]
    public Property ValidPropertyPattern_AcceptedWithoutError(ValidPatternScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var undefinedPropertyDiags = diagnostics
            .Where(d => d.Id.Value == PatternUndefinedPropertyId
                        && d.Message.Contains(scenario.PatternedName, StringComparison.Ordinal))
            .ToList();

        if (undefinedPropertyDiags.Count > 0)
        {
            var messages = string.Join("\n", undefinedPropertyDiags.Select(d => $"  {d.Message}"));
            return false.ToProperty().Label(
                $"Expected no {PatternUndefinedPropertyId} for pattern '{scenario.PatternedName}' but got:\n"
                + $"{messages}\nScenario: {scenario}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 10 (invalid): Property patterns referencing non-existent names
    //                         produce MIST_TC_0053
    //
    //   For any AnonymousType and a property pattern referencing a property name
    //   that DOES NOT exist in the type, the type checker SHALL report
    //   MIST_TC_0053.
    //
    // **Validates: Requirements 5.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypePatternMatchingPropertyTests) })]
    public Property InvalidPropertyPattern_ProducesUndefinedPropertyDiagnostic(InvalidPatternScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var undefinedPropertyDiags = diagnostics
            .Where(d => d.Id.Value == PatternUndefinedPropertyId
                        && d.Message.Contains(scenario.NonExistentName, StringComparison.Ordinal))
            .ToList();

        if (undefinedPropertyDiags.Count == 0)
        {
            var allDiags = string.Join("\n", diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty().Label(
                $"Expected {PatternUndefinedPropertyId} for pattern '{scenario.NonExistentName}' but got none.\n"
                + $"Scenario: {scenario}\nAll diagnostics: [{allDiags}]\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 13: Property patterns on `unknown` accept any property names
    //
    //   For any set of property names in a property pattern and a scrutinee of type
    //   `unknown`, the type checker SHALL accept the pattern without reporting
    //   missing-property errors (MIST_TC_0053), deferring validation to runtime.
    //
    // **Validates: Requirements 8.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypePatternMatchingPropertyTests) })]
    public Property UnknownScrutinee_AcceptsAnyPropertyNames(UnknownPatternScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var undefinedPropertyDiags = diagnostics
            .Where(d => d.Id.Value == PatternUndefinedPropertyId)
            .ToList();

        if (undefinedPropertyDiags.Count > 0)
        {
            var messages = string.Join("\n", undefinedPropertyDiags.Select(d => $"  {d.Message}"));
            return false.ToProperty().Label(
                $"Expected no {PatternUndefinedPropertyId} for unknown-typed scrutinee but got:\n"
                + $"{messages}\nScenario: {scenario}\nSource:\n{scenario.Source}");
        }

        // Also verify no error-level diagnostics (the pattern should compile cleanly).
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (errors.Count > 0)
        {
            var messages = string.Join("\n", errors.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty().Label(
                $"Expected zero errors for unknown-typed pattern but got {errors.Count}:\n"
                + $"{messages}\nScenario: {scenario}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty();
    }
}
