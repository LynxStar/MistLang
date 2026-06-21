// Feature: optional-defaultability, Property 8: Enum default variant designation
// **Validates: Requirements 6.3, 6.4, 6.5, 6.6**

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

/// <summary>
/// Property-based tests for enum defaultability classification and default(E) validation.
///
/// Property 8: For any enum type with exactly one variant marked <c>default</c>,
/// the compiler SHALL classify the enum as defaultable and <c>default(E)</c> SHALL be valid.
/// For any enum with no <c>default</c> variant, <c>default(E)</c> SHALL produce
/// <c>MIST_DEFAULT_NOT_SUPPORTED</c>.
/// For any enum with multiple <c>default</c> variants, the compiler SHALL emit
/// <c>MIST_ENUM_MULTIPLE_DEFAULTS</c>.
/// </summary>
public class EnumDefaultabilityPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_enum_dflt_pbt_" + Guid.NewGuid().ToString("N"));
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

    private static bool HasDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id) =>
        diagnostics.Any(d => d.Id.Value == id);

    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario for an enum with exactly one default variant.
    /// Classification should be Explicit, and default(E) should be valid.
    /// </summary>
    public sealed record SingleDefaultScenario(
        string EnumName,
        string BackingType,
        IReadOnlyList<string> VariantNames,
        int DefaultVariantIndex)
    {
        public override string ToString() =>
            $"enum {EnumName} : {BackingType} (default={VariantNames[DefaultVariantIndex]}, {VariantNames.Count} variants)";
    }

    /// <summary>
    /// Scenario for an enum with no default variant.
    /// Classification should be NotDefaultable, and default(E) should emit MIST_DEFAULT_NOT_SUPPORTED.
    /// </summary>
    public sealed record NoDefaultScenario(
        string EnumName,
        string BackingType,
        IReadOnlyList<string> VariantNames)
    {
        public override string ToString() =>
            $"enum {EnumName} : {BackingType} (no default, {VariantNames.Count} variants)";
    }

    /// <summary>
    /// Scenario for an enum with multiple default variants.
    /// Should emit MIST_ENUM_MULTIPLE_DEFAULTS diagnostic.
    /// </summary>
    public sealed record MultipleDefaultScenario(
        string EnumName,
        string BackingType,
        IReadOnlyList<string> VariantNames,
        IReadOnlyList<int> DefaultVariantIndices)
    {
        public override string ToString() =>
            $"enum {EnumName} : {BackingType} ({DefaultVariantIndices.Count} defaults, {VariantNames.Count} variants)";
    }

