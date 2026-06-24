// Feature: iterators-enumerables, Property 1: Valid IEnumerable/IEnumerator implementations are accepted
// **Validates: Requirements 1.4, 2.4, 2.5**

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

// ─── Scenario types ─────────────────────────────────────────────────────────

/// <summary>
/// Scenario for verifying that a type correctly implementing
/// <c>IEnumerable&lt;TElement&gt;</c> with a proper nested <c>Enumerator</c> type
/// implementing <c>IEnumerator&lt;TElement&gt;</c> produces zero abstract-type-related diagnostics.
/// </summary>
public sealed class ValidEnumerableScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string ClassName { get; }
    public string EnumeratorKind { get; }
    public string Description { get; }

    public ValidEnumerableScenario(string source, string elementType, string className, string enumeratorKind, string description)
    {
        Source = source;
        ElementType = elementType;
        ClassName = className;
        EnumeratorKind = enumeratorKind;
        Description = description;
    }

    public override string ToString() => Description;
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for valid IEnumerable/IEnumerator implementations.
///
/// Property 1: Valid IEnumerable/IEnumerator implementations are accepted
///
/// For any type that declares it implements <c>IEnumerable&lt;TElement&gt;</c> and provides
/// a nested type <c>Enumerator</c> implementing <c>IEnumerator&lt;TElement&gt;</c> (with both
/// <c>MoveNext()</c> and <c>Current</c>), and a <c>GetEnumerator()</c> method returning
/// that <c>Enumerator</c> type, the type checker SHALL emit zero abstract-type-related
/// diagnostics for that type.
///
/// Uses in-process compilation via a shared CompilerDriver instance.
/// Inspects the DiagnosticBag directly (no process spawning).
///
/// **Validates: Requirements 1.4, 2.4, 2.5**
/// </summary>
public sealed class ValidEnumerableImplementationPropertyTests : IDisposable
{
    private readonly string _tempDir;

    public ValidEnumerableImplementationPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MistValidEnumPBT_" + Guid.NewGuid().ToString("N"));
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

    private static readonly string[] ElementTypes = ["int", "string", "bool", "double", "float", "long", "char"];
    private static readonly string[] ClassNames = ["MyCollection", "DataStream", "ItemList", "Sequence", "Elements", "Series", "Range", "Words"];

    /// <summary>
    /// The kind of nested Enumerator type to generate.
    /// </summary>
    private enum EnumeratorKind
    {
        /// <summary>Nested struct enumerator.</summary>
        Struct,
        /// <summary>Nested class enumerator.</summary>
        Class,
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a class implementing <c>IEnumerable&lt;TElement&gt;</c>
    /// with a properly-implemented nested Enumerator type (struct or class).
    /// </summary>
    private static Gen<ValidEnumerableScenario> GenValidEnumerableScenario()
    {
        return from elementType in Gen.Elements(ElementTypes)
               from className in Gen.Elements(ClassNames)
               from enumeratorKind in Gen.Elements(EnumeratorKind.Struct, EnumeratorKind.Class)
               let source = BuildValidEnumerableSource(className, elementType, enumeratorKind)
               select new ValidEnumerableScenario(
                   source, elementType, className, enumeratorKind.ToString(),
                   $"class {className} : IEnumerable<{elementType}> (Enumerator: {enumeratorKind})");
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildValidEnumerableSource(string className, string elementType, EnumeratorKind kind)
    {
        var enumeratorKeyword = kind == EnumeratorKind.Struct ? "struct" : "class";

        return $$"""
            namespace Standard;

            class {{className}} : IEnumerable<{{elementType}}>
            {
                public {{enumeratorKeyword}} Enumerator : IEnumerator<{{elementType}}>
                {
                    private int index;
                    public Enumerator()
                    {
                        this.index = -1;
                    }
                    public bool MoveNext()
                    {
                        this.index = this.index + 1;
                        return false;
                    }
                    public {{elementType}} Current { get; }
                }

                public Enumerator GetEnumerator()
                {
                    return new Enumerator();
                }
            }

            public class Program
            {
                public static void Main()
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

    public static Arbitrary<ValidEnumerableScenario> ArbitraryValidEnumerable() =>
        GenValidEnumerableScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Valid IEnumerable/IEnumerator implementations are accepted
    //
    // For any type that declares it implements IEnumerable<TElement> and
    // provides a nested type Enumerator implementing IEnumerator<TElement>
    // (with both MoveNext() and Current), and a GetEnumerator() method
    // returning that Enumerator type, the type checker SHALL emit zero
    // abstract-type-related diagnostics for that type.
    //
    // **Validates: Requirements 1.4, 2.4, 2.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidEnumerableImplementationPropertyTests) })]
    public Property ValidEnumerable_ZeroAbstractDiagnostics(ValidEnumerableScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // Check for abstract type-related diagnostics (MIST_SM_0022 = MissingAbstractOverload)
        var abstractDiagnostics = result.Diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        var hasAbstractDiagnostics = abstractDiagnostics.Count > 0;

        if (hasAbstractDiagnostics)
        {
            var diagMessages = string.Join("\n", abstractDiagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Expected zero abstract-type-related diagnostics but found {abstractDiagnostics.Count}:\n{diagMessages}\n" +
                       $"Scenario: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        // Also verify the compilation succeeded overall (no other errors that would
        // indicate a broken test scenario rather than an abstract type validation issue)
        if (!result.Succeeded)
        {
            var allDiags = string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return false.ToProperty()
                .Label($"Compilation failed (non-abstract-type diagnostics present):\n{allDiags}\n" +
                       $"Scenario: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"Valid IEnumerable<{scenario.ElementType}> implementation accepted without abstract type diagnostics");
    }
}
