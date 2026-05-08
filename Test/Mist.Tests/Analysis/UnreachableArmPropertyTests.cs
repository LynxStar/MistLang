// Feature: switch-expressions, Property 5: Unreachable Arm Detection
// **Validates: Requirements 7.1, 7.2, 7.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for unreachable arm detection in switch expressions.
///
/// **Validates: Requirements 7.1, 7.2, 7.3**
///
/// For any switch expression where an unguarded catch-all appears at position <c>i</c>,
/// the type checker SHALL emit MIST_TC_0093 for every arm at position <c>j &gt; i</c>.
/// For duplicate constant patterns, MIST_TC_0093 is emitted for the later arm.
/// </summary>
public class UnreachableArmPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: catch-all at position i with trailing arms after it.
    /// </summary>
    public sealed record CatchAllUnreachableScenario(
        int CatchAllPosition,
        int TotalArms,
        string Source,
        string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Scenario: duplicate constant pattern.
    /// </summary>
    public sealed record DuplicateConstantScenario(
        string Source,
        string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] IntConstants = ["0", "1", "2", "3", "42", "99"];

    public static Arbitrary<CatchAllUnreachableScenario> ArbitraryCatchAllUnreachableScenario()
    {
        // Use distinct constants for leading arms to avoid duplicate-constant unreachable warnings
        var gen = from catchAllPos in Gen.Choose(0, 3)
                  from trailingCount in Gen.Choose(1, 3)
                  from shuffled in Gen.Shuffle(IntConstants)
                  let constants = shuffled.Take(catchAllPos).ToArray()
                  from trailingConstants in Gen.ArrayOf(Gen.Elements(IntConstants), trailingCount)
                  let totalArms = catchAllPos + 1 + trailingCount
                  let leadingArms = constants.Select(c => $"{c} => \"a\"")
                  let catchAllArm = "var _ => \"b\""
                  let trailingArms = trailingConstants.Select(c => $"{c} => \"c\"")
                  let allArms = leadingArms.Append(catchAllArm).Concat(trailingArms)
                  let armsText = string.Join(", ", allArms)
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        int x = 1;\n        var r = x switch {{ {armsText} }};\n    }}\n}}"
                  select new CatchAllUnreachableScenario(catchAllPos, totalArms, source,
                      $"catch-all@{catchAllPos}, total={totalArms}");
        return gen.ToArbitrary();
    }

    public static Arbitrary<DuplicateConstantScenario> ArbitraryDuplicateConstantScenario()
    {
        var gen = from constant in Gen.Elements(IntConstants)
                  from leadingCount in Gen.Choose(0, 2)
                  from leadingConstants in Gen.ArrayOf(
                      Gen.Elements(IntConstants).Where(c => c != constant), leadingCount)
                  let leadingArms = leadingConstants.Select(c => $"{c} => \"a\"")
                  let firstArm = $"{constant} => \"b\""
                  let duplicateArm = $"{constant} => \"c\""
                  let catchAll = "var _ => \"d\""
                  let allArms = leadingArms.Append(firstArm).Append(duplicateArm).Append(catchAll)
                  let armsText = string.Join(", ", allArms)
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        int x = 1;\n        var r = x switch {{ {armsText} }};\n    }}\n}}"
                  select new DuplicateConstantScenario(source,
                      $"duplicate constant={constant}, leading={leadingCount}");
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5a: Catch-all makes trailing arms unreachable
    //
    // **Validates: Requirements 7.1, 7.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnreachableArmPropertyTests) })]
    public Property CatchAll_MakesTrailingArms_Unreachable(CatchAllUnreachableScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var unreachableCount = diagnostics.Count(d => d.Id.Value == "MIST_TC_0093");
        var expectedCount = scenario.TotalArms - scenario.CatchAllPosition - 1;

        return (unreachableCount == expectedCount).ToProperty()
            .Label($"Expected {expectedCount} unreachable warnings, got {unreachableCount}. " +
                   $"Scenario: {scenario.Label}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5b: Duplicate constant pattern is unreachable
    //
    // **Validates: Requirements 7.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnreachableArmPropertyTests) })]
    public Property DuplicateConstant_IsUnreachable(DuplicateConstantScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasUnreachable = diagnostics.Any(d => d.Id.Value == "MIST_TC_0093");

        return hasUnreachable.ToProperty()
            .Label($"Expected MIST_TC_0093 for duplicate constant. Scenario: {scenario.Label}. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}
