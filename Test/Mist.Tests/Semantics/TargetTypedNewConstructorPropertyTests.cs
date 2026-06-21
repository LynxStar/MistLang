// Feature: target-typed-new, Property 6: Constructor validation
// **Validates: Requirements 4.1, 4.2**

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
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// A test scenario for Property 6: Constructor validation.
/// Contains the generated source, the constructor arity of the type, the argument count
/// used in the target-typed new expression, and whether the combination should compile.
/// </summary>
#pragma warning disable CA1034
public sealed class ConstructorValidationScenario
{
    public string Source { get; }
    public int ConstructorArity { get; }
    public int ArgumentCount { get; }
    public bool ShouldSucceed { get; }
    public string Description { get; }

    public ConstructorValidationScenario(
        string source, int constructorArity, int argumentCount, bool shouldSucceed, string description)
    {
        Source = source;
        ConstructorArity = constructorArity;
        ArgumentCount = argumentCount;
        ShouldSucceed = shouldSucceed;
        Description = description;
    }

    public override string ToString() => Description;
}
#pragma warning restore CA1034

// ─── Generators ─────────────────────────────────────────────────────────────

/// <summary>
/// Generators for constructor validation scenarios.
/// Produces types with constructor arities 0-4 and target-typed new expressions
/// with argument counts 0-4.
/// </summary>
public static class ConstructorValidationGenerators
{
    private static readonly string[] ClassNames = ["Widget", "Gadget", "Doohickey", "Thingamajig", "Gizmo"];

    public static Gen<ConstructorValidationScenario> GenScenario()
    {
        return from classIdx in Gen.Choose(0, ClassNames.Length - 1)
               from ctorArity in Gen.Choose(0, 4)
               from argCount in Gen.Choose(0, 4)
               let className = ClassNames[classIdx]
               let shouldSucceed = ctorArity == argCount
               let source = BuildSource(className, ctorArity, argCount)
               select new ConstructorValidationScenario(
                   source, ctorArity, argCount, shouldSucceed,
                   $"{className}(arity={ctorArity}), new({FormatArgs(argCount)}) => {(shouldSucceed ? "OK" : "Error")}");
    }

    private static string BuildSource(string className, int ctorArity, int argCount)
    {
        var ctorDecl = BuildCtorDeclaration(className, ctorArity);
        var args = FormatArgs(argCount);

        return $@"class {className}
{{
{ctorDecl}}}

class Program
{{
    void Run()
    {{
        {className} x = new({args});
    }}
}}";
    }

    private static string BuildCtorDeclaration(string className, int arity)
    {
        if (arity == 0) return "";

        var parameters = string.Join(", ", Enumerable.Range(0, arity).Select(i => $"int p{i}"));
        return $"    public {className}({parameters}) {{ }}\n";
    }

    private static string FormatArgs(int count)
    {
        if (count == 0) return "";
        return string.Join(", ", Enumerable.Range(0, count).Select(i => (i + 1).ToString()));
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for constructor validation with target-typed new expressions.
///
/// Property 6: For any resolved target type T and argument list, the type checker SHALL
/// accept the target-typed new expression if and only if T has an accessible constructor
/// whose parameter types are compatible with the provided arguments (same validation as
/// fully-typed new T(args)).
///
/// Uses in-process compilation via CompilerDriver to inspect diagnostics. Zero process spawns.
///
/// **Validates: Requirements 4.1, 4.2**
/// </summary>
public sealed class TargetTypedNewConstructorPropertyTests : IDisposable
{
    private readonly string _outputDir;

    public TargetTypedNewConstructorPropertyTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "MistTargetNewCtor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir))
        {
            try { Directory.Delete(_outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver ──────────────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Arbitrary registration ─────────────────────────────────────────

    public static Arbitrary<ConstructorValidationScenario> ArbitraryScenario() =>
        ConstructorValidationGenerators.GenScenario().ToArbitrary();

    // ─── Helpers ────────────────────────────────────────────────────────

    private CompilerResult Compile(string source)
    {
        var filePath = Path.Combine(_outputDir, "Test_" + Guid.NewGuid().ToString("N") + ".mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _outputDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _outputDir,
            BinDirectory: Path.Combine(_outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    /// <summary>
    /// Diagnostic IDs that indicate constructor-related errors.
    /// </summary>
    private static readonly HashSet<string> ConstructorErrorIds = new()
    {
        "MIST_TC_0011", // Type does not contain a constructor matching the given arguments
        "MIST_TC_0031", // No overload for constructor matches argument types
    };

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Constructor validation
    //
    // For any resolved target type T and argument list, the type checker SHALL
    // accept the target-typed new expression if and only if T has an accessible
    // constructor whose parameter types are compatible with the provided arguments.
    //
    // **Validates: Requirements 4.1, 4.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TargetTypedNewConstructorPropertyTests) })]
    public Property TargetTypedNew_AcceptsIffConstructorMatches(ConstructorValidationScenario scenario)
    {
        var result = Compile(scenario.Source);

        // Filter to constructor-related errors only (ignore entry point warnings, etc.)
        var constructorErrors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Where(d => ConstructorErrorIds.Contains(d.Id.Value))
            .ToList();

        var hasConstructorError = constructorErrors.Count > 0;

        if (scenario.ShouldSucceed)
        {
            // Arity matches: no constructor-related errors should exist
            return (!hasConstructorError)
                .ToProperty()
                .Label($"Expected success (arity match: ctor={scenario.ConstructorArity}, args={scenario.ArgumentCount}) " +
                       $"but got constructor errors:\n" +
                       string.Join("\n", constructorErrors.Select(e => $"  {e.Id.Value}: {e.Message}")) +
                       $"\nSource:\n{scenario.Source}");
        }
        else
        {
            // Arity mismatch: should have a constructor-related error
            return hasConstructorError
                .ToProperty()
                .Label($"Expected constructor error (arity mismatch: ctor={scenario.ConstructorArity}, args={scenario.ArgumentCount}) " +
                       $"but no constructor-related error was emitted.\n" +
                       $"All diagnostics: [{string.Join(", ", result.Diagnostics.Select(d => d.Id.Value + ": " + d.Message))}]\n" +
                       $"Source:\n{scenario.Source}");
        }
    }
}
