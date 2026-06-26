// Feature: ownership-system, Property 2: Lending compatibility matrix

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for the lending compatibility matrix.
///
/// Property 2: Lending compatibility matrix
/// For any variable at ownership tier X passed to a parameter at ownership tier Y,
/// the compiler SHALL accept the call if and only if the (X, Y) pair is in the permitted set:
/// {(owned, owned), (owned, ref), (owned, readonly ref), (owned, frozen ref),
///  (ref, ref), (ref, readonly ref), (ref, frozen ref),
///  (readonly ref, readonly ref),
///  (frozen ref, frozen ref), (frozen ref, readonly ref),
///  (unrestricted, readonly ref), (unrestricted, unrestricted)}.
/// Value types SHALL satisfy any tier regardless of source tier.
/// All other combinations SHALL produce a diagnostic error.
///
/// **Validates: Requirements 3.5, 3.6, 3.7, 3.8, 6.5, 6.6, 6.7, 7.6, 7.7, 7.8,
///              12.1, 12.2, 12.3, 12.4, 12.5, 12.6, 14.1, 14.2, 15.1, 15.3**
/// </summary>
public class OwnershipLendingPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
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

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Lending compatibility matrix ────────────────────────────────────

    /// <summary>
    /// The permitted lending pairs: (source tier, target tier).
    /// Note: ReadonlyRef is excluded because the grammar change (ref-parameters feature)
    /// now parses "readonly ref" in parameter position as a ref modifier, not an ownership specifier.
    /// </summary>
    private static readonly HashSet<(OwnershipTier Source, OwnershipTier Target)> PermittedPairs = new()
    {
        (OwnershipTier.Owned, OwnershipTier.Owned),
        (OwnershipTier.Owned, OwnershipTier.Borrowed),
        (OwnershipTier.Owned, OwnershipTier.Frozen),
        (OwnershipTier.Borrowed, OwnershipTier.Borrowed),
        (OwnershipTier.Borrowed, OwnershipTier.Frozen),
        (OwnershipTier.Frozen, OwnershipTier.Frozen),
        (OwnershipTier.Unrestricted, OwnershipTier.Unrestricted),
        // v2 IClonable bridge: non-owned sources of clonable types can be implicitly
        // cloned into owned destinations. The test classes are empty owned classes that
        // auto-derive IClonable, so these transitions are accepted via implicit clone.
        (OwnershipTier.Frozen, OwnershipTier.Owned),
        (OwnershipTier.Borrowed, OwnershipTier.Owned),
        (OwnershipTier.Unrestricted, OwnershipTier.Owned),
    };

    /// <summary>
    /// All ownership tiers that can appear on variables/parameters.
    /// Note: ReadonlyRef is excluded because the grammar change (ref-parameters feature)
    /// now parses "readonly ref" in parameter position as a ref modifier, not an ownership specifier.
    /// </summary>
    private static readonly OwnershipTier[] AllTiers =
    [
        OwnershipTier.Unrestricted,
        OwnershipTier.Frozen,
        OwnershipTier.Borrowed,
        OwnershipTier.Owned,
    ];

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// A lending scenario: a variable at source tier is passed to a parameter at target tier.
    /// </summary>
    public sealed record LendingScenario(
        OwnershipTier SourceTier,
        OwnershipTier TargetTier,
        string Source,
        bool IsPermitted,
        string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// A value type lending scenario: a value type variable passed to any tier parameter.
    /// </summary>
    public sealed record ValueTypeLendingScenario(
        OwnershipTier TargetTier,
        string Source,
        string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Widget", "Handler", "Service", "Processor", "Manager", "Controller"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "obj", "item", "thing"];

    private static readonly string[] MethodNames =
        ["Process", "Handle", "Transform", "Execute", "Apply", "Consume"];

    // ─── Source code generation helpers ──────────────────────────────────

    /// <summary>
    /// Returns the Mist source code specifier prefix for a given ownership tier.
    /// </summary>
    private static string TierToSpecifier(OwnershipTier tier) => tier switch
    {
        OwnershipTier.Owned => "owned ",
        OwnershipTier.Borrowed => "borrowed ",
        OwnershipTier.ReadonlyRef => "readonly ref ",
        OwnershipTier.Frozen => "frozen ",
        OwnershipTier.Unrestricted => "",
        _ => "",
    };

    /// <summary>
    /// Generates a Mist program that declares a variable at the source tier and passes
    /// it to a method parameter at the target tier.
    /// </summary>
    private static string BuildLendingProgram(
        OwnershipTier sourceTier,
        OwnershipTier targetTier,
        string className,
        string varName,
        string methodName)
    {
        var sourceSpec = TierToSpecifier(sourceTier);
        var targetSpec = TierToSpecifier(targetTier);

        // For ref/readonly ref/frozen ref source tiers, the variable must be obtained
        // by lending from an owned variable (since refs are non-storable and must come
        // from somewhere). We create an owned source and lend into a nested scope.
        // For owned and unrestricted, we can declare directly.
        return sourceTier switch
        {
            OwnershipTier.Owned => $$"""
                owned class {{className}} {
                }
                class Program {
                    public static void {{methodName}}({{targetSpec}}{{className}} param) {
                    }
                    public static void Main() {
                        owned {{className}} {{varName}} = new {{className}}();
                        {{methodName}}({{varName}});
                    }
                }
                """,

            OwnershipTier.Unrestricted => $$"""
                owned class {{className}} {
                }
                class Program {
                    public static void {{methodName}}({{targetSpec}}{{className}} param) {
                    }
                    public static void Main() {
                        {{className}} {{varName}} = new {{className}}();
                        {{methodName}}({{varName}});
                    }
                }
                """,

            OwnershipTier.Borrowed => $$"""
                owned class {{className}} {
                }
                class Program {
                    public static void {{methodName}}({{targetSpec}}{{className}} param) {
                    }
                    public static void Lender(borrowed {{className}} {{varName}}) {
                        {{methodName}}({{varName}});
                    }
                    public static void Main() {
                        owned {{className}} source = new {{className}}();
                        Lender(source);
                    }
                }
                """,

            OwnershipTier.ReadonlyRef => $$"""
                owned class {{className}} {
                }
                class Program {
                    public static void {{methodName}}({{targetSpec}}{{className}} param) {
                    }
                    public static void Lender(readonly ref {{className}} {{varName}}) {
                        {{methodName}}({{varName}});
                    }
                    public static void Main() {
                        owned {{className}} source = new {{className}}();
                        Lender(source);
                    }
                }
                """,

            OwnershipTier.Frozen => $$"""
                owned class {{className}} {
                }
                class Program {
                    public static void {{methodName}}({{targetSpec}}{{className}} param) {
                    }
                    public static void Lender(frozen {{className}} {{varName}}) {
                        {{methodName}}({{varName}});
                    }
                    public static void Main() {
                        owned {{className}} source = new {{className}}();
                        Lender(source);
                    }
                }
                """,

            _ => throw new ArgumentOutOfRangeException(nameof(sourceTier)),
        };
    }

    /// <summary>
    /// Generates a Mist program that passes a value type (int) to a parameter at the
    /// given target tier.
    /// </summary>
    private static string BuildValueTypeLendingProgram(
        OwnershipTier targetTier,
        string methodName)
    {
        var targetSpec = TierToSpecifier(targetTier);

        return $$"""
            class Program {
                public static void {{methodName}}({{targetSpec}}int param) {
                }
                public static void Main() {
                    int x = 42;
                    {{methodName}}(x);
                }
            }
            """;
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<LendingScenario> GenLendingScenario()
    {
        return from sourceTier in Gen.Elements(AllTiers)
               from targetTier in Gen.Elements(AllTiers)
               from className in Gen.Elements(ClassNames)
               from varName in Gen.Elements(VarNames)
               from methodName in Gen.Elements(MethodNames)
               let isPermitted = PermittedPairs.Contains((sourceTier, targetTier))
               let source = BuildLendingProgram(sourceTier, targetTier, className, varName, methodName)
               let label = $"{sourceTier}->{targetTier} (permitted={isPermitted})"
               select new LendingScenario(sourceTier, targetTier, source, isPermitted, label);
    }

    private static Gen<ValueTypeLendingScenario> GenValueTypeLendingScenario()
    {
        return from targetTier in Gen.Elements(AllTiers)
               from methodName in Gen.Elements(MethodNames)
               let source = BuildValueTypeLendingProgram(targetTier, methodName)
               let label = $"int->{targetTier} (value type, always permitted)"
               select new ValueTypeLendingScenario(targetTier, source, label);
    }

    // ─── Arbitrary instances ─────────────────────────────────────────────

    public static Arbitrary<LendingScenario> ArbitraryLendingScenario() =>
        GenLendingScenario().ToArbitrary();

    public static Arbitrary<ValueTypeLendingScenario> ArbitraryValueTypeLendingScenario() =>
        GenValueTypeLendingScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2a: Permitted lending pairs produce no lending diagnostics
    // **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 12.6**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipLendingPropertyTests) })]
    public Property LendingCompatibility_MatchesMatrix(LendingScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasLendingError = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_INCOMPATIBLE_LEND" ||
            d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");

        var result = scenario.IsPermitted
            ? !hasLendingError
            : hasLendingError;

        return result.ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Source tier: {scenario.SourceTier}, Target tier: {scenario.TargetTier}")
            .Label($"Expected permitted: {scenario.IsPermitted}, Has lending error: {hasLendingError}")
            .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2b: Value types always satisfy any tier requirement
    // **Validates: Requirements 14.1, 14.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(OwnershipLendingPropertyTests) })]
    public Property ValueTypes_AlwaysSatisfyAnyTier(ValueTypeLendingScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasLendingError = diagnostics.Any(d =>
            d.Id.Value == "MIST_OWN_INCOMPATIBLE_LEND" ||
            d.Id.Value == "MIST_OWN_UNRESTRICTED_TO_REF");

        return (!hasLendingError).ToProperty()
            .Label($"Scenario: {scenario.Label}")
            .Label($"Value types should always pass, but got lending error")
            .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}