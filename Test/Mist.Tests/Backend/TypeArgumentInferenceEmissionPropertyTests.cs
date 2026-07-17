// Feature: type-argument-inference, Property 7: Inferred calls emit explicit type arguments in C#
// **Validates: Requirements 1.3, 7.1, 7.2, 7.4**
//
// For any generic method call where inference succeeds, the emitted C# code SHALL
// contain explicit type arguments in angle-bracket syntax matching the inferred types
// in declaration order. The emitted C# SHALL be identical to what would be emitted if
// the Mist source had provided those type arguments explicitly.
//
// These tests compile Mist programs in-process via CompilerDriver (EmitToDisk: false)
// and verify that the emitted C# output contains the expected explicit type arguments.

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

namespace Mist.Tests.Backend;

/// <summary>
/// Represents a scenario where a generic method is called without explicit type arguments
/// (inference succeeds), and we verify the emitted C# contains the expected explicit
/// type arguments.
/// </summary>
public sealed class InferredEmissionScenario
{
    /// <summary>The complete Mist source code for the scenario.</summary>
    public string Source { get; }

    /// <summary>Human-readable description for test output.</summary>
    public string Description { get; }

    /// <summary>
    /// The expected type argument string that should appear in the emitted C# method call.
    /// For example, "&lt;int&gt;" or "&lt;int, string&gt;".
    /// </summary>
    public string ExpectedTypeArgs { get; }

    /// <summary>The method name being called (used to locate the call in emitted C#).</summary>
    public string MethodName { get; }

    public InferredEmissionScenario(string source, string description, string expectedTypeArgs, string methodName)
    {
        Source = source;
        Description = description;
        ExpectedTypeArgs = expectedTypeArgs;
        MethodName = methodName;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// Property-based tests verifying that inferred type arguments are emitted as explicit
/// type arguments in the generated C# code.
///
/// Property 7: Inferred calls emit explicit type arguments in C#
///
/// For any generic method call where inference succeeds, the emitted C# code SHALL
/// contain explicit type arguments in angle-bracket syntax matching the inferred types
/// in declaration order. The emitted C# SHALL be identical to what would be emitted if
/// the Mist source had provided those type arguments explicitly.
///
/// **Validates: Requirements 1.3, 7.1, 7.2, 7.4**
/// </summary>
public sealed class TypeArgumentInferenceEmissionPropertyTests : IDisposable
{
    private readonly string outputDir;

    public TypeArgumentInferenceEmissionPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistEmissionPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
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

    // ─── Type and method name pools ──────────────────────────────────────

    private static readonly string[] TypeNames = ["int", "string", "bool"];
    private static readonly string[] MethodNames = ["Process", "Transform", "Convert", "Apply"];
    private static readonly string[] ParamNames = ["a", "b", "c", "x"];

    /// <summary>
    /// Returns a default literal value for the given Mist type name.
    /// </summary>
    private static string DefaultValueFor(string typeName)
    {
        return typeName switch
        {
            "int" => "42",
            "string" => "\"hello\"",
            "bool" => "true",
            _ => "0",
        };
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a single type parameter inferred from a direct argument.
    /// Example: T Process&lt;T&gt;(T a) called as Process(42) → emitted as Process&lt;int&gt;(42).
    /// Validates Requirements 1.3, 7.1.
    /// </summary>
    private static Gen<InferredEmissionScenario> GenSingleTypeParamDirect()
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
                           var result = Helpers.{{methodName}}({{value}});
                       }
                   }
                   """
               select new InferredEmissionScenario(source,
                   $"Single type param: Helpers.{methodName}({value}) → <{typeName}>",
                   $"<{typeName}>",
                   methodName);
    }

    /// <summary>
    /// Generates a scenario with multiple type parameters inferred from different arguments.
    /// Example: string Process&lt;A, B&gt;(A first, B second) called as Process(42, "hello")
    /// → emitted as Process&lt;int, string&gt;(42, "hello").
    /// Validates Requirements 7.1, 7.2.
    /// </summary>
    private static Gen<InferredEmissionScenario> GenMultiTypeParamDirect()
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
                           var result = Helpers.{{methodName}}({{value1}}, {{value2}});
                       }
                   }
                   """
               select new InferredEmissionScenario(source,
                   $"Multi type param: Helpers.{methodName}({value1}, {value2}) → <{type1}, {type2}>",
                   $"<{type1}, {type2}>",
                   methodName);
    }

