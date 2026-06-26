// Feature: iterators-enumerables, Property 2: Invalid IEnumerator/IEnumerable implementations produce diagnostics
// **Validates: Requirements 1.5, 2.4, 2.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
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
/// Scenario for verifying that a type declaring <c>IEnumerator&lt;TElement&gt;</c>
/// but missing required members produces at least one diagnostic error.
/// </summary>
public sealed class InvalidEnumeratorScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string TypeName { get; }
    public InvalidEnumeratorKind Kind { get; }
    public string Description { get; }

    public InvalidEnumeratorScenario(string source, string elementType, string typeName, InvalidEnumeratorKind kind, string description)
    {
        Source = source;
        ElementType = elementType;
        TypeName = typeName;
        Kind = kind;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// Scenario for verifying that a type declaring <c>IEnumerable&lt;TElement&gt;</c>
/// but with an invalid implementation produces at least one diagnostic error.
/// </summary>
public sealed class InvalidEnumerableScenario
{
    public string Source { get; }
    public string ElementType { get; }
    public string TypeName { get; }
    public InvalidEnumerableKind Kind { get; }
    public string Description { get; }

    public InvalidEnumerableScenario(string source, string elementType, string typeName, InvalidEnumerableKind kind, string description)
    {
        Source = source;
        ElementType = elementType;
        TypeName = typeName;
        Kind = kind;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// The kind of invalid IEnumerator implementation to generate.
/// </summary>
public enum InvalidEnumeratorKind
{
    /// <summary>Missing MoveNext() method.</summary>
    MissingMoveNext,
    /// <summary>Missing Current property.</summary>
    MissingCurrent,
    /// <summary>Missing both MoveNext() and Current.</summary>
    MissingBoth,
}

/// <summary>
/// The kind of invalid IEnumerable implementation to generate.
/// </summary>
public enum InvalidEnumerableKind
{
    /// <summary>Missing GetEnumerator() method.</summary>
    MissingGetEnumerator,
    /// <summary>Enumerator type does not implement IEnumerator&lt;TElement&gt;.</summary>
    EnumeratorNotImplementingIEnumerator,
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for invalid IEnumerator/IEnumerable implementations.
///
/// Property 2: Invalid IEnumerator/IEnumerable implementations produce diagnostics
///
/// For any type that declares it implements <c>IEnumerator&lt;TElement&gt;</c> but is missing
/// <c>MoveNext()</c>, <c>Current</c>, or both, the type checker SHALL emit at least one
/// diagnostic error identifying the missing member(s). Similarly, for any type implementing
/// <c>IEnumerable&lt;TElement&gt;</c> whose <c>Enumerator</c> type does not satisfy
/// <c>IEnumerator&lt;TElement&gt;</c>, or whose <c>GetEnumerator()</c> is missing or has a
/// mismatched return type, the type checker SHALL emit a diagnostic.
///
/// Uses in-process compilation via a shared CompilerDriver instance.
/// Inspects the DiagnosticBag directly (no process spawning).
///
/// **Validates: Requirements 1.5, 2.4, 2.5**
/// </summary>
public sealed class InvalidEnumerableImplementationPropertyTests : IDisposable
{
    private readonly string tempDir;

    public InvalidEnumerableImplementationPropertyTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "MistInvalidEnumPBT_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(tempDir))
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
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
    private static readonly string[] TypeNames = ["BadIterator", "BrokenCursor", "FaultyStream", "InvalidWalker", "MissingImpl", "PartialEnum"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario with a type implementing <c>IEnumerator&lt;TElement&gt;</c>
    /// but missing one or more required members.
    /// </summary>
    private static Gen<InvalidEnumeratorScenario> GenInvalidEnumeratorScenario()
    {
        return from elementType in Gen.Elements(ElementTypes)
               from typeName in Gen.Elements(TypeNames)
               from kind in Gen.Elements(
                   InvalidEnumeratorKind.MissingMoveNext,
                   InvalidEnumeratorKind.MissingCurrent,
                   InvalidEnumeratorKind.MissingBoth)
               let source = BuildInvalidEnumeratorSource(typeName, elementType, kind)
               select new InvalidEnumeratorScenario(
                   source, elementType, typeName, kind,
                   $"struct {typeName} : IEnumerator<{elementType}> ({kind})");
    }

    /// <summary>
    /// Generates a scenario with a type implementing <c>IEnumerable&lt;TElement&gt;</c>
    /// but with an invalid implementation.
    /// </summary>
    private static Gen<InvalidEnumerableScenario> GenInvalidEnumerableScenario()
    {
        return from elementType in Gen.Elements(ElementTypes)
               from typeName in Gen.Elements(TypeNames)
               from kind in Gen.Elements(
                   InvalidEnumerableKind.MissingGetEnumerator,
                   InvalidEnumerableKind.EnumeratorNotImplementingIEnumerator)
               let source = BuildInvalidEnumerableSource(typeName, elementType, kind)
               select new InvalidEnumerableScenario(
                   source, elementType, typeName, kind,
                   $"class {typeName} : IEnumerable<{elementType}> ({kind})");
    }

    // ─── Source builders ─────────────────────────────────────────────────

    private static string BuildInvalidEnumeratorSource(string typeName, string elementType, InvalidEnumeratorKind kind)
    {
        var members = kind switch
        {
            InvalidEnumeratorKind.MissingMoveNext => $$"""
                    public {{elementType}} Current { get; }
            """,
            InvalidEnumeratorKind.MissingCurrent => """
                    public bool MoveNext()
                    {
                        return false;
                    }
            """,
            InvalidEnumeratorKind.MissingBoth => """
                    private int placeholder;
            """,
            _ => ""
        };

        return $$"""
            namespace Standard;

            struct {{typeName}} : IEnumerator<{{elementType}}>
            {
            {{members}}
                public {{typeName}}()
                {
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

    private static string BuildInvalidEnumerableSource(string typeName, string elementType, InvalidEnumerableKind kind)
    {
        return kind switch
        {
            InvalidEnumerableKind.MissingGetEnumerator => BuildMissingGetEnumeratorSource(typeName, elementType),
            InvalidEnumerableKind.EnumeratorNotImplementingIEnumerator => BuildEnumeratorNotImplementingSource(typeName, elementType),
            _ => BuildMissingGetEnumeratorSource(typeName, elementType),
        };
    }

    private static string BuildMissingGetEnumeratorSource(string typeName, string elementType)
    {
        // Type implements IEnumerable<TElement> with a valid Enumerator but missing GetEnumerator()
        return $$"""
            namespace Standard;

            class {{typeName}} : IEnumerable<{{elementType}}>
            {
                public struct Enumerator : IEnumerator<{{elementType}}>
                {
                    public Enumerator()
                    {
                    }
                    public bool MoveNext()
                    {
                        return false;
                    }
                    public {{elementType}} Current { get; }
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

    private static string BuildEnumeratorNotImplementingSource(string typeName, string elementType)
    {
        // Type implements IEnumerable<TElement> with GetEnumerator() but the Enumerator
        // type does NOT implement IEnumerator<TElement> (missing the abstract type declaration)
        return $$"""
            namespace Standard;

            class {{typeName}} : IEnumerable<{{elementType}}>
            {
                public struct Enumerator
                {
                    public Enumerator()
                    {
                    }
                    public bool MoveNext()
                    {
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
        var filePath = Path.Combine(tempDir, $"Test_{Guid.NewGuid():N}.mist");
        File.WriteAllText(filePath, source);

        var options = new CompilerOptions(
            ProjectFolder: tempDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: tempDir,
            BinDirectory: Path.Combine(tempDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "Standard",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);

        return SharedDriver.Compile(options);
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<InvalidEnumeratorScenario> ArbitraryInvalidEnumerator() =>
        GenInvalidEnumeratorScenario().ToArbitrary();

    public static Arbitrary<InvalidEnumerableScenario> ArbitraryInvalidEnumerable() =>
        GenInvalidEnumerableScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2a: IEnumerator<TElement> missing required members produces diagnostics
    //
    // For any type that declares it implements IEnumerator<TElement> but is
    // missing MoveNext(), Current, or both, the type checker SHALL emit at
    // least one diagnostic error (MIST_SM_0022) identifying the missing member(s).
    //
    // **Validates: Requirements 1.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InvalidEnumerableImplementationPropertyTests) })]
    public Property InvalidEnumerator_MissingMembers_ProducesDiagnostic(InvalidEnumeratorScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // Check for abstract type-related diagnostics:
        // - MIST_SM_0022 (MissingAbstractOverload) for missing methods
        // - MIST_MISSING_ABSTRACT_PROPERTY for missing properties (e.g., Current)
        var abstractDiagnostics = result.Diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022"
                     || d.Id.Value == "MIST_MISSING_ABSTRACT_PROPERTY")
            .ToList();

        var hasDiagnostic = abstractDiagnostics.Count > 0;

        if(!hasDiagnostic)
        {
            var allDiags = result.Diagnostics.Any()
                ? string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"))
                : "  (none)";
            return false.ToProperty()
                .Label($"Expected at least one abstract type diagnostic for {scenario.Kind} " +
                       $"but found none.\nAll diagnostics:\n{allDiags}\n" +
                       $"Scenario: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"Invalid IEnumerator<{scenario.ElementType}> ({scenario.Kind}) correctly produced " +
                   $"{abstractDiagnostics.Count} diagnostic(s)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2b: IEnumerable<TElement> with invalid implementation produces diagnostics
    //
    // For any type implementing IEnumerable<TElement> whose Enumerator type
    // does not satisfy IEnumerator<TElement>, or whose GetEnumerator() is
    // missing, the type checker SHALL emit at least one diagnostic error.
    //
    // **Validates: Requirements 2.4, 2.5**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(InvalidEnumerableImplementationPropertyTests) })]
    public Property InvalidEnumerable_InvalidImplementation_ProducesDiagnostic(InvalidEnumerableScenario scenario)
    {
        var result = CompileSource(scenario.Source);

        // Check for abstract type-related diagnostics (MIST_SM_0022 = MissingAbstractOverload)
        // or any error-severity diagnostic that indicates the invalid implementation was caught
        var abstractDiagnostics = result.Diagnostics
            .Where(d => d.Id.Value == "MIST_SM_0022")
            .ToList();

        var hasErrorDiagnostic = abstractDiagnostics.Count > 0
            || result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        if(!hasErrorDiagnostic)
        {
            var allDiags = result.Diagnostics.Any()
                ? string.Join("\n", result.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"))
                : "  (none)";
            return false.ToProperty()
                .Label($"Expected at least one diagnostic error for {scenario.Kind} " +
                       $"but found none.\nAll diagnostics:\n{allDiags}\n" +
                       $"Scenario: {scenario.Description}\nSource:\n{scenario.Source}");
        }

        return true.ToProperty()
            .Label($"Invalid IEnumerable<{scenario.ElementType}> ({scenario.Kind}) correctly produced diagnostic(s)");
    }
}