// Feature: type-argument-inference, Property 10: Overload resolution filters by inference success
// **Validates: Requirements 5.1, 5.2, 5.5**
//
// For any generic method call without type arguments that has multiple overload
// candidates, the compiler SHALL attempt inference independently for each candidate,
// exclude candidates for which inference fails, and select from the remaining
// candidates using normal overload resolution rules. When no candidates survive
// inference, a "no applicable overload" diagnostic SHALL be emitted.
//
// These tests compile Mist programs in-process via CompilerDriver and verify that
// the correct overload is selected when some candidates have inferrable type
// parameters and others don't.

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

/// <summary>
/// Represents a scenario where a method has multiple overloads, some with inferrable
/// type parameters and some without. The test verifies that the compiler correctly
/// filters out candidates where inference fails and selects from the remaining ones.
/// </summary>
public sealed class OverloadInferenceFilterScenario
{
    /// <summary>The complete Mist source code for the scenario.</summary>
    public string Source { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    /// <summary>Whether the scenario should compile without errors.</summary>
    public bool ShouldCompileCleanly { get; }

    /// <summary>
    /// When ShouldCompileCleanly is false, the expected diagnostic ID.
    /// </summary>
    public string? ExpectedDiagnosticId { get; }

    public OverloadInferenceFilterScenario(
        string source,
        string description,
        bool shouldCompileCleanly,
        string? expectedDiagnosticId = null)
    {
        Source = source;
        Description = description;
        ShouldCompileCleanly = shouldCompileCleanly;
        ExpectedDiagnosticId = expectedDiagnosticId;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that overload resolution filters candidates
/// by inference success.
///
/// Property 10: Overload resolution filters by inference success
///
/// For any generic method call without type arguments that has multiple overload
/// candidates, the compiler SHALL attempt inference independently for each candidate,
/// exclude candidates for which inference fails, and select from the remaining
/// candidates using normal overload resolution rules. When no candidates survive
/// inference, a "no applicable overload" diagnostic SHALL be emitted.
///
/// **Validates: Requirements 5.1, 5.2, 5.5**
/// </summary>
public sealed class TypeArgumentInferenceOverloadPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public TypeArgumentInferenceOverloadPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistOverloadInfPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across all iterations) ────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Type and method name pools ──────────────────────────────────────

    private static readonly string[] ConcreteTypes = ["int", "string", "bool"];
    private static readonly string[] MethodNames = ["Process", "Transform", "Handle", "Execute"];
    private static readonly string[] ParamNames = ["a", "b", "c", "x"];

    /// <summary>
    /// Returns a default literal value for the given Mist type name.
    /// </summary>
    private static string DefaultValueFor(string typeName) => typeName switch
    {
        "int" => "42",
        "string" => "\"hello\"",
        "bool" => "true",
        _ => "0",
    };

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a non-generic overload with matching parameter type
    /// coexists with a generic overload whose type parameter cannot be inferred
    /// (T only in return type, not in parameter position) and has different arity.
    /// The non-generic overload should be selected because the generic one is excluded
    /// by both inference failure and arity mismatch.
    ///
    /// Validates Requirement 5.1: inference attempted independently for each candidate.
    /// Validates Requirement 5.2: candidate surviving inference filtering is selected.
    /// </summary>
    private static Gen<OverloadInferenceFilterScenario> GenNonGenericSelectedAfterGenericFailsInference()
    {
        return from typeName in Gen.Elements(ConcreteTypes)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let source = $$"""
                   class Helpers {
                       public static {{typeName}} {{methodName}}({{typeName}} {{paramName}}) {
                           return {{paramName}};
                       }
                       public static int {{methodName}}<T>(int {{paramName}}, string extra) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}({{value}});
                       }
                   }
                   """
               select new OverloadInferenceFilterScenario(source,
                   $"Non-generic {methodName}({typeName}) selected after generic {methodName}<T>(int, string) excluded — call with ({typeName})",
                   shouldCompileCleanly: true);
    }

    /// <summary>
    /// Generates a scenario where a non-generic overload with matching arity
    /// coexists with a generic overload that has wrong arity. The non-generic
    /// overload should be selected because the generic one fails on arity.
    /// The generic overload's inference is irrelevant since arity mismatch
    /// excludes it first.
    ///
    /// Validates Requirement 5.1: inference attempted for generic candidate.
    /// Validates Requirement 5.2: non-generic candidate passes through.
    /// </summary>
    private static Gen<OverloadInferenceFilterScenario> GenNonGenericSelectedVsGenericWrongArity()
    {
        return from typeName in Gen.Elements(ConcreteTypes)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let source = $$"""
                   class Helpers {
                       public static {{typeName}} {{methodName}}({{typeName}} {{paramName}}) {
                           return {{paramName}};
                       }
                       public static int {{methodName}}<T>(int {{paramName}}, int extra) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}({{value}});
                       }
                   }
                   """
               select new OverloadInferenceFilterScenario(source,
                   $"Non-generic {methodName}({typeName}) vs generic wrong arity {methodName}<T>(int, int) — call with ({typeName})",
                   shouldCompileCleanly: true);
    }

    /// <summary>
    /// Generates a scenario where multiple generic overloads exist but all have
    /// uninferrable type parameters (type parameter only in return type, not in
    /// any parameter position) and different arities so they don't conflict.
    /// Since all candidates fail inference and the call has wrong arity for some,
    /// the compiler should emit a "no overload matches" diagnostic.
    ///
    /// Validates Requirement 5.5: when no candidates survive inference, a diagnostic
    /// is emitted.
    /// </summary>
    private static Gen<OverloadInferenceFilterScenario> GenAllGenericCandidatesFailInference()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let source = $$"""
                   class Helpers {
                       public static int {{methodName}}<T>(int {{paramName}}, string extra) {
                           return {{paramName}};
                       }
                       public static string {{methodName}}<U>(string {{paramName}}, int extra) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}(42, "test");
                       }
                   }
                   """
               select new OverloadInferenceFilterScenario(source,
                   $"All generic candidates fail inference: {methodName}<T>(int, string) and {methodName}<U>(string, int) — call with (int, string)",
                   shouldCompileCleanly: false,
                   expectedDiagnosticId: "MIST_TC_0031");
    }