    /// <summary>
    /// Generates a scenario with inference from a constructed type argument (Box&lt;T&gt;).
    /// Uses a user-defined generic class to avoid stdlib dependency.
    /// Example: T Process&lt;T&gt;(Box&lt;T&gt; items) called with Box&lt;int&gt;
    /// → emitted as Process&lt;int&gt;(items).
    /// Validates Requirements 7.1, 7.4.
    /// </summary>
    private static Gen<InferredEmissionScenario> GenConstructedTypeArg()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               let value = DefaultValueFor(typeName)
               let source = $$"""
                   public class Box<T> {
                       public T Value;
                       public Box(T v) {
                           Value = v;
                       }
                   }
                   public class Helpers {
                       public static T {{methodName}}<T>(Box<T> container) {
                           return container.Value;
                       }
                   }
                   class Program {
                       public static void Main() {
                           var box = new Box<{{typeName}}>({{value}});
                           var result = Helpers.{{methodName}}(box);
                       }
                   }
                   """
               select new InferredEmissionScenario(source,
                   $"Constructed type: Helpers.{methodName}(Box<{typeName}>) → <{typeName}>",
                   $"<{typeName}>",
                   methodName);
    }

    /// <summary>
    /// Generates a scenario with inference on an instance method.
    /// Example: container.Process(42) → emitted as container.Process&lt;int&gt;(42).
    /// Validates Requirements 1.3, 7.1.
    /// </summary>
    private static Gen<InferredEmissionScenario> GenInstanceMethodInference()
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
                           var result = c.{{methodName}}({{value}});
                       }
                   }
                   """
               select new InferredEmissionScenario(source,
                   $"Instance method: c.{methodName}({value}) → <{typeName}>",
                   $"<{typeName}>",
                   methodName);
    }

    /// <summary>
    /// Generates a scenario where the same type parameter appears in multiple positions
    /// with the same type (consistent binding). Verifies the emitted type arg is singular.
    /// Validates Requirements 7.1, 7.2.
    /// </summary>
    private static Gen<InferredEmissionScenario> GenSameTypeParamMultiplePositions()
    {
        return from typeName in Gen.Elements(TypeNames)
               from methodName in Gen.Elements(MethodNames)
               let value = DefaultValueFor(typeName)
               let source = $$"""
                   class Helpers {
                       public static T {{methodName}}<T>(T first, T second) {
                           return first;
                       }
                   }
                   class Program {
                       public static void Main() {
                           var result = Helpers.{{methodName}}({{value}}, {{value}});
                       }
                   }
                   """
               select new InferredEmissionScenario(source,
                   $"Same type param in multiple positions: Helpers.{methodName}({value}, {value}) → <{typeName}>",
                   $"<{typeName}>",
                   methodName);
    }

    // ─── Combined generator ──────────────────────────────────────────────

    private static Gen<InferredEmissionScenario> GenScenario()
    {
        return Gen.OneOf(
            GenSingleTypeParamDirect(),
            GenMultiTypeParamDirect(),
            GenConstructedTypeArg(),
            GenInstanceMethodInference(),
            GenSameTypeParamMultiplePositions());
    }

    public static Arbitrary<InferredEmissionScenario> ArbitraryInferredEmissionScenario()
    {
        return GenScenario().ToArbitrary();
    }

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

    /// <summary>
    /// Concatenates all emitted C# file contents from the compiler result.
    /// </summary>
    private static string GetAllEmittedCSharp(CompilerResult result)
    {
        if(result.EmittedFiles is null || result.EmittedFiles.Count == 0)
        {
            return "";
        }

        return string.Join("\n", result.EmittedFiles.Values);
    }

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 7: Inferred calls emit explicit type arguments in C#
    ///
    /// For any generic method call where inference succeeds, the emitted C# code SHALL
    /// contain explicit type arguments in angle-bracket syntax matching the inferred types
    /// in declaration order. The emitted C# SHALL be identical to what would be emitted if
    /// the Mist source had provided those type arguments explicitly.
    ///
    /// **Validates: Requirements 1.3, 7.1, 7.2, 7.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeArgumentInferenceEmissionPropertyTests) })]
    public Property InferredCalls_EmitExplicitTypeArguments_InCSharp(InferredEmissionScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // First, compilation must succeed (inference must work)
        if(!result.Succeeded)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error)
                .Select(d => $"{d.Id.Value}: {d.Message}");
            return false.ToProperty()
                .Label($"Compilation failed (inference or other error).\n"
                     + $"  Diagnostics: [{string.Join(", ", errors)}]\n"
                     + $"  Scenario: {scenario.Description}\n"
                     + $"  Source:\n{scenario.Source}");
        }

        // Get the emitted C# output
        var emittedCSharp = GetAllEmittedCSharp(result);

        // The emitted C# must contain the method call with explicit type arguments.
        // We look for: methodName<expectedTypeArgs>( pattern in the emitted output.
        var expectedPattern = $"{scenario.MethodName}{scenario.ExpectedTypeArgs}(";
        var containsExplicitTypeArgs = emittedCSharp.Contains(expectedPattern);

        return containsExplicitTypeArgs.ToProperty()
            .Label($"Expected emitted C# to contain '{expectedPattern}'.\n"
                 + $"  Scenario: {scenario.Description}\n"
                 + $"  Expected type args: {scenario.ExpectedTypeArgs}\n"
                 + $"  Emitted C#:\n{emittedCSharp}\n"
                 + $"  Source:\n{scenario.Source}");
    }
}