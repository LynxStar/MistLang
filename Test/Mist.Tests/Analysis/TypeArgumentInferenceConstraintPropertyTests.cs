// Feature: type-argument-inference, Property 9: Constraint satisfaction uses same diagnostics as explicit
// **Validates: Requirements 4.4, 6.1, 6.2, 6.3, 6.4**
//
// For any generic method with constrained type parameters, when inference produces
// a type argument that violates a constraint, the compiler SHALL emit the same
// diagnostic ID and message format as when an explicit type argument violates the
// same constraint. For type parameters with multiple constraints where the inferred
// type violates more than one, a separate diagnostic SHALL be emitted for each
// violated constraint.
//
// These tests compile Mist programs in-process via CompilerDriver and compare
// diagnostics between explicit and inferred versions of the same constraint violation.

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
/// Represents a scenario where a generic method has a constrained type parameter
/// and is called with a type argument (either explicit or inferred) that violates
/// the constraint. Two source programs are generated: one with explicit type args
/// and one with inferred type args, both producing the same constraint violation.
/// </summary>
public sealed class ConstraintDiagnosticScenario
{
    /// <summary>Mist source where the violating type argument is provided explicitly.</summary>
    public string ExplicitSource { get; }

    /// <summary>Mist source where the violating type argument is inferred from the call-site argument.</summary>
    public string InferredSource { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    public ConstraintDiagnosticScenario(string explicitSource, string inferredSource, string description)
    {
        ExplicitSource = explicitSource;
        InferredSource = inferredSource;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that constraint satisfaction diagnostics are
/// identical whether type arguments are explicit or inferred.
///
/// Property 9: Constraint satisfaction uses same diagnostics as explicit
///
/// For any generic method with constrained type parameters, when inference produces
/// a type argument that violates a constraint, the compiler SHALL emit the same
/// diagnostic ID and message format as when an explicit type argument violates the
/// same constraint.
///
/// **Validates: Requirements 4.4, 6.1, 6.2, 6.3, 6.4**
/// </summary>
public sealed class TypeArgumentInferenceConstraintPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public TypeArgumentInferenceConstraintPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistConstraintPBT_" + Guid.NewGuid().ToString("N"));
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

    // ─── Constraint-related diagnostic IDs ─────────────────────────────────
    // These are the diagnostic IDs that can be emitted when a type argument
    // violates a constraint. The property verifies that the SAME set of
    // diagnostics is emitted regardless of whether type arguments are explicit
    // or inferred.

    private static readonly HashSet<string> ConstraintRelatedDiagnosticIds = new()
    {
        "MIST_CONSTRAINT_0009", // Abstract type not implemented
        "MIST_CONSTRAINT_0010", // Abstract type not satisfied
        "MIST_CONSTRAINT_0011", // Base class not derived
        "MIST_CONSTRAINT_0012", // Class constraint violation (value type given)
        "MIST_CONSTRAINT_0013", // Struct constraint violation (reference type given)
        "MIST_CONSTRAINT_0014", // Parameterless ctor violation
        "MIST_CONSTRAINT_0015", // Parameterized ctor violation
        "MIST_TC_0077",         // Readonly constraint violation
        "MIST_TC_0078",         // Mutable constraint violation
        "MIST_TC_0002",         // Argument type not assignable (emitted alongside constraint violations)
    };

    // ─── Method and parameter name pools ─────────────────────────────────

    private static readonly string[] MethodNames = ["Process", "Transform", "Handle", "Apply"];
    private static readonly string[] ParamNames = ["item", "value", "arg", "input"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a method has a 'struct' constraint on T,
    /// but a reference type (string) is used as the type argument.
    /// int is a struct, string is not — so passing string violates the constraint.
    /// </summary>
    private static Gen<ConstraintDiagnosticScenario> GenStructConstraintViolation()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let explicitSource = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : struct {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<string>("hello");
                       }
                   }
                   """
               let inferredSource = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : struct {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}("hello");
                       }
                   }
                   """
               select new ConstraintDiagnosticScenario(explicitSource, inferredSource,
                   $"struct constraint on {methodName}<T>(T) violated by string (reference type)");
    }

    /// <summary>
    /// Generates a scenario where a method has a 'class' constraint on T,
    /// but a value type (int) is used as the type argument.
    /// </summary>
    private static Gen<ConstraintDiagnosticScenario> GenClassConstraintViolation()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let explicitSource = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : class {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<int>(42);
                       }
                   }
                   """
               let inferredSource = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : class {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}(42);
                       }
                   }
                   """
               select new ConstraintDiagnosticScenario(explicitSource, inferredSource,
                   $"class constraint on {methodName}<T>(T) violated by int (value type)");
    }

    /// <summary>
    /// Generates a scenario where a method has an abstract type constraint on T,
    /// but a type that does not implement the abstract type is used.
    /// We define an abstract type IMarker and pass int which does not implement it.
    /// </summary>
    private static Gen<ConstraintDiagnosticScenario> GenAbstractConstraintViolation()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let explicitSource = $$"""
                   abstract IMarker {
                       void Mark();
                   }
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : IMarker {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<int>(42);
                       }
                   }
                   """
               let inferredSource = $$"""
                   abstract IMarker {
                       void Mark();
                   }
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : IMarker {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}(42);
                       }
                   }
                   """
               select new ConstraintDiagnosticScenario(explicitSource, inferredSource,
                   $"abstract type constraint IMarker on {methodName}<T>(T) violated by int");
    }

    /// <summary>
    /// Generates a scenario where a method has multiple constraints on T
    /// (class + abstract type), and a value type is used which violates both.
    /// This validates Requirement 6.3: a separate diagnostic for each violated constraint.
    /// </summary>
    private static Gen<ConstraintDiagnosticScenario> GenMultipleConstraintViolation()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let explicitSource = $$"""
                   abstract IMarker {
                       void Mark();
                   }
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : class, IMarker {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<int>(42);
                       }
                   }
                   """
               let inferredSource = $$"""
                   abstract IMarker {
                       void Mark();
                   }
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : class, IMarker {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}(42);
                       }
                   }
                   """
               select new ConstraintDiagnosticScenario(explicitSource, inferredSource,
                   $"multiple constraints (class, IMarker) on {methodName}<T>(T) violated by int");
    }

    /// <summary>
    /// Generates a scenario where a method has a base class constraint on T,
    /// but a type that does not derive from the base class is used.
    /// We define a base class Animal and pass int which does not derive from it.
    /// </summary>
    private static Gen<ConstraintDiagnosticScenario> GenBaseClassConstraintViolation()
    {
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let explicitSource = $$"""
                   class Animal {
                       public string Name;
                       public Animal(string name) {
                           this.Name = name;
                       }
                   }
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : Animal {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<int>(42);
                       }
                   }
                   """
               let inferredSource = $$"""
                   class Animal {
                       public string Name;
                       public Animal(string name) {
                           this.Name = name;
                       }
                   }
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) where T : Animal {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}(42);
                       }
                   }
                   """
               select new ConstraintDiagnosticScenario(explicitSource, inferredSource,
                   $"base class constraint Animal on {methodName}<T>(T) violated by int");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ConstraintDiagnosticScenario> GenScenario()
    {
        return Gen.OneOf(
            GenStructConstraintViolation(),
            GenClassConstraintViolation(),
            GenAbstractConstraintViolation(),
            GenMultipleConstraintViolation(),
            GenBaseClassConstraintViolation());
    }

    public static Arbitrary<ConstraintDiagnosticScenario> ArbitraryConstraintDiagnosticScenario() =>
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
    /// Property 9: Constraint satisfaction uses same diagnostics as explicit
    ///
    /// For any generic method with constrained type parameters, when inference
    /// produces a type argument that violates a constraint, the compiler SHALL
    /// emit the same diagnostic ID and message format as when an explicit type
    /// argument violates the same constraint.
    ///
    /// We compile two versions of each scenario: one with explicit type arguments
    /// and one with inferred type arguments. Both should produce the same set of
    /// constraint-related diagnostic IDs.
    ///
    /// **Validates: Requirements 4.4, 6.1, 6.2, 6.3, 6.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceConstraintPropertyTests) })]
    public Property ConstraintViolation_SameDiagnosticIds_ExplicitAndInferred(ConstraintDiagnosticScenario scenario)
    {
        var explicitResult = CompileSource(scenario.ExplicitSource);
        var inferredResult = CompileSource(scenario.InferredSource);

        // Extract constraint-related diagnostics from both compilations.
        // This includes both the specific constraint IDs and MIST_TC_0002 which
        // is emitted when the argument type is not assignable to the constrained
        // parameter type.
        var explicitConstraintDiags = explicitResult.Diagnostics
            .Where(d => ConstraintRelatedDiagnosticIds.Contains(d.Id.Value))
            .Select(d => d.Id.Value)
            .OrderBy(id => id)
            .ToList();

        var inferredConstraintDiags = inferredResult.Diagnostics
            .Where(d => ConstraintRelatedDiagnosticIds.Contains(d.Id.Value))
            .Select(d => d.Id.Value)
            .OrderBy(id => id)
            .ToList();

        // Both should have at least one constraint-related diagnostic.
        var explicitHasDiags = explicitConstraintDiags.Count > 0;
        var inferredHasDiags = inferredConstraintDiags.Count > 0;

        // The diagnostic IDs should be identical between explicit and inferred.
        var sameIds = explicitConstraintDiags.SequenceEqual(inferredConstraintDiags);

        return (explicitHasDiags && inferredHasDiags && sameIds).ToProperty()
            .Label($"Expected: same constraint-related diagnostic IDs for explicit and inferred type arguments.\n"
                 + $"  Explicit constraint diagnostics: [{string.Join(", ", explicitConstraintDiags)}]\n"
                 + $"  Inferred constraint diagnostics: [{string.Join(", ", inferredConstraintDiags)}]\n"
                 + $"  Explicit has diags: {explicitHasDiags}, Inferred has diags: {inferredHasDiags}\n"
                 + $"  All explicit diagnostics: [{string.Join(", ", explicitResult.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n"
                 + $"  All inferred diagnostics: [{string.Join(", ", inferredResult.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n"
                 + $"  Scenario: {scenario.Description}");
    }
}
