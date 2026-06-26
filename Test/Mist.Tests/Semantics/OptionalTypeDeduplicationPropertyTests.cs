// Feature: optional-types, Property 2: Void deduplication in optional types
// **Validates: Requirements 1.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// A scenario for testing void deduplication: a type that already contains void
/// has the optional suffix applied, and we verify the expected diagnostic behavior.
/// </summary>
public sealed class VoidDeduplicationScenario
{
    /// <summary>The Mist source code exercising the scenario.</summary>
    public string Source { get; }

    /// <summary>Whether we expect MIST_TC_0045 to be emitted.</summary>
    public bool ExpectError { get; }

    /// <summary>A human-readable label for the scenario.</summary>
    public string Label { get; }

    public VoidDeduplicationScenario(string source, bool expectError, string label)
    {
        Source = source;
        ExpectError = expectError;
        Label = label;
    }

    public override string ToString() => Label;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for void deduplication in optional types.
///
/// Property 2: For any union type U that already contains void as a constituent,
/// applying the `?` suffix SHALL produce a diagnostic error MIST_TC_0045 (the type
/// already contains void — no duplicate void is added).
///
/// Conversely, for any type T that does NOT already contain void, applying `?`
/// SHALL NOT produce MIST_TC_0045.
///
/// **Validates: Requirements 1.3**
/// </summary>
public sealed class OptionalTypeDeduplicationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mist_pbt_" + Guid.NewGuid().ToString("N"));
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

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Known types for generation ─────────────────────────────────────

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double"];

    private static readonly string[] NamedTypes =
        ["MyClass", "Foo", "Bar"];

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates scenarios where a union type already containing void has `?` applied.
    /// These should produce MIST_TC_0045.
    /// </summary>
    private static Gen<VoidDeduplicationScenario> GenAlreadyContainsVoid()
    {
        // Pattern: (T | void)? — the union already contains void, applying ? is an error
        // Note: The grammar only supports `?` on primitive and named type references,
        // not on parenthesized union types. So we use type aliases to test this.
        var primitiveAliasGen = Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $"type MaybeT = {t} | void;\n\nclass Program {{\n    public static void Main() {{\n        MaybeT? x = default;\n    }}\n}}";
            return new VoidDeduplicationScenario(source, expectError: true, $"type MaybeT = {t} | void; MaybeT?");
        });

        // Pattern: type alias that expands to T | U | void, then applying ? to it
        var multiAliasGen =
            from t1 in Gen.Elements(PrimitiveTypes)
            from t2 in Gen.Elements(PrimitiveTypes)
            where t1 != t2
            let source = $"type MultiMaybe = {t1} | {t2} | void;\n\nclass Program {{\n    public static void Main() {{\n        MultiMaybe? x = default;\n    }}\n}}"
            select new VoidDeduplicationScenario(source, expectError: true, $"type MultiMaybe = {t1} | {t2} | void; MultiMaybe?");

        return Gen.OneOf(primitiveAliasGen, multiAliasGen);
    }

    /// <summary>
    /// Generates scenarios where a type does NOT contain void and has `?` applied.
    /// These should NOT produce MIST_TC_0045.
    /// </summary>
    private static Gen<VoidDeduplicationScenario> GenDoesNotContainVoid()
    {
        // Pattern: T? where T is a simple primitive — normal optional
        var primitiveGen = Gen.Elements(PrimitiveTypes).Select(t =>
        {
            var source = $"class Program {{\n    public static void Main() {{\n        {t}? x = default;\n    }}\n}}";
            return new VoidDeduplicationScenario(source, expectError: false, $"{t}?");
        });

        // Pattern: (T | U)? where neither T nor U is void — valid optional on union
        var unionGen =
            from t1 in Gen.Elements(PrimitiveTypes)
            from t2 in Gen.Elements(PrimitiveTypes)
            where t1 != t2
            let source = $"class Program {{\n    public static void Main() {{\n        ({t1} | {t2})? x = default;\n    }}\n}}"
            select new VoidDeduplicationScenario(source, expectError: false, $"({t1} | {t2})?");

        // Pattern: NamedType? — named type without void
        var namedGen = Gen.Elements(NamedTypes).Select(t =>
        {
            var source = $"class {t} {{\n}}\n\nclass Program {{\n    public static void Main() {{\n        {t}? x = default;\n    }}\n}}";
            return new VoidDeduplicationScenario(source, expectError: false, $"{t}?");
        });

        return Gen.OneOf(primitiveGen, unionGen, namedGen);
    }

    public static Arbitrary<VoidDeduplicationScenario> ArbitraryVoidDeduplicationScenario()
    {
        var gen = Gen.Frequency(
            (3, GenAlreadyContainsVoid()),
            (2, GenDoesNotContainVoid()));

        return gen.ToArbitrary();
    }

    // ─── Property Tests ─────────────────────────────────────────────────

    /// <summary>
    /// Property 2: For any union type U that already contains void, applying `?`
    /// produces MIST_TC_0045. For any type T that does NOT contain void, applying
    /// `?` does NOT produce MIST_TC_0045.
    ///
    /// **Validates: Requirements 1.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OptionalTypeDeduplicationPropertyTests) })]
    public Property VoidDeduplication_ProducesCorrectDiagnostic(VoidDeduplicationScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var hasMistTc0045 = diagnostics.Any(d => d.Id.Value == "MIST_TC_0045");

        if(scenario.ExpectError)
        {
            return hasMistTc0045.ToProperty()
                .Label($"Expected MIST_TC_0045 for '{scenario.Label}' but it was not emitted")
                .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
        else
        {
            return (!hasMistTc0045).ToProperty()
                .Label($"Did NOT expect MIST_TC_0045 for '{scenario.Label}' but it was emitted")
                .Label($"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        }
    }
}