#pragma warning restore CA1034

    // ─── Name and type pools ─────────────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Direction", "Color", "Status", "Priority", "Level", "Mode", "Season"];

    private static readonly string[] VariantNamePool =
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending", "High", "Low", "Medium"];

    private static readonly string[] BackingTypes = ["int", "string", "byte"];

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<SingleDefaultScenario> GenSingleDefaultScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from backingType in Gen.Elements(BackingTypes)
               from variantCount in Gen.Choose(1, 5)
               from variants in Gen.Shuffle(VariantNamePool)
                   .Select(m => m.Take(variantCount).ToList())
               from defaultIdx in Gen.Choose(0, variantCount - 1)
               select new SingleDefaultScenario(enumName, backingType, variants, defaultIdx);
    }

    private static Gen<NoDefaultScenario> GenNoDefaultScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from backingType in Gen.Elements(BackingTypes)
               from variantCount in Gen.Choose(1, 5)
               from variants in Gen.Shuffle(VariantNamePool)
                   .Select(m => m.Take(variantCount).ToList())
               select new NoDefaultScenario(enumName, backingType, variants);
    }

    private static Gen<MultipleDefaultScenario> GenMultipleDefaultScenario()
    {
        return from enumName in Gen.Elements(EnumNames)
               from backingType in Gen.Elements(BackingTypes)
               from variantCount in Gen.Choose(2, 5)
               from variants in Gen.Shuffle(VariantNamePool)
                   .Select(m => m.Take(variantCount).ToList())
               from defaultCount in Gen.Choose(2, variantCount)
               from defaultIndices in Gen.Shuffle(Enumerable.Range(0, variantCount).ToArray())
                   .Select(m => m.Take(defaultCount).ToList())
               select new MultipleDefaultScenario(enumName, backingType, variants, defaultIndices);
    }

    // ─── Arbitrary registrations ─────────────────────────────────────────

    public static Arbitrary<SingleDefaultScenario> ArbitrarySingleDefault() =>
        GenSingleDefaultScenario().ToArbitrary();

    public static Arbitrary<NoDefaultScenario> ArbitraryNoDefault() =>
        GenNoDefaultScenario().ToArbitrary();

    public static Arbitrary<MultipleDefaultScenario> ArbitraryMultipleDefault() =>
        GenMultipleDefaultScenario().ToArbitrary();

    // ─── Source generation helpers ───────────────────────────────────────

    private static string GenerateEnumSource(
        string enumName,
        string backingType,
        IReadOnlyList<string> variantNames,
        IReadOnlyList<int>? defaultIndices)
    {
        var defaultSet = defaultIndices is not null
            ? new HashSet<int>(defaultIndices)
            : new HashSet<int>();

        var members = new List<string>();
        for (var i = 0; i < variantNames.Count; i++)
        {
            var prefix = defaultSet.Contains(i) ? "default " : "";
            var value = backingType == "string"
                ? $"\"{variantNames[i]}\""
                : i.ToString();
            members.Add($"    {prefix}{variantNames[i]} = {value}");
        }

        return $@"namespace TestProgram;

public enum {enumName} : {backingType}
{{
{string.Join(",\n", members)}
}}
";
    }

    private static string GenerateEnumWithDefaultUsageSource(
        string enumName,
        string backingType,
        IReadOnlyList<string> variantNames,
        int? defaultVariantIndex)
    {
        var defaultIndices = defaultVariantIndex.HasValue
            ? new List<int> { defaultVariantIndex.Value }
            : null;

        var enumDecl = GenerateEnumSource(enumName, backingType, variantNames, defaultIndices);

        // Add a class that uses default(EnumName) to trigger type checker validation
        return $@"{enumDecl}
public class Consumer
{{
    public static {enumName} GetDefault()
    {{
        return default({enumName});
    }}
}}
";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8a: Enum with exactly one default variant is classified as
    // defaultable and default(E) is valid (no error diagnostic).
    //
    // **Validates: Requirements 6.3, 6.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumDefaultabilityPropertyTests) },
        MaxTest = 100)]
    public Property SingleDefaultVariant_EnumIsDefaultable_AndDefaultExpressionIsValid(
        SingleDefaultScenario scenario)
    {
        var source = GenerateEnumWithDefaultUsageSource(
            scenario.EnumName,
            scenario.BackingType,
            scenario.VariantNames,
            scenario.DefaultVariantIndex);

        var diagnostics = CompileAndGetDiagnostics(source);

        var hasDefaultNotSupported = HasDiagnostic(diagnostics, "MIST_DEFAULT_NOT_SUPPORTED");
        var hasMultipleDefaults = HasDiagnostic(diagnostics, "MIST_ENUM_MULTIPLE_DEFAULTS");

        // With exactly one default variant, neither diagnostic should appear
        return (!hasDefaultNotSupported && !hasMultipleDefaults)
            .ToProperty()
            .Label($"Expected no MIST_DEFAULT_NOT_SUPPORTED or MIST_ENUM_MULTIPLE_DEFAULTS. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => d.Id.Value))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8b: Enum with no default variant is classified as
    // NotDefaultable and default(E) produces MIST_DEFAULT_NOT_SUPPORTED.
    //
    // **Validates: Requirements 6.4, 6.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumDefaultabilityPropertyTests) },
        MaxTest = 100)]
    public Property NoDefaultVariant_DefaultExpression_ProducesDefaultNotSupported(
        NoDefaultScenario scenario)
    {
        var source = GenerateEnumWithDefaultUsageSource(
            scenario.EnumName,
            scenario.BackingType,
            scenario.VariantNames,
            defaultVariantIndex: null);

        var diagnostics = CompileAndGetDiagnostics(source);

        var hasDefaultNotSupported = HasDiagnostic(diagnostics, "MIST_DEFAULT_NOT_SUPPORTED");

        return hasDefaultNotSupported
            .ToProperty()
            .Label($"Expected MIST_DEFAULT_NOT_SUPPORTED for enum without default variant. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => d.Id.Value))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8c: Enum with multiple default variants emits
    // MIST_ENUM_MULTIPLE_DEFAULTS diagnostic.
    //
    // **Validates: Requirements 6.3, 6.4** (implicit: 6.7 from requirements)
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        Arbitrary = new[] { typeof(EnumDefaultabilityPropertyTests) },
        MaxTest = 100)]
    public Property MultipleDefaultVariants_EmitsMultipleDefaultsDiagnostic(
        MultipleDefaultScenario scenario)
    {
        var source = GenerateEnumSource(
            scenario.EnumName,
            scenario.BackingType,
            scenario.VariantNames,
            scenario.DefaultVariantIndices);

        var diagnostics = CompileAndGetDiagnostics(source);

        var hasMultipleDefaults = HasDiagnostic(diagnostics, "MIST_ENUM_MULTIPLE_DEFAULTS");

        return hasMultipleDefaults
            .ToProperty()
            .Label($"Expected MIST_ENUM_MULTIPLE_DEFAULTS for enum with {scenario.DefaultVariantIndices.Count} defaults. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => d.Id.Value))}]");
    }
}
