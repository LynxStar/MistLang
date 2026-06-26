// Feature: dependent-type-refinement-constraints, Property 9: Constructor Resolution Equivalence
// **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**

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
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// The category of Span constructor invocation being tested.
/// </summary>
public enum SpanConstructorCategory
{
    /// <summary>Valid: <c>new Span&lt;T&gt;(arrayExpr)</c> where <c>arrayExpr</c> is an array of compatible element type.</summary>
    ValidArrayArgument,
    /// <summary>Invalid: <c>new Span&lt;T&gt;(nonArrayExpr)</c> with wrong argument type.</summary>
    WrongArgumentType,
    /// <summary>Invalid: <c>new Span&lt;T&gt;()</c> with too few arguments.</summary>
    TooFewArguments,
    /// <summary>Invalid: <c>new Span&lt;T&gt;(a, b)</c> with too many arguments.</summary>
    TooManyArguments
}

/// <summary>
/// Describes a Span constructor resolution scenario for property testing.
/// </summary>
public sealed record SpanConstructorScenario(
    string Source,
    SpanConstructorCategory Category,
    string ElementType,
    bool ExpectsError,
    string Description)
{
    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for Span constructor resolution equivalence
/// (Property 9: Constructor Resolution Equivalence).
///
/// Verifies that constructor resolution for <c>new Span&lt;T&gt;(expr)</c> via
/// member-scope lookup produces the same accept/reject behavior and diagnostic
/// messages as the previous hardcoded branch:
/// <list type="bullet">
///   <item>Accepted: <c>new Span&lt;T&gt;(arrayExpr)</c> where <c>arrayExpr</c> is an array of compatible element type</item>
///   <item>Rejected: <c>new Span&lt;T&gt;(nonArrayExpr)</c> with MIST_TC_0011 (wrong argument type)</item>
///   <item>Rejected: <c>new Span&lt;T&gt;()</c> (too few arguments) with MIST_TC_0011</item>
///   <item>Rejected: <c>new Span&lt;T&gt;(a, b)</c> (too many arguments) with MIST_TC_0011</item>
/// </list>
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// <b>Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5</b>
/// </summary>
public sealed class SpanConstructorResolutionPropertyTests : IDisposable
{
    private readonly string tempDir;

    public SpanConstructorResolutionPropertyTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "MistSpanCtorPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Element type pool ───────────────────────────────────────────────

    /// <summary>
    /// Stampable primitive types that can be used as Span element types.
    /// These types support <c>new T[n] { |i| expr }</c> array construction.
    /// </summary>
    private static readonly string[] StampableElementTypes =
        ["int", "uint", "long", "byte", "short", "float", "double", "bool", "char"];

    /// <summary>
    /// Types that are NOT arrays — used as wrong-argument-type sources.
    /// These produce values that cannot be passed to <c>new Span&lt;T&gt;(...)</c>.
    /// </summary>
    private static readonly string[] NonArrayExpressions =
        ["\"hello\"", "42", "true", "3.14"];

    /// <summary>
    /// The non-array type display names corresponding to <see cref="NonArrayExpressions"/>.
    /// Used for labeling test output.
    /// </summary>
    private static readonly string[] NonArrayTypeNames =
        ["string", "int", "bool", "double"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid scenario: <c>new Span&lt;T&gt;(arr)</c> where <c>arr</c> is
    /// an array of compatible element type.
    /// Expected: no MIST_TC_0011 diagnostic.
    /// </summary>
    private static Gen<SpanConstructorScenario> GenValidArrayArgument()
    {
        return from typeIdx in Gen.Choose(0, StampableElementTypes.Length - 1)
               from arraySize in Gen.Choose(1, 20)
               let elementType = StampableElementTypes[typeIdx]
               let source = WrapInProgram(elementType, arraySize)
               select new SpanConstructorScenario(
                   source,
                   SpanConstructorCategory.ValidArrayArgument,
                   elementType,
                   ExpectsError: false,
                   $"new Span<{elementType}>(new {elementType}[{arraySize}]) → accepted");
    }

    /// <summary>
    /// Generates a scenario with wrong argument type: <c>new Span&lt;int&gt;(nonArrayExpr)</c>.
    /// Expected: MIST_TC_0011 (no matching constructor).
    /// </summary>
    private static Gen<SpanConstructorScenario> GenWrongArgumentType()
    {
        return from exprIdx in Gen.Choose(0, NonArrayExpressions.Length - 1)
               let expr = NonArrayExpressions[exprIdx]
               let typeName = NonArrayTypeNames[exprIdx]
               let source = WrapInProgramRaw($"Span<int> s = new Span<int>({expr});")
               select new SpanConstructorScenario(
                   source,
                   SpanConstructorCategory.WrongArgumentType,
                   "int",
                   ExpectsError: true,
                   $"new Span<int>({expr}) [{typeName} arg] → MIST_TC_0011");
    }

    /// <summary>
    /// Generates a scenario with too few arguments: <c>new Span&lt;T&gt;()</c>.
    /// Expected: MIST_TC_0011 (no matching constructor).
    /// </summary>
    private static Gen<SpanConstructorScenario> GenTooFewArguments()
    {
        return from typeIdx in Gen.Choose(0, StampableElementTypes.Length - 1)
               let elementType = StampableElementTypes[typeIdx]
               let source = WrapInProgramRaw($"Span<{elementType}> s = new Span<{elementType}>();")
               select new SpanConstructorScenario(
                   source,
                   SpanConstructorCategory.TooFewArguments,
                   elementType,
                   ExpectsError: true,
                   $"new Span<{elementType}>() [0 args] → MIST_TC_0011");
    }

    /// <summary>
    /// Generates a scenario with too many arguments: <c>new Span&lt;T&gt;(arr, extra)</c>.
    /// Expected: MIST_TC_0011 (no matching constructor).
    /// </summary>
    private static Gen<SpanConstructorScenario> GenTooManyArguments()
    {
        return from typeIdx in Gen.Choose(0, StampableElementTypes.Length - 1)
               from arraySize in Gen.Choose(1, 10)
               let elementType = StampableElementTypes[typeIdx]
               let source = WrapInProgramRaw(
                   $"{elementType}[] arr = new {elementType}[{arraySize}];\n" +
                   $"            Span<{elementType}> s = new Span<{elementType}>(arr, arr);")
               select new SpanConstructorScenario(
                   source,
                   SpanConstructorCategory.TooManyArguments,
                   elementType,
                   ExpectsError: true,
                   $"new Span<{elementType}>(arr, arr) [2 args] → MIST_TC_0011");
    }

    /// <summary>
    /// Combined generator: all Span constructor scenario categories.
    /// </summary>
    public static Arbitrary<SpanConstructorScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenValidArrayArgument(),
            GenWrongArgumentType(),
            GenTooFewArguments(),
            GenTooManyArguments()).ToArbitrary();
    }

    // ─── Source builders ─────────────────────────────────────────────────

    /// <summary>
    /// Wraps a valid Span construction with an array in a complete program.
    /// Uses <c>using Standard;</c> and a separate namespace to match the
    /// compilation path that samples use.
    /// </summary>
    private static string WrapInProgram(string elementType, int arraySize)
    {
        return $$"""
            using Standard;

            namespace SpanTest;

            public class Program
            {
                public static void Main()
                {
                    {{elementType}}[] arr = new {{elementType}}[{{arraySize}}];
                    Span<{{elementType}}> s = new Span<{{elementType}}>(arr);
                }
            }
            """;
    }

    /// <summary>
    /// Wraps an arbitrary body statement in a complete program.
    /// Uses <c>using Standard;</c> and a separate namespace to match the
    /// compilation path that samples use.
    /// </summary>
    private static string WrapInProgramRaw(string body)
    {
        return $$"""
            using Standard;

            namespace SpanTest;

            public class Program
            {
                public static void Main()
                {
                    {{body}}
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private (ImmutableArray<Diagnostic> Diagnostics, string FilePath) CompileAndGetDiagnostics(string source)
    {
        var filePath = Path.Combine(tempDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        // Write a mist.toml to trigger full project-mode compilation,
        // matching the path that samples use (with global usings resolved from config).
        var tomlPath = Path.Combine(tempDir, "mist.toml");
        if(!File.Exists(tomlPath))
        {
            File.WriteAllText(tomlPath, """
                name = "SpanTest"

                [global_usings]
                namespaces = ["Standard"]
                """);
        }

        var options = new CompilerOptions(
            ProjectFolder: tempDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: tempDir,
            BinDirectory: Path.Combine(tempDir, "bin"),
            ProjectName: "SpanTest",
            NamespaceRoot: "SpanTest",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        var result = SharedDriver.Compile(options);
        return (result.Diagnostics, filePath);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Constructor Resolution Equivalence
    //
    // **Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5**
    //
    // For all `new Span<T>(expr)` constructions, the constructor resolution
    // via member-scope lookup produces the same accept/reject behavior and
    // diagnostic messages as the previous hardcoded branch:
    //   - Accepted: new Span<T>(arrayExpr) — no MIST_TC_0011
    //   - Rejected: new Span<T>(nonArrayExpr) — MIST_TC_0011
    //   - Rejected: new Span<T>() — MIST_TC_0011
    //   - Rejected: new Span<T>(a, b) — MIST_TC_0011
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 9: Span constructor resolution produces the correct accept/reject
    /// behavior. Valid array arguments are accepted (no MIST_TC_0011), while invalid
    /// constructions (wrong type, too few args, too many args) are rejected with
    /// MIST_TC_0011.
    ///
    /// <b>Validates: Requirements 6.1, 6.2, 6.3, 6.4, 6.5</b>
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(SpanConstructorResolutionPropertyTests) })]
    public Property SpanConstructor_AcceptRejectBehaviorMatchesHardcodedBranch(
        SpanConstructorScenario scenario)
    {
        var (diagnostics, filePath) = CompileAndGetDiagnostics(scenario.Source);

        // Filter to MIST_TC_0011 from the user file only.
        var constructorErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0011"
                        && d.CompilationUnitPath == filePath)
            .ToList();

        bool passed;
        if(scenario.ExpectsError)
        {
            // Invalid constructions must produce at least one MIST_TC_0011.
            passed = constructorErrors.Count >= 1;
        }
        else
        {
            // Valid constructions must not produce MIST_TC_0011.
            // Also check no user-file errors.
            var userErrors = diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error
                            && d.CompilationUnitPath == filePath)
                .ToList();

            passed = constructorErrors.Count == 0 && userErrors.Count == 0;
        }

        return passed.ToProperty()
            .Label(
                $"Scenario: {scenario}\n" +
                $"Category: {scenario.Category}\n" +
                $"ExpectsError: {scenario.ExpectsError}\n" +
                $"MIST_TC_0011 count: {constructorErrors.Count}\n" +
                $"MIST_TC_0011 messages: [{string.Join("; ", constructorErrors.Select(d => d.Message))}]\n" +
                $"All user errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.CompilationUnitPath == filePath).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Source:\n{scenario.Source}");
    }

    /// <summary>
    /// Property 9 (diagnostic content): When a Span constructor call is rejected,
    /// the MIST_TC_0011 message references the Span type name and indicates
    /// no matching constructor was found.
    ///
    /// <b>Validates: Requirements 6.3, 6.4</b>
    /// </summary>
    [Property(
        MaxTest = 60,
        Arbitrary = new[] { typeof(SpanConstructorResolutionPropertyTests) })]
    public Property SpanConstructor_RejectionDiagnosticMentionsSpanType(
        SpanConstructorScenario scenario)
    {
        if(!scenario.ExpectsError)
        {
            // Only test error scenarios for message content.
            return true.ToProperty().Label("Skipped: valid scenario");
        }

        var (diagnostics, filePath) = CompileAndGetDiagnostics(scenario.Source);

        var constructorErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0011"
                        && d.CompilationUnitPath == filePath)
            .ToList();

        if(constructorErrors.Count == 0)
        {
            return false.ToProperty()
                .Label(
                    $"Expected MIST_TC_0011 but none found.\n" +
                    $"Scenario: {scenario}\n" +
                    $"All user errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && d.CompilationUnitPath == filePath).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                    $"Source:\n{scenario.Source}");
        }

        // The diagnostic message should mention 'Span' or the constructed type name.
        var messageReferencesSpan = constructorErrors.Any(d =>
            d.Message.Contains("Span", StringComparison.OrdinalIgnoreCase));

        return messageReferencesSpan.ToProperty()
            .Label(
                $"MIST_TC_0011 message should reference 'Span'.\n" +
                $"Scenario: {scenario}\n" +
                $"Messages: [{string.Join("; ", constructorErrors.Select(d => d.Message))}]\n" +
                $"Source:\n{scenario.Source}");
    }

    // ─── Deterministic coverage tests ────────────────────────────────────

    /// <summary>
    /// Validates that <c>new Span&lt;int&gt;(arr)</c> with a valid int array is accepted
    /// without any MIST_TC_0011 diagnostics.
    /// </summary>
    [Fact]
    public void ValidSpanConstruction_IntArray_NoError()
    {
        var source = WrapInProgram("int", 5);
        var (diagnostics, filePath) = CompileAndGetDiagnostics(source);

        var constructorErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0011"
                        && d.CompilationUnitPath == filePath)
            .ToList();

        var userErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                        && d.CompilationUnitPath == filePath)
            .ToList();

        Assert.Empty(constructorErrors);
        Assert.Empty(userErrors);
    }

    /// <summary>
    /// Validates that <c>new Span&lt;int&gt;("hello")</c> is rejected with MIST_TC_0011.
    /// </summary>
    [Fact]
    public void InvalidSpanConstruction_StringArg_EmitsMistTc0011()
    {
        var source = WrapInProgramRaw("Span<int> s = new Span<int>(\"hello\");");
        var (diagnostics, filePath) = CompileAndGetDiagnostics(source);

        var constructorErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0011"
                        && d.CompilationUnitPath == filePath)
            .ToList();

        Assert.NotEmpty(constructorErrors);
        Assert.Contains(constructorErrors, d =>
            d.Message.Contains("Span", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validates that <c>new Span&lt;int&gt;()</c> (too few args) is rejected with MIST_TC_0011.
    /// </summary>
    [Fact]
    public void InvalidSpanConstruction_NoArgs_EmitsMistTc0011()
    {
        var source = WrapInProgramRaw("Span<int> s = new Span<int>();");
        var (diagnostics, filePath) = CompileAndGetDiagnostics(source);

        var constructorErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0011"
                        && d.CompilationUnitPath == filePath)
            .ToList();

        Assert.NotEmpty(constructorErrors);
    }

    /// <summary>
    /// Validates that <c>new Span&lt;int&gt;(arr, arr)</c> (too many args) is rejected with MIST_TC_0011.
    /// </summary>
    [Fact]
    public void InvalidSpanConstruction_TooManyArgs_EmitsMistTc0011()
    {
        var source = WrapInProgramRaw(
            "int[] arr = new int[3];\n" +
            "            Span<int> s = new Span<int>(arr, arr);");
        var (diagnostics, filePath) = CompileAndGetDiagnostics(source);

        var constructorErrors = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0011"
                        && d.CompilationUnitPath == filePath)
            .ToList();

        Assert.NotEmpty(constructorErrors);
    }
}