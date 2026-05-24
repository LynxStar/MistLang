// Feature: iterators-enumerables, Property 4: Foreach over non-IEnumerable types produces a diagnostic
// **Validates: Requirements 3.2**

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
using Mist.Lexing;
using Mist.Parsing;
using Mist.Semantics.Model;

namespace Mist.Tests.Analysis;

// ─── Scenario type ──────────────────────────────────────────────────────────

/// <summary>
/// Scenario for verifying that foreach over a type that does NOT implement
/// <c>IEnumerable&lt;TElement&gt;</c> produces the <c>MIST_TC_FOREACH_NOT_ENUMERABLE</c> diagnostic.
/// </summary>
public sealed class NonEnumerableForeachScenario
{
    public string Source { get; }
    public string TypeName { get; }
    public string Description { get; }

    public NonEnumerableForeachScenario(string source, string typeName, string description)
    {
        Source = source;
        TypeName = typeName;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for type checker foreach validation on non-IEnumerable types.
///
/// Property 4: Foreach over non-IEnumerable types produces a diagnostic
///
/// For any type that does not implement <c>IEnumerable&lt;TElement&gt;</c>, a foreach
/// statement with a collection expression of that type SHALL cause the type checker
/// to emit diagnostic <c>MIST_TC_FOREACH_NOT_ENUMERABLE</c>.
///
/// Uses in-process compilation via a shared CompilerDriver instance.
///
/// **Validates: Requirements 3.2**
/// </summary>
public sealed class ForeachNonEnumerablePropertyTests : IDisposable
{
    private readonly string _tempDir;

    public ForeachNonEnumerablePropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MistForeachNonEnumPBT_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<Mist.Analysis.Contracts.IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Name pools ──────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Widget", "Gadget", "Processor", "Handler", "Container", "Manager", "Service", "Helper"];

    private static readonly string[] FieldTypes = ["int", "string", "bool"];

    /// <summary>
    /// Kinds of non-IEnumerable types to generate.
    /// </summary>
    private enum NonEnumerableKind
    {
        /// <summary>Plain class with no interface implementations.</summary>
        PlainClass,
        /// <summary>Plain struct with no interface implementations.</summary>
        PlainStruct,
        /// <summary>Class with fields but no interface implementations.</summary>
        ClassWithFields,
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a type that does NOT implement IEnumerable&lt;TElement&gt;
    /// and a foreach statement attempting to iterate over it.
    /// </summary>
    private static Gen<NonEnumerableForeachScenario> GenNonEnumerableScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from kind in Gen.Elements(
                   NonEnumerableKind.PlainClass,
                   NonEnumerableKind.PlainStruct,
                   NonEnumerableKind.ClassWithFields)
               from fieldType in Gen.Elements(FieldTypes)
               let source = BuildNonEnumerableSource(className, kind, fieldType)
               let description = $"{kind}: {className} (field: {fieldType})"
               select new NonEnumerableForeachScenario(source, className, description);
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildNonEnumerableSource(string className, NonEnumerableKind kind, string fieldType)
    {
        var typeBody = kind switch
        {
            NonEnumerableKind.PlainClass => BuildPlainClassSource(className),
            NonEnumerableKind.PlainStruct => BuildPlainStructSource(className),
            NonEnumerableKind.ClassWithFields => BuildClassWithFieldsSource(className, fieldType),
            _ => BuildPlainClassSource(className),
        };

        return $$"""
            namespace Standard;

            {{typeBody}}

            public class Program
            {
                public static void Main()
                {
                    var item = new {{className}}();
                    foreach (int x in item)
                    {
                    }
                }
            }
            """;
    }

    private static string BuildPlainClassSource(string className)
    {
        return $$"""
            class {{className}}
            {
                public {{className}}()
                {
                }
            }
            """;
    }

    private static string BuildPlainStructSource(string className)
    {
        return $$"""
            struct {{className}}
            {
                public {{className}}()
                {
                }
            }
            """;
    }

    private static string BuildClassWithFieldsSource(string className, string fieldType)
    {
        return $$"""
            class {{className}}
            {
                private {{fieldType}} value;
                public {{className}}()
                {
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private CompilerResult CompileSource(string source)
    {
        var filePath = Path.Combine(_tempDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: _tempDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: _tempDir,
            BinDirectory: Path.Combine(_tempDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "Standard",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<NonEnumerableForeachScenario> ArbitraryNonEnumerableForeach() =>
        GenNonEnumerableScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Foreach over non-IEnumerable types produces a diagnostic
    //
    // For any type that does not implement IEnumerable<TElement>, a foreach
    // statement with a collection expression of that type SHALL cause the
    // type checker to emit diagnostic MIST_TC_FOREACH_NOT_ENUMERABLE.
    //
    // **Validates: Requirements 3.2**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ForeachNonEnumerablePropertyTests) })]
    public Property NonEnumerableType_ProducesForeachDiagnostic(NonEnumerableForeachScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // The compilation should produce the MIST_TC_FOREACH_NOT_ENUMERABLE diagnostic.
        var hasForeachDiagnostic = result.Diagnostics.Any(d =>
            d.Id.Value == "MIST_TC_FOREACH_NOT_ENUMERABLE");

        return hasForeachDiagnostic.ToProperty()
            .Label($"Expected diagnostic MIST_TC_FOREACH_NOT_ENUMERABLE for type '{scenario.TypeName}' " +
                   $"but it was not found.\n" +
                   $"Diagnostics: [{string.Join(", ", result.Diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                   $"Source:\n{scenario.Source}");
    }
}
