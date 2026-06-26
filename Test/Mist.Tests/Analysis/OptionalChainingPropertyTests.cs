// Feature: optional-types, Property 8: Optional chaining projects receiver and wraps result type
// Feature: optional-types, Property 9: Optional chaining requires optional type
// **Validates: Requirements 3.1, 3.3, 3.4, 3.7, 3.8**

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

namespace Mist.Tests.Analysis;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for Property 8: a class with a method returning type R, where
/// optional chaining on a receiver of type ClassName? should infer R | void.
/// </summary>
public sealed class OptionalChainingProjectionScenario
{
    /// <summary>The class name used as the receiver type.</summary>
    public string ClassName { get; }

    /// <summary>The method name on the class.</summary>
    public string MethodName { get; }

    /// <summary>The return type of the method (Mist type name).</summary>
    public string ReturnType { get; }

    public OptionalChainingProjectionScenario(string className, string methodName, string returnType)
    {
        ClassName = className;
        MethodName = methodName;
        ReturnType = returnType;
    }

    public override string ToString() => $"{ClassName}?.{MethodName}() → {ReturnType} | void";
}

/// <summary>
/// Scenario for Property 9: a non-optional type on which ?. is applied,
/// expecting a diagnostic error MIST_TC_0111.
/// </summary>
public sealed class OptionalChainingNonOptionalScenario
{
    /// <summary>The non-optional type expression used as the receiver.</summary>
    public string TypeName { get; }

    /// <summary>The member name being accessed via ?..</summary>
    public string MemberName { get; }

    /// <summary>Whether the type is a primitive or a class (affects source generation).</summary>
    public bool IsPrimitive { get; }

    public OptionalChainingNonOptionalScenario(string typeName, string memberName, bool isPrimitive)
    {
        TypeName = typeName;
        MemberName = memberName;
        IsPrimitive = isPrimitive;
    }

    public override string ToString() => $"{TypeName}?.{MemberName} → MIST_TC_0111";
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for optional chaining type checking.
///
/// Property 8: For any class type T with a method returning type R, applying ?.
/// on an expression of type T | void to call that method SHALL infer the result
/// type as R | void, and the member resolution SHALL succeed against type T.
///
/// Property 9: For any type T that does not contain void as a union constituent,
/// applying ?. to an expression of type T SHALL produce diagnostic MIST_TC_0111.
///
/// **Validates: Requirements 3.1, 3.3, 3.4, 3.7, 3.8**
/// </summary>
public sealed class OptionalChainingPropertyTests
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
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_opt_chain_pbt_" + Guid.NewGuid().ToString("N"));
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
                EmitToDisk: false,
                GlobalUsings: ImmutableArray.Create("Standard"));

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Known types for generation ─────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Widget", "Gadget", "Service", "Handler", "Processor"];

    private static readonly string[] MethodNames =
        ["GetValue", "Compute", "Fetch", "Process", "Calculate"];

    private static readonly string[] ReturnTypes =
        ["int", "string", "bool", "float", "double"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double"];

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios for Property 8: a class with a method returning a known type,
    /// where optional chaining should succeed and infer R | void.
    /// </summary>
    public static Arbitrary<OptionalChainingProjectionScenario> ArbitraryOptionalChainingProjectionScenario()
    {
        var gen =
            from className in Gen.Elements(ClassNames)
            from methodName in Gen.Elements(MethodNames)
            from returnType in Gen.Elements(ReturnTypes)
            select new OptionalChainingProjectionScenario(className, methodName, returnType);
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios for Property 9: a non-optional type with a member access via ?..
    /// </summary>
    public static Arbitrary<OptionalChainingNonOptionalScenario> ArbitraryOptionalChainingNonOptionalScenario()
    {
        // Primitive types — use a method that exists on the type (ToString)
        var primitiveGen = Gen.Elements(PrimitiveTypes)
            .Select(t => new OptionalChainingNonOptionalScenario(t, "ToString", isPrimitive: true));

        // Class types — use a method defined on the class
        var classGen =
            from className in Gen.Elements(ClassNames)
            from methodName in Gen.Elements(MethodNames)
            select new OptionalChainingNonOptionalScenario(className, methodName, isPrimitive: false);

        return Gen.OneOf(primitiveGen, classGen).ToArbitrary();
    }

    // ─── Property 8: Optional chaining projects receiver and wraps result ─

    /// <summary>
    /// Property 8: For any class type T with a method returning type R, applying ?.
    /// on an expression of type T | void to call that method SHALL infer the result
    /// type as R | void, and the member resolution SHALL succeed against type T.
    ///
    /// We verify this by:
    /// 1. Declaring a class with a method returning R
    /// 2. Declaring a variable of type ClassName? (i.e., ClassName | void)
    /// 3. Calling variable?.Method() and assigning to R | void (should produce no errors)
    ///
    /// **Validates: Requirements 3.1, 3.3, 3.7, 3.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OptionalChainingPropertyTests) })]
    public Property OptionalChaining_ProjectsReceiverAndWrapsResultType(
        OptionalChainingProjectionScenario scenario)
    {
        var source = $$"""
            using Standard;

            class {{scenario.ClassName}}
            {
                public {{scenario.ReturnType}} {{scenario.MethodName}}()
                {
                    return default;
                }
            }

            class Program
            {
                public static void Main()
                {
                    {{scenario.ClassName}}? receiver = new {{scenario.ClassName}}();
                    {{scenario.ReturnType}}? result = receiver?.{{scenario.MethodName}}();
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        return (!errors.Any()).ToProperty()
            .Label(errors.Any()
                ? $"Unexpected errors for {scenario}: {string.Join("; ", errors.Select(e => $"{e.Id.Value}: {e.Message}"))}"
                : "No errors — optional chaining correctly projects receiver and wraps result");
    }

    // ─── Property 9: Optional chaining requires optional type ────────────

    /// <summary>
    /// Property 9: For any type T that does not contain void as a union constituent,
    /// applying ?. to an expression of type T SHALL produce diagnostic MIST_TC_0111.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OptionalChainingPropertyTests) })]
    public Property OptionalChaining_OnNonOptionalType_ProducesDiagnostic(
        OptionalChainingNonOptionalScenario scenario)
    {
        string source;
        if(scenario.IsPrimitive)
        {
            // For primitive types, we use a local variable and call ToString via ?.
            source = $$"""
                class Program
                {
                    public static void Main()
                    {
                        {{scenario.TypeName}} x = default;
                        var result = x?.{{scenario.MemberName}}();
                    }
                }
                """;
        }
        else
        {
            // For class types, declare the class with the method and use a non-optional variable
            source = $$"""
                class {{scenario.TypeName}}
                {
                    public int {{scenario.MemberName}}()
                    {
                        return 0;
                    }
                }

                class Program
                {
                    public static void Main()
                    {
                        {{scenario.TypeName}} x = new {{scenario.TypeName}}();
                        var result = x?.{{scenario.MemberName}}();
                    }
                }
                """;
        }

        var diagnostics = CompileAndGetDiagnostics(source);

        var hasOptionalChainingError = diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value == "MIST_TC_0111");

        return hasOptionalChainingError.ToProperty()
            .Label($"Expected MIST_TC_0111 for '{scenario}' but it was not emitted")
            .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}