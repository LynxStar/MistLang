// Feature: switch-expressions, Property 7: Result Type Unification
// **Validates: Requirements 5.1, 5.2, 5.4, 5.5**

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
/// Property-based tests for result type unification in switch expressions.
///
/// **Validates: Requirements 5.1, 5.2, 5.4, 5.5**
///
/// For any switch expression where all arm result expressions have the same type <c>T</c>,
/// the inferred result type SHALL be <c>T</c>. For incompatible types, MIST_TC_0091 SHALL be emitted.
/// </summary>
public class ResultTypeUnificationPropertyTests
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

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Scenario: all arms return the same type — no MIST_TC_0091 expected.
    /// </summary>
    public sealed record SameTypeScenario(string Source, string ResultType, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    /// <summary>
    /// Scenario: arms return incompatible types — MIST_TC_0091 expected.
    /// </summary>
    public sealed record IncompatibleTypeScenario(string Source, string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly (string TypeName, string[] Literals)[] TypeLiterals =
    [
        ("string", ["\"hello\"", "\"world\"", "\"foo\"", "\"bar\""]),
        ("int", ["0", "1", "42", "99"]),
        ("bool", ["true", "false"]),
    ];

    public static Arbitrary<SameTypeScenario> ArbitrarySameTypeScenario()
    {
        var gen = from typeIdx in Gen.Choose(0, TypeLiterals.Length - 1)
                  from armCount in Gen.Choose(2, 5)
                  from literals in Gen.ArrayOf(
                      Gen.Elements(TypeLiterals[typeIdx].Literals), armCount)
                  let typeName = TypeLiterals[typeIdx].TypeName
                  let arms = literals.Select((lit, i) => $"{i} => {lit}").ToList()
                  let catchAll = $"var _ => {TypeLiterals[typeIdx].Literals[0]}"
                  let allArms = arms.Append(catchAll)
                  let armsText = string.Join(", ", allArms)
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        int x = 0;\n        var r = x switch {{ {armsText} }};\n    }}\n}}"
                  select new SameTypeScenario(source, typeName, $"all arms return {typeName}");
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios where arms return incompatible types (e.g., string and int).
    /// </summary>
    public static Arbitrary<IncompatibleTypeScenario> ArbitraryIncompatibleTypeScenario()
    {
        var gen = from type1Idx in Gen.Choose(0, TypeLiterals.Length - 1)
                  from type2Idx in Gen.Choose(0, TypeLiterals.Length - 1)
                  where type1Idx != type2Idx
                  from lit1 in Gen.Elements(TypeLiterals[type1Idx].Literals)
                  from lit2 in Gen.Elements(TypeLiterals[type2Idx].Literals)
                  let type1 = TypeLiterals[type1Idx].TypeName
                  let type2 = TypeLiterals[type2Idx].TypeName
                  let source = $"namespace TestNs;\n\npublic class Program\n{{\n    public static void Main()\n    {{\n        int x = 0;\n        var r = x switch {{ 0 => {lit1}, var _ => {lit2} }};\n    }}\n}}"
                  select new IncompatibleTypeScenario(source, $"{type1} vs {type2}");
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7a: Same-type arms produce no MIST_TC_0091
    //
    // **Validates: Requirements 5.1, 5.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ResultTypeUnificationPropertyTests) })]
    public Property SameTypeArms_ProduceNoIncompatibleTypeDiagnostic(SameTypeScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasIncompatible = diagnostics.Any(d => d.Id.Value == "MIST_TC_0091");

        return (!hasIncompatible).ToProperty()
            .Label($"Unexpected MIST_TC_0091 for same-type arms. Scenario: {scenario.Label}. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7b: Incompatible-type arms produce MIST_TC_0091
    //
    // **Validates: Requirements 5.4, 5.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ResultTypeUnificationPropertyTests) })]
    public Property IncompatibleTypeArms_ProduceIncompatibleTypeDiagnostic(IncompatibleTypeScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);
        var hasIncompatible = diagnostics.Any(d => d.Id.Value == "MIST_TC_0091");

        return hasIncompatible.ToProperty()
            .Label($"Expected MIST_TC_0091 for incompatible types. Scenario: {scenario.Label}. " +
                   $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}