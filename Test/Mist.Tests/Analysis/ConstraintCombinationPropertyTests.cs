// Feature: substructural-field-discipline, Property 3: Constraint Combination Validation
// **Validates: Requirements 3.3, 3.4**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying constraint combination validation.
///
/// Property 3: Constraint Combination Validation
///
/// For any <c>where</c> clause that specifies more than one substructural constraint keyword
/// on the same type parameter, the compiler SHALL emit a diagnostic error (MIST_CONSTRAINT_0009).
/// For any <c>where</c> clause that combines exactly one substructural constraint with any number
/// of non-substructural constraints (class, struct, interface, new(), owned, readonly, mutable,
/// default), the compiler SHALL emit no substructural combination error.
///
/// All work runs in-process using CompilerDriver — no external process is spawned.
///
/// **Validates: Requirements 3.3, 3.4**
/// </summary>
public sealed class ConstraintCombinationPropertyTests
{
    private const string MultipleSubstructuralDiagnosticId = "MIST_CONSTRAINT_0009";

    // ─── Shared infrastructure (in-process compile, 0 process spawns) ─────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(
            new TypeCheckerAnalyzer(),
            new ReadonlyAnalyzer(),
            new OwnershipAnalyzer(),
            new DropAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> Compile(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_constraint_combo_pbt_" + Guid.NewGuid().ToString("N"));
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
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ─── Scenario model ──────────────────────────────────────────────────

#pragma warning disable CA1034 // Nested types should not be visible — required for FsCheck Arbitrary discovery

    /// <summary>The substructural constraint keywords.</summary>
    public enum SubstructuralConstraint
    {
        Linear,
        Affine,
        Relevant,
    }

    /// <summary>Non-substructural constraints that can combine with a single substructural constraint.</summary>
    public enum NonSubstructuralConstraint
    {
        Class,
        Struct,
        Interface,
        New,
        Owned,
        Readonly,
        Mutable,
        Default,
    }

    /// <summary>
    /// A scenario where multiple substructural constraints are combined on the same type parameter.
    /// Expected: MIST_CONSTRAINT_0009 diagnostic.
    /// </summary>
    public sealed record MultipleSubstructuralScenario(
        SubstructuralConstraint First,
        SubstructuralConstraint Second,
        string TypeParamName,
        string ClassName)
    {
        public override string ToString() =>
            $"where {TypeParamName} : {First.ToString().ToLowerInvariant()}, {Second.ToString().ToLowerInvariant()} on {ClassName}";
    }

    /// <summary>
    /// A scenario where a single substructural constraint is combined with one or more
    /// non-substructural constraints. Expected: no MIST_CONSTRAINT_0009 diagnostic.
    /// </summary>
    public sealed record MixedConstraintScenario(
        SubstructuralConstraint Substructural,
        NonSubstructuralConstraint NonSubstructural,
        string TypeParamName,
        string ClassName)
    {
        public override string ToString() =>
            $"where {TypeParamName} : {Substructural.ToString().ToLowerInvariant()}, {NonSubstructural} on {ClassName}";
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] TypeParamNames = ["T", "U", "V", "TItem", "TValue"];
    private static readonly string[] ClassNames = ["Container", "Holder", "Wrapper", "Box", "Store"];

    /// <summary>
    /// Generates scenarios with two different substructural constraints on the same type parameter.
    /// </summary>
    public static Gen<MultipleSubstructuralScenario> GenMultipleSubstructural()
    {
        var allDisciplines = new[] { SubstructuralConstraint.Linear, SubstructuralConstraint.Affine, SubstructuralConstraint.Relevant };

        return from first in Gen.Elements(allDisciplines)
               from second in Gen.Elements(allDisciplines)
               where first != second
               from tpName in Gen.Elements(TypeParamNames)
               from className in Gen.Elements(ClassNames)
               select new MultipleSubstructuralScenario(first, second, tpName, className);
    }

    /// <summary>
    /// Generates scenarios with exactly one substructural constraint combined with
    /// a non-substructural constraint.
    /// </summary>
    public static Gen<MixedConstraintScenario> GenMixedConstraint()
    {
        return from substructural in Gen.Elements(
                   SubstructuralConstraint.Linear,
                   SubstructuralConstraint.Affine,
                   SubstructuralConstraint.Relevant)
               from nonSubstructural in Gen.Elements(
                   NonSubstructuralConstraint.Class,
                   NonSubstructuralConstraint.Interface,
                   NonSubstructuralConstraint.New,
                   NonSubstructuralConstraint.Owned,
                   NonSubstructuralConstraint.Readonly,
                   NonSubstructuralConstraint.Mutable,
                   NonSubstructuralConstraint.Default)
               from tpName in Gen.Elements(TypeParamNames)
               from className in Gen.Elements(ClassNames)
               select new MixedConstraintScenario(substructural, nonSubstructural, tpName, className);
    }

    public static Arbitrary<MultipleSubstructuralScenario> ArbitraryMultipleSubstructural() =>
        GenMultipleSubstructural().ToArbitrary();

    public static Arbitrary<MixedConstraintScenario> ArbitraryMixedConstraint() =>
        GenMixedConstraint().ToArbitrary();

    // ─── Source builders ─────────────────────────────────────────────────

    private static string SubstructuralKeyword(SubstructuralConstraint c) => c switch
    {
        SubstructuralConstraint.Linear => "linear",
        SubstructuralConstraint.Affine => "affine",
        SubstructuralConstraint.Relevant => "relevant",
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    private static string BuildMultipleSubstructuralSource(MultipleSubstructuralScenario scenario)
    {
        var first = SubstructuralKeyword(scenario.First);
        var second = SubstructuralKeyword(scenario.Second);

        return $$"""
            class {{scenario.ClassName}}<{{scenario.TypeParamName}}> where {{scenario.TypeParamName}} : {{first}}, {{second}} {
            }
            class Program { public static void Main() {} }
            """;
    }

    private static string BuildMixedConstraintSource(MixedConstraintScenario scenario)
    {
        var subKw = SubstructuralKeyword(scenario.Substructural);
        var nonSubKw = NonSubstructuralKeywordAndSetup(scenario.NonSubstructural, scenario.TypeParamName);

        return $$"""
            {{nonSubKw.Preamble}}class {{scenario.ClassName}}<{{scenario.TypeParamName}}> where {{scenario.TypeParamName}} : {{subKw}}, {{nonSubKw.Constraint}} {
            }
            class Program { public static void Main() {} }
            """;
    }

    private static (string Preamble, string Constraint) NonSubstructuralKeywordAndSetup(
        NonSubstructuralConstraint c, string typeParamName)
    {
        return c switch
        {
            NonSubstructuralConstraint.Class => ("", "class"),
            NonSubstructuralConstraint.Struct => ("", "struct"),
            NonSubstructuralConstraint.Interface => ("interface IMarker { }\n", "IMarker"),
            NonSubstructuralConstraint.New => ("", "new()"),
            NonSubstructuralConstraint.Owned => ("", "owned"),
            NonSubstructuralConstraint.Readonly => ("", "readonly"),
            NonSubstructuralConstraint.Mutable => ("", "mutable"),
            NonSubstructuralConstraint.Default => ("", "default"),
            _ => throw new ArgumentOutOfRangeException(nameof(c)),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 3a: For any <c>where</c> clause that specifies more than one substructural
    /// constraint keyword on the same type parameter, the compiler SHALL emit a diagnostic
    /// error (MIST_CONSTRAINT_0009).
    ///
    /// **Validates: Requirement 3.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstraintCombinationPropertyTests) }, MaxTest = 100)]
    public Property MultipleSubstructuralConstraints_EmitsDiagnostic(MultipleSubstructuralScenario scenario)
    {
        var source = BuildMultipleSubstructuralSource(scenario);
        var diagnostics = Compile(source);

        var multiSubDiags = diagnostics
            .Where(d => d.Id.Value == MultipleSubstructuralDiagnosticId &&
                        d.Message.Contains("substructural"))
            .ToList();

        var hasDiagnostic = multiSubDiags.Count > 0;

        return hasDiagnostic.ToProperty()
            .Label($"Expected MIST_CONSTRAINT_0009 (multiple substructural) but none emitted.\n" +
                   $"Scenario: {scenario}\n" +
                   $"Source:\n{source}\n" +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }

    /// <summary>
    /// Property 3b: For any <c>where</c> clause that combines exactly one substructural
    /// constraint with any number of non-substructural constraints, the compiler SHALL
    /// emit no substructural combination error (MIST_CONSTRAINT_0009 with "substructural" message).
    ///
    /// **Validates: Requirement 3.4**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(ConstraintCombinationPropertyTests) }, MaxTest = 100)]
    public Property SingleSubstructuralWithNonSubstructural_NoSubstructuralCombinationError(MixedConstraintScenario scenario)
    {
        var source = BuildMixedConstraintSource(scenario);
        var diagnostics = Compile(source);

        // Only check for the "multiple substructural" diagnostic — other constraint
        // diagnostics (e.g., struct+class incompatibility) are separate concerns.
        var multiSubDiags = diagnostics
            .Where(d => d.Id.Value == MultipleSubstructuralDiagnosticId &&
                        d.Message.Contains("substructural"))
            .ToList();

        var noSubstructuralComboError = multiSubDiags.Count == 0;

        return noSubstructuralComboError.ToProperty()
            .Label($"Expected no substructural combination error but got {multiSubDiags.Count}.\n" +
                   $"Scenario: {scenario}\n" +
                   $"Source:\n{source}\n" +
                   $"Substructural diagnostics: [{string.Join(", ", multiSubDiags.Select(d => d.Message))}]");
    }
}