    /// <summary>
    /// Generates a scenario with an instance method where a non-generic overload
    /// with matching parameter type coexists with a generic overload that has
    /// different arity (so it's excluded by arity mismatch). The non-generic
    /// overload should be selected.
    ///
    /// Validates Requirement 5.1: inference works for instance method overloads.
    /// Validates Requirement 5.2: non-generic candidate selected after generic excluded.
    /// </summary>
    private static Gen<OverloadInferenceFilterScenario> GenInstanceMethodNonGenericSelected()
    {
        return from typeName in Gen.Elements(ConcreteTypes)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let source = $$"""
                   class Container {
                       public {{typeName}} {{methodName}}({{typeName}} {{paramName}}) {
                           return {{paramName}};
                       }
                       public int {{methodName}}<T>(int {{paramName}}, int extra) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var c = new Container();
                           var result = c.{{methodName}}({{value}});
                       }
                   }
                   """
               select new OverloadInferenceFilterScenario(source,
                   $"Instance: non-generic {methodName}({typeName}) selected after generic {methodName}<T>(int, int) excluded — call with ({typeName})",
                   shouldCompileCleanly: true);
    }

    /// <summary>
    /// Generates a scenario where two non-generic overloads exist alongside a
    /// generic overload with uninferrable type parameter and different arity.
    /// The non-generic overload with matching type should be selected (the generic
    /// one is excluded by inference failure and arity mismatch, the other non-generic
    /// fails on type mismatch).
    ///
    /// Validates Requirement 5.1: inference attempted for generic candidate.
    /// Validates Requirement 5.2: remaining candidates use normal resolution rules.
    /// </summary>
    private static Gen<OverloadInferenceFilterScenario> GenMultipleNonGenericWithGenericExcluded()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let source = $$"""
                   class Helpers {
                       public static int {{methodName}}(int {{paramName}}) {
                           return {{paramName}};
                       }
                       public static string {{methodName}}(string {{paramName}}) {
                           return {{paramName}};
                       }
                       public static int {{methodName}}<T>(int {{paramName}}, int extra) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}(42);
                       }
                   }
                   """
               select new OverloadInferenceFilterScenario(source,
                   $"Non-generic {methodName}(int) selected among {methodName}(string) and generic {methodName}<T>(int, int) — call with (int)",
                   shouldCompileCleanly: true);
    }

    /// <summary>
    /// Generates a scenario where a non-generic overload with matching type exists
    /// alongside a generic overload that has a conflicting inference (same type
    /// parameter bound to different types from different argument positions).
    /// The generic candidate is excluded by inference failure (conflict), and the
    /// non-generic candidate is selected.
    ///
    /// Validates Requirement 5.1: inference attempted independently, conflict detected.
    /// Validates Requirement 5.2: candidate with failed inference excluded.
    /// </summary>
    private static Gen<OverloadInferenceFilterScenario> GenNonGenericSelectedAfterGenericConflict()
    {
        return from methodName in Gen.Elements(MethodNames)
               let source = $$"""
                   class Helpers {
                       public static string {{methodName}}(int a, string b) {
                           return b;
                       }
                       public static int {{methodName}}<T>(T a, T b) {
                           return 0;
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}(42, "hello");
                       }
                   }
                   """
               select new OverloadInferenceFilterScenario(source,
                   $"Non-generic {methodName}(int, string) selected after generic {methodName}<T>(T, T) has conflicting inference — call with (int, string)",
                   shouldCompileCleanly: true);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<OverloadInferenceFilterScenario> GenScenario()
    {
        return Gen.OneOf(
            GenNonGenericSelectedAfterGenericFailsInference(),
            GenNonGenericSelectedVsGenericWrongArity(),
            GenAllGenericCandidatesFailInference(),
            GenInstanceMethodNonGenericSelected(),
            GenMultipleNonGenericWithGenericExcluded(),
            GenNonGenericSelectedAfterGenericConflict());
    }

    public static Arbitrary<OverloadInferenceFilterScenario> ArbitraryOverloadInferenceFilterScenario() =>
        GenScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 10: Overload resolution filters by inference success
    ///
    /// For any generic method call without type arguments that has multiple overload
    /// candidates, the compiler SHALL attempt inference independently for each candidate,
    /// exclude candidates for which inference fails, and select from the remaining
    /// candidates using normal overload resolution rules.
    ///
    /// This property verifies two aspects:
    /// 1. When a non-generic candidate exists alongside generic candidates that fail
    ///    inference, the non-generic candidate is selected (compiles without errors).
    /// 2. When all candidates fail inference, a diagnostic is emitted.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceOverloadPropertyTests) })]
    public Property OverloadResolution_FiltersBy_InferenceSuccess(OverloadInferenceFilterScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        if (scenario.ShouldCompileCleanly)
        {
            // When a non-generic candidate survives after generic candidates are
            // excluded by inference failure, the program should compile without errors.
            var errors = result.Diagnostics
                .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
                .ToList();

            var noErrors = errors.Count == 0;

            return noErrors.ToProperty()
                .Label($"Expected: zero errors (correct overload selected via inference filtering).\n"
                     + $"  Errors found: {errors.Count}\n"
                     + $"  Diagnostics: [{string.Join(", ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n"
                     + $"  Scenario: {scenario.Description}\n"
                     + $"  Source:\n{scenario.Source}");
        }
        else
        {
            // When no candidates survive inference, a diagnostic should be emitted.
            var errors = result.Diagnostics
                .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
                .ToList();

            var hasExpectedDiagnostic = errors.Any(d => d.Id.Value == scenario.ExpectedDiagnosticId);

            return hasExpectedDiagnostic.ToProperty()
                .Label($"Expected: diagnostic {scenario.ExpectedDiagnosticId} when no candidates survive inference.\n"
                     + $"  Errors found: {errors.Count}\n"
                     + $"  Diagnostics: [{string.Join(", ", errors.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n"
                     + $"  Scenario: {scenario.Description}\n"
                     + $"  Source:\n{scenario.Source}");
        }
    }
}
