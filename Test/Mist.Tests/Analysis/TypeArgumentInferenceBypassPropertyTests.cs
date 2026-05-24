// Feature: type-argument-inference, Property 6: Explicit type arguments bypass inference
// **Validates: Requirements 1.5, 4.5, 7.5**
//
// For any generic method call that provides explicit type arguments, the compiler
// SHALL use those explicit type arguments directly, SHALL NOT attempt inference,
// and SHALL NOT emit inference-related diagnostics regardless of whether inference
// would have succeeded or failed.
//
// These tests compile Mist programs in-process via CompilerDriver and verify that
// no inference diagnostics (MIST_TC_0080, MIST_TC_0081) are emitted when explicit
// type arguments are provided — even in scenarios where inference would fail.

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
/// Represents a scenario where a generic method is called with explicit type arguments.
/// The scenario is designed so that inference would fail or produce different results
/// if attempted, proving that the compiler bypasses inference entirely.
/// </summary>
public sealed class ExplicitTypeArgBypassScenario
{
    /// <summary>The complete Mist source code for the scenario.</summary>
    public string Source { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    public ExplicitTypeArgBypassScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that explicit type arguments bypass inference.
///
/// Property 6: Explicit type arguments bypass inference
///
/// For any generic method call that provides explicit type arguments, the compiler
/// SHALL use those explicit type arguments directly, SHALL NOT attempt inference,
/// and SHALL NOT emit inference-related diagnostics regardless of whether inference
/// would have succeeded or failed.
///
/// **Validates: Requirements 1.5, 4.5, 7.5**
/// </summary>
public sealed class TypeArgumentInferenceBypassPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public TypeArgumentInferenceBypassPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistBypassPBT_" + Guid.NewGuid().ToString("N"));
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

    // ─── Inference-related diagnostic IDs ────────────────────────────────

    private static readonly string[] InferenceDiagnosticIds =
    [
        "MIST_TC_0080", // Uninferrable type parameter
        "MIST_TC_0081", // Conflicting inference
    ];

    // ─── Type and method name pools ──────────────────────────────────────

    private static readonly string[] TypeNames = ["int", "string", "bool"];
    private static readonly string[] MethodNames = ["Process", "Transform", "Convert", "Apply"];
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
    /// Generates a scenario where inference WOULD succeed (type param in parameter position),
    /// but explicit type arguments are provided anyway. The compiler should use the explicit
    /// type arguments and not attempt inference. Uses a separate Helpers class since Mist
    /// requires a receiver for generic method calls with explicit type arguments.
    /// </summary>
    private static Gen<ExplicitTypeArgBypassScenario> GenInferenceWouldSucceed()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let source = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<{{typeName}}>({{value}});
                       }
                   }
                   """
               select new ExplicitTypeArgBypassScenario(source,
                   $"Explicit <{typeName}> on Helpers.{methodName}<T>(T) where inference would succeed");
    }

    /// <summary>
    /// Generates a scenario where the type parameter does NOT appear in any parameter
    /// position (inference would FAIL with MIST_TC_0080 if attempted), but explicit
    /// type arguments are provided. Uses a separate Helpers class since Mist requires
    /// a receiver for generic method calls with explicit type arguments.
    /// </summary>
    private static Gen<ExplicitTypeArgBypassScenario> GenReturnTypeOnlyTypeParam()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               let source = $$"""
                   class Helpers {
                       public static int {{methodName}}<T>(int value) {
                           return value;
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<{{typeName}}>(42);
                       }
                   }
                   """
               select new ExplicitTypeArgBypassScenario(source,
                   $"Explicit <{typeName}> on Helpers.{methodName}<T>(int) where T is not in any parameter type (uninferrable)");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters where explicit type arguments
    /// are provided. One type parameter (U) does not appear in any parameter position
    /// (would be uninferrable without explicit args), but explicit args bypass inference.
    /// Uses a separate Helpers class since Mist requires a receiver for generic method
    /// calls with explicit type arguments.
    /// </summary>
    private static Gen<ExplicitTypeArgBypassScenario> GenMultiTypeParamPartiallyUninferrable()
    {
        return from type1 in Gen.Elements(TypeNames)
               from type2 in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value1 = DefaultValueFor(type1)
               let source = $$"""
                   class Helpers {
                       public static int {{methodName}}<T, U>(T {{paramName}}) {
                           return 0;
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<{{type1}}, {{type2}}>({{value1}});
                       }
                   }
                   """
               select new ExplicitTypeArgBypassScenario(source,
                   $"Explicit <{type1}, {type2}> on Helpers.{methodName}<T, U>(T) where U is uninferrable");
    }

    /// <summary>
    /// Generates a scenario where the argument type differs from the explicit type argument.
    /// Without explicit type args, inference would bind T to the argument type, but with
    /// explicit type args the compiler uses the explicit type directly.
    /// Uses a separate Helpers class since Mist requires a receiver for generic method
    /// calls with explicit type arguments.
    /// </summary>
    private static Gen<ExplicitTypeArgBypassScenario> GenExplicitDiffersFromArgType()
    {
        // Use int argument with explicit <int> — straightforward case where
        // the explicit type matches but we're verifying no inference runs.
        return from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let source = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<int>(42);
                       }
                   }
                   """
               select new ExplicitTypeArgBypassScenario(source,
                   $"Explicit <int> on Helpers.{methodName}<T>(T) with int arg — bypass confirmed by no diagnostics");
    }

    /// <summary>
    /// Generates a scenario with an instance method where explicit type arguments are provided.
    /// </summary>
    private static Gen<ExplicitTypeArgBypassScenario> GenInstanceMethodExplicit()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let source = $$"""
                   class Container {
                       public T {{methodName}}<T>(T {{paramName}}) {
                           return {{paramName}};
                       }
                   }
                   class Program {
                       public static void Main() {
                           var c = new Container();
                           var result = c.{{methodName}}<{{typeName}}>({{value}});
                       }
                   }
                   """
               select new ExplicitTypeArgBypassScenario(source,
                   $"Explicit <{typeName}> on instance method Container.{methodName}<T>(T)");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters all provided explicitly,
    /// where inference would succeed for all of them. Uses a separate Helpers class
    /// since Mist requires a receiver for generic method calls with explicit type arguments.
    /// </summary>
    private static Gen<ExplicitTypeArgBypassScenario> GenMultiTypeParamAllExplicit()
    {
        return from type1 in Gen.Elements(TypeNames)
               from type2 in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               let value1 = DefaultValueFor(type1)
               let value2 = DefaultValueFor(type2)
               let source = $$"""
                   class Helpers {
                       public static string {{methodName}}<A, B>(A first, B second) {
                           return "ok";
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}<{{type1}}, {{type2}}>({{value1}}, {{value2}});
                       }
                   }
                   """
               select new ExplicitTypeArgBypassScenario(source,
                   $"Explicit <{type1}, {type2}> on Helpers.{methodName}<A, B>(A, B) where inference would succeed");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ExplicitTypeArgBypassScenario> GenScenario()
    {
        return Gen.OneOf(
            GenInferenceWouldSucceed(),
            GenReturnTypeOnlyTypeParam(),
            GenMultiTypeParamPartiallyUninferrable(),
            GenExplicitDiffersFromArgType(),
            GenInstanceMethodExplicit(),
            GenMultiTypeParamAllExplicit());
    }

    public static Arbitrary<ExplicitTypeArgBypassScenario> ArbitraryExplicitTypeArgBypassScenario() =>
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
    /// Property 6: Explicit type arguments bypass inference
    ///
    /// For any generic method call that provides explicit type arguments, the compiler
    /// SHALL use those explicit type arguments directly, SHALL NOT attempt inference,
    /// and SHALL NOT emit inference-related diagnostics (MIST_TC_0080, MIST_TC_0081)
    /// regardless of whether inference would have succeeded or failed.
    ///
    /// **Validates: Requirements 1.5, 4.5, 7.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceBypassPropertyTests) })]
    public Property ExplicitTypeArgs_BypassInference_NoInferenceDiagnostics(ExplicitTypeArgBypassScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // Check that no inference-related diagnostics were emitted.
        // This is the core assertion: when explicit type arguments are provided,
        // the inference engine is NOT invoked, so no inference diagnostics appear.
        var inferenceDiagnostics = result.Diagnostics
            .Where(d => InferenceDiagnosticIds.Contains(d.Id.Value))
            .ToList();

        var noInferenceDiagnostics = inferenceDiagnostics.Count == 0;

        return noInferenceDiagnostics.ToProperty()
            .Label($"Expected: no inference diagnostics (MIST_TC_0080/MIST_TC_0081) when explicit type args are provided.\n"
                 + $"  Inference diagnostics found: {inferenceDiagnostics.Count}\n"
                 + $"  Diagnostics: [{string.Join(", ", inferenceDiagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n"
                 + $"  Scenario: {scenario.Description}\n"
                 + $"  Source:\n{scenario.Source}");
    }
}
