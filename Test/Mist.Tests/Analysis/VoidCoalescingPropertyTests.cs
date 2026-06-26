// Feature: optional-types, Property 11: Void-coalescing requires optional left operand
// Feature: optional-types, Property 12: Void-coalescing result type inference
// **Validates: Requirements 4.1, 4.2, 4.4**

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
/// Scenario for Property 11: a non-optional type used as the left operand of ??,
/// which should produce MIST_TC_0041.
/// </summary>
public sealed class VoidCoalescingNonOptionalScenario
{
    /// <summary>The Mist type name (e.g., "int", "string", "bool").</summary>
    public string TypeName { get; }

    /// <summary>A literal value for the type.</summary>
    public string LiteralValue { get; }

    /// <summary>A fallback literal value for the right operand.</summary>
    public string FallbackValue { get; }

    public VoidCoalescingNonOptionalScenario(string typeName, string literalValue, string fallbackValue)
    {
        TypeName = typeName;
        LiteralValue = literalValue;
        FallbackValue = fallbackValue;
    }

    public override string ToString()
    {
        return $"{TypeName} x = {LiteralValue}; x ?? {FallbackValue} → MIST_TC_0041";
    }
}

/// <summary>
/// Scenario for Property 12: an optional type used as the left operand of ??,
/// which should infer result type T (void removed from the union).
/// </summary>
public sealed class VoidCoalescingResultTypeScenario
{
    /// <summary>The Mist type name (e.g., "int", "string", "bool").</summary>
    public string TypeName { get; }

    /// <summary>A literal value for the optional variable.</summary>
    public string LiteralValue { get; }

    /// <summary>A fallback literal value for the right operand.</summary>
    public string FallbackValue { get; }

    public VoidCoalescingResultTypeScenario(string typeName, string literalValue, string fallbackValue)
    {
        TypeName = typeName;
        LiteralValue = literalValue;
        FallbackValue = fallbackValue;
    }

    public override string ToString()
    {
        return $"{TypeName}? x = {LiteralValue}; {TypeName} y = x ?? {FallbackValue} → no error";
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for void-coalescing operator (??) type checking.
///
/// Property 11: For any type T that does not contain void as a union constituent,
/// using T as the left operand of ?? SHALL produce diagnostic MIST_TC_0041.
///
/// Property 12: For any type T, when the left operand of ?? has type T | void and
/// the right operand has type T, the inferred result type SHALL be T (void removed
/// from the union), allowing assignment to a variable of type T without error.
///
/// **Validates: Requirements 4.1, 4.2, 4.4**
/// </summary>
public sealed class VoidCoalescingPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    /// <summary>
    /// Type definitions with their corresponding literal values and fallback values.
    /// Float uses the 'f' suffix; decimal uses the 'm' suffix.
    /// </summary>
    private static readonly (string TypeName, string LiteralValue, string FallbackValue)[] TypeLiterals =
    [
        ("int", "42", "0"),
        ("string", "\"hello\"", "\"default\""),
        ("bool", "true", "false"),
        ("float", "3.14f", "0.0f"),
        ("double", "2.718", "0.0"),
    ];

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios for Property 11: non-optional type as left operand of ??.
    /// </summary>
    public static Arbitrary<VoidCoalescingNonOptionalScenario> ArbitraryNonOptionalScenario()
    {
        var gen = Gen.Elements(TypeLiterals)
            .Select(t => new VoidCoalescingNonOptionalScenario(t.TypeName, t.LiteralValue, t.FallbackValue));
        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates scenarios for Property 12: optional type as left operand of ??,
    /// result type should be T.
    /// </summary>
    public static Arbitrary<VoidCoalescingResultTypeScenario> ArbitraryResultTypeScenario()
    {
        var gen = Gen.Elements(TypeLiterals)
            .Select(t => new VoidCoalescingResultTypeScenario(t.TypeName, t.LiteralValue, t.FallbackValue));
        return gen.ToArbitrary();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_vc_pbt_" + Guid.NewGuid().ToString("N"));
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

    // ─── Property 11: Void-coalescing requires optional left operand ────

    /// <summary>
    /// Property 11: For any type T that does not contain void as a union constituent,
    /// using T as the left operand of ?? SHALL produce diagnostic MIST_TC_0041.
    ///
    /// The test declares a non-optional variable and applies ?? to it, expecting
    /// the type checker to reject it with MIST_TC_0041.
    ///
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(VoidCoalescingPropertyTests) })]
    public bool NonOptionalLeftOperand_ProducesMIST_TC_0041(VoidCoalescingNonOptionalScenario scenario)
    {
        var source = $$"""
            class Program
            {
                public static void Main()
                {
                    {{scenario.TypeName}} x = {{scenario.LiteralValue}};
                    {{scenario.TypeName}} y = x ?? {{scenario.FallbackValue}};
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id.Value == "MIST_TC_0041")
            .ToList();

        return errors.Count > 0;
    }

    // ─── Property 12: Void-coalescing result type inference ─────────────

    /// <summary>
    /// Property 12: For any type T, when the left operand of ?? has type T | void
    /// and the right operand has type T, the inferred result type SHALL be T
    /// (void removed from the union).
    ///
    /// The test declares an optional variable (T?), applies ?? with a fallback of
    /// type T, and assigns the result to a non-optional variable of type T. If the
    /// result type is correctly inferred as T, no type errors should occur.
    ///
    /// **Validates: Requirements 4.2, 4.4**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(VoidCoalescingPropertyTests) })]
    public Property OptionalLeftOperand_ResultTypeIsT(VoidCoalescingResultTypeScenario scenario)
    {
        var source = $$"""
            class Program
            {
                public static void Main()
                {
                    {{scenario.TypeName}}? x = {{scenario.LiteralValue}};
                    {{scenario.TypeName}} y = x ?? {{scenario.FallbackValue}};
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        return (!errors.Any()).Label(
            errors.Any()
                ? $"Unexpected errors for {scenario.TypeName}: {string.Join("; ", errors.Select(e => $"{e.Id.Value}: {e.Message}"))}"
                : "No errors — result type correctly inferred as T");
    }
}