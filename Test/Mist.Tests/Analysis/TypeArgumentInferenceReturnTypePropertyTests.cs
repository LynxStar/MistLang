// Feature: type-argument-inference, Property 8: Return type substitution after inference
// **Validates: Requirements 8.1, 8.2, 8.3, 8.4**
//
// For any generic method call where inference succeeds and the method's return type
// mentions type parameters, the resolved return type of the call expression SHALL
// have all type parameters substituted with their inferred concrete types.
//
// These tests compile Mist programs in-process via CompilerDriver and verify that
// no type errors are emitted when the inferred return type is used in a context
// requiring the correct concrete type — proving substitution occurred correctly.

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
/// Represents a scenario where a generic method's return type mentions type parameters,
/// the method is called with inferred type arguments, and the result is used in a
/// context that requires the substituted concrete type.
/// </summary>
public sealed class ReturnTypeSubstitutionScenario
{
    /// <summary>The complete Mist source code for the scenario.</summary>
    public string Source { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    public ReturnTypeSubstitutionScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Property-based tests verifying that return types are correctly substituted
/// after type argument inference.
///
/// Property 8: Return type substitution after inference
///
/// For any generic method call where inference succeeds and the method's return type
/// mentions type parameters, the resolved return type of the call expression SHALL
/// have all type parameters substituted with their inferred concrete types.
///
/// **Validates: Requirements 8.1, 8.2, 8.3, 8.4**
/// </summary>
public sealed class TypeArgumentInferenceReturnTypePropertyTests : IDisposable
{
    private readonly string outputDir;

    public TypeArgumentInferenceReturnTypePropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistReturnTypePBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across all iterations) ────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Type pools ──────────────────────────────────────────────────────

    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];
    private static readonly string[] MethodNames = ["Identity", "Produce", "Extract", "Fetch"];
    private static readonly string[] ParamNames = ["value", "item", "input", "arg"];

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

    /// <summary>
    /// Returns a method name that accepts the given type as a parameter,
    /// used to verify the return type was correctly substituted.
    /// </summary>
    private static string ConsumerMethodFor(string typeName) => typeName switch
    {
        "int" => "ConsumeInt",
        "string" => "ConsumeString",
        "bool" => "ConsumeBool",
        _ => "ConsumeInt",
    };

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where a generic method returns T directly (TypeParameterType
    /// as return type), and the result is passed to a method expecting the concrete type.
    /// Example: T Identity&lt;T&gt;(T value) called with int, result passed to ConsumeInt(int).
    /// Validates Requirement 8.3.
    /// </summary>
    private static Gen<ReturnTypeSubstitutionScenario> GenDirectTypeParamReturn()
    {
        return from typeName in Gen.Elements(PrimitiveTypes)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let consumer = ConsumerMethodFor(typeName)
               let source = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) {
                           return {{paramName}};
                       }
                       public static void {{consumer}}({{typeName}} x) {
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}({{value}});
                           Helpers.{{consumer}}(result);
                       }
                   }
                   """
               select new ReturnTypeSubstitutionScenario(source,
                   $"T {methodName}<T>(T) returns {typeName}, passed to {consumer}({typeName})");
    }

    /// <summary>
    /// Generates a scenario where the return type is used in a variable assignment
    /// and then the variable is used as an argument to a typed method.
    /// This validates Requirement 8.2 (downstream expression usage).
    /// </summary>
    private static Gen<ReturnTypeSubstitutionScenario> GenReturnTypeInAssignment()
    {
        return from typeName in Gen.Elements(PrimitiveTypes)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let consumer = ConsumerMethodFor(typeName)
               let source = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) {
                           return {{paramName}};
                       }
                       public static void {{consumer}}({{typeName}} x) {
                       }
                   }
                   class Program {
                       public static void Main() {
                           var x = Helpers.{{methodName}}({{value}});
                           var y = x;
                           Helpers.{{consumer}}(y);
                       }
                   }
                   """
               select new ReturnTypeSubstitutionScenario(source,
                   $"T {methodName}<T>(T) assigned to var, re-assigned, then passed to {consumer}({typeName})");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters where the return type
    /// uses one of them. Example: A Pair&lt;A, B&gt;(A first, B second) returns A,
    /// and the result is passed to a method expecting the first type.
    /// Validates Requirements 8.1, 8.3.
    /// </summary>
    private static Gen<ReturnTypeSubstitutionScenario> GenMultiTypeParamReturnFirst()
    {
        return from type1 in Gen.Elements(PrimitiveTypes)
               from type2 in Gen.Elements(PrimitiveTypes)
               from methodName in Gen.Elements(MethodNames)
               let value1 = DefaultValueFor(type1)
               let value2 = DefaultValueFor(type2)
               let consumer = ConsumerMethodFor(type1)
               let source = $$"""
                   class Helpers {
                       public static A {{methodName}}<A, B>(A first, B second) {
                           return first;
                       }
                       public static void {{consumer}}({{type1}} x) {
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}({{value1}}, {{value2}});
                           Helpers.{{consumer}}(result);
                       }
                   }
                   """
               select new ReturnTypeSubstitutionScenario(source,
                   $"A {methodName}<A, B>(A, B) returns {type1} (first param type), passed to {consumer}({type1})");
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters where the return type
    /// uses the second type parameter. Example: B SelectSecond&lt;A, B&gt;(A first, B second)
    /// returns B, and the result is passed to a method expecting the second type.
    /// Validates Requirements 8.1, 8.3.
    /// </summary>
    private static Gen<ReturnTypeSubstitutionScenario> GenMultiTypeParamReturnSecond()
    {
        return from type1 in Gen.Elements(PrimitiveTypes)
               from type2 in Gen.Elements(PrimitiveTypes)
               from methodName in Gen.Elements(MethodNames)
               let value1 = DefaultValueFor(type1)
               let value2 = DefaultValueFor(type2)
               let consumer = ConsumerMethodFor(type2)
               let source = $$"""
                   class Helpers {
                       public static B {{methodName}}<A, B>(A first, B second) {
                           return second;
                       }
                       public static void {{consumer}}({{type2}} x) {
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}({{value1}}, {{value2}});
                           Helpers.{{consumer}}(result);
                       }
                   }
                   """
               select new ReturnTypeSubstitutionScenario(source,
                   $"B {methodName}<A, B>(A, B) returns {type2} (second param type), passed to {consumer}({type2})");
    }

    /// <summary>
    /// Generates a scenario where the return type is used as an argument to another
    /// generic method call (chained inference). Example: result of Identity(42) passed
    /// directly to ConsumeInt(int).
    /// Validates Requirement 8.2 (argument to another method call).
    /// </summary>
    private static Gen<ReturnTypeSubstitutionScenario> GenReturnTypeAsArgToAnotherCall()
    {
        return from typeName in Gen.Elements(PrimitiveTypes)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let consumer = ConsumerMethodFor(typeName)
               let source = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T {{paramName}}) {
                           return {{paramName}};
                       }
                       public static void {{consumer}}({{typeName}} x) {
                       }
                   }
                   class Program {
                       public static void Main() {
                           Helpers.{{consumer}}(Helpers.{{methodName}}({{value}}));
                       }
                   }
                   """
               select new ReturnTypeSubstitutionScenario(source,
                   $"T {methodName}<T>(T) result passed directly as arg to {consumer}({typeName})");
    }

    /// <summary>
    /// Generates a scenario where the generic method is an instance method and
    /// the return type is a type parameter. Validates Requirement 8.1 with instance methods.
    /// </summary>
    private static Gen<ReturnTypeSubstitutionScenario> GenInstanceMethodReturnType()
    {
        return from typeName in Gen.Elements(PrimitiveTypes)
               from methodName in Gen.Elements(MethodNames)
               from paramName in Gen.Elements(ParamNames)
               let value = DefaultValueFor(typeName)
               let consumer = ConsumerMethodFor(typeName)
               let source = $$"""
                   class Container {
                       public T {{methodName}}<T>(T {{paramName}}) {
                           return {{paramName}};
                       }
                   }
                   class Helpers {
                       public static void {{consumer}}({{typeName}} x) {
                       }
                   }
                   class Program {
                       public static void Main() {
                           var c = new Container();
                           var result = c.{{methodName}}({{value}});
                           Helpers.{{consumer}}(result);
                       }
                   }
                   """
               select new ReturnTypeSubstitutionScenario(source,
                   $"Instance method T {methodName}<T>(T) on Container returns {typeName}, passed to {consumer}({typeName})");
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<ReturnTypeSubstitutionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenDirectTypeParamReturn(),
            GenReturnTypeInAssignment(),
            GenMultiTypeParamReturnFirst(),
            GenMultiTypeParamReturnSecond(),
            GenReturnTypeAsArgToAnotherCall(),
            GenInstanceMethodReturnType());
    }

    public static Arbitrary<ReturnTypeSubstitutionScenario> ArbitraryReturnTypeSubstitutionScenario() =>
        GenScenario().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProgram",
            NamespaceRoot: "TestProgram",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 8: Return type substitution after inference
    ///
    /// For any generic method call where inference succeeds and the method's return type
    /// mentions type parameters, the resolved return type of the call expression SHALL
    /// have all type parameters substituted with their inferred concrete types.
    ///
    /// We verify this by compiling programs where the inferred return type is used in a
    /// context requiring the correct concrete type (passed to a method with a typed
    /// parameter). If substitution fails, the type checker would emit a type mismatch error.
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3, 8.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceReturnTypePropertyTests) })]
    public Property InferredReturnType_IsCorrectlySubstituted_NoTypeErrors(ReturnTypeSubstitutionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // The program should compile without any type errors.
        // If return type substitution failed, the type checker would emit a type mismatch
        // diagnostic when the result is passed to a method expecting the concrete type.
        var typeErrors = result.Diagnostics
            .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
            .ToList();

        var noTypeErrors = typeErrors.Count == 0;

        return noTypeErrors.ToProperty()
            .Label($"Expected: zero type errors (return type correctly substituted after inference).\n"
                 + $"  Type errors found: {typeErrors.Count}\n"
                 + $"  Diagnostics: [{string.Join(", ", typeErrors.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n"
                 + $"  Scenario: {scenario.Description}\n"
                 + $"  Source:\n{scenario.Source}");
    }
}