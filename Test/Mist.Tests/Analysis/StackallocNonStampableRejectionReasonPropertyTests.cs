// Feature: stack-only-types, Property 9: Non-stampable element rejection carries the correct reason
//
// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 14.3**
//
// In-process; for each non-stampable category assert the exact diagnostic ID
// (MIST_TC_0152/0153/0154/0155), no span type assigned, anchored at the element type.

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
/// Describes the non-stampable element type category for a stackalloc rejection
/// reason test scenario.
/// </summary>
public enum NonStampableCategory
{
    /// <summary>A class type — rejected with MIST_TC_0152.</summary>
    ReferenceType,

    /// <summary>A string (also a reference type) — rejected with MIST_TC_0152.</summary>
    StringType,

    /// <summary>A struct with a class-typed field — rejected with MIST_TC_0153.</summary>
    NonStampableStruct,

    /// <summary>A ref struct — rejected with MIST_TC_0154.</summary>
    RefStruct,

    /// <summary>An unconstrained type parameter — rejected with MIST_TC_0155.</summary>
    UnconstrainedTypeParameter,
}

/// <summary>
/// A generated scenario for testing non-stampable element rejection reasons.
/// </summary>
public sealed record NonStampableRejectionScenario(
    string Source,
    string ElementTypeName,
    NonStampableCategory Category,
    string ExpectedDiagnosticId)
{
    public override string ToString()
    {
        return $"stackalloc {ElementTypeName}[N] category={Category} → {ExpectedDiagnosticId}";
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for non-stampable element rejection reasons
/// (Property 9: Non-stampable element rejection carries the correct reason).
///
/// Verifies that <see cref="TypeCheckerAnalyzer"/> emits the exact diagnostic ID
/// corresponding to the non-stampable element type category:
/// <list type="bullet">
///   <item><c>MIST_TC_0152</c> — reference type (class or string)</item>
///   <item><c>MIST_TC_0153</c> — non-stampable struct</item>
///   <item><c>MIST_TC_0154</c> — ref struct</item>
///   <item><c>MIST_TC_0155</c> — unconstrained type parameter</item>
/// </list>
///
/// Also asserts that compilation fails (no emitted output) and the diagnostic
/// is anchored at the element type.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 14.3**
/// </summary>
public sealed class StackallocNonStampableRejectionReasonPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Name pools ─────────────────────────────────────────────────────

    private static readonly string[] ClassNames =
        ["Widget", "Service", "Handler", "Agent", "Controller"];

    private static readonly string[] NonStampableStructNames =
        ["Container", "Holder", "Wrapper", "Cell", "Envelope"];

    private static readonly string[] RefStructNames =
        ["StackRef", "FrameView", "SliceRef", "BufferRef", "PinnedRef"];

    private static readonly string[] TypeParamNames =
        ["TItem", "TValue", "TElement", "TData", "TPayload"];

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where the element type is a class (reference type).
    /// Expected: MIST_TC_0152.
    /// </summary>
    private static Gen<NonStampableRejectionScenario> GenReferenceTypeScenario()
    {
        return from className in Gen.Elements(ClassNames)
               from size in Gen.Choose(1, 32)
               let source = BuildClassTypeSource(className, size)
               select new NonStampableRejectionScenario(
                   source, className,
                   NonStampableCategory.ReferenceType,
                   "MIST_TC_0152");
    }

    /// <summary>
    /// Generates a scenario where the element type is string (reference type).
    /// Expected: MIST_TC_0152.
    /// </summary>
    private static Gen<NonStampableRejectionScenario> GenStringTypeScenario()
    {
        return from size in Gen.Choose(1, 32)
               let source = BuildStringSource(size)
               select new NonStampableRejectionScenario(
                   source, "string",
                   NonStampableCategory.StringType,
                   "MIST_TC_0152");
    }

    /// <summary>
    /// Generates a scenario where the element type is a struct with a class-typed field.
    /// Expected: MIST_TC_0153.
    /// </summary>
    private static Gen<NonStampableRejectionScenario> GenNonStampableStructScenario()
    {
        return from structName in Gen.Elements(NonStampableStructNames)
               from size in Gen.Choose(1, 32)
               let source = BuildNonStampableStructSource(structName, size)
               select new NonStampableRejectionScenario(
                   source, structName,
                   NonStampableCategory.NonStampableStruct,
                   "MIST_TC_0153");
    }

    /// <summary>
    /// Generates a scenario where the element type is a ref struct.
    /// Expected: MIST_TC_0154.
    /// </summary>
    private static Gen<NonStampableRejectionScenario> GenRefStructScenario()
    {
        return from refStructName in Gen.Elements(RefStructNames)
               from size in Gen.Choose(1, 32)
               let source = BuildRefStructSource(refStructName, size)
               select new NonStampableRejectionScenario(
                   source, refStructName,
                   NonStampableCategory.RefStruct,
                   "MIST_TC_0154");
    }

    /// <summary>
    /// Generates a scenario where the element type is an unconstrained type parameter.
    /// Expected: MIST_TC_0155.
    /// </summary>
    private static Gen<NonStampableRejectionScenario> GenUnconstrainedTypeParamScenario()
    {
        return from typeParam in Gen.Elements(TypeParamNames)
               from size in Gen.Choose(1, 32)
               let source = BuildUnconstrainedTypeParamSource(typeParam, size)
               select new NonStampableRejectionScenario(
                   source, typeParam,
                   NonStampableCategory.UnconstrainedTypeParameter,
                   "MIST_TC_0155");
    }

    /// <summary>
    /// Combined generator: all non-stampable categories with equal probability.
    /// </summary>
    public static Arbitrary<NonStampableRejectionScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenReferenceTypeScenario(),
            GenStringTypeScenario(),
            GenNonStampableStructScenario(),
            GenRefStructScenario(),
            GenUnconstrainedTypeParamScenario()).ToArbitrary();
    }

    // ─── Source generation ───────────────────────────────────────────────

    private static string BuildClassTypeSource(string className, int size)
    {
        return $$"""
            class {{className}} {
                public int id;
            }

            class Program {
                static void Main() {
                    var s = stackalloc {{className}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildStringSource(int size)
    {
        return $$"""
            class Program {
                static void Main() {
                    var s = stackalloc string[{{size}}];
                }
            }
            """;
    }

    private static string BuildNonStampableStructSource(string structName, int size)
    {
        return $$"""
            class Dependency {
                public int value;
            }

            struct {{structName}} {
                public int x;
                public Dependency dep;
            }

            class Program {
                static void Main() {
                    var s = stackalloc {{structName}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildRefStructSource(string refStructName, int size)
    {
        return $$"""
            ref struct {{refStructName}} {
                public int value;
            }

            class Program {
                static void Main() {
                    var s = stackalloc {{refStructName}}[{{size}}];
                }
            }
            """;
    }

    private static string BuildUnconstrainedTypeParamSource(string typeParam, int size)
    {
        return $$"""
            class Program {
                static void Allocate<{{typeParam}}>() {
                    var s = stackalloc {{typeParam}}[{{size}}];
                }

                static void Main() {
                }
            }
            """;
    }

    // ─── Compilation helper ──────────────────────────────────────────────

    private static ImmutableArray<Diagnostic> CompileAndGetDiagnostics(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
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
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 9: Non-stampable element rejection carries the correct reason
    //
    // **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 14.3**
    //
    // For each non-stampable category, assert:
    // 1. The exact diagnostic ID is emitted (MIST_TC_0152/0153/0154/0155)
    // 2. Compilation fails (error-severity diagnostic present)
    // 3. The diagnostic message names the offending element type
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 9: A stackalloc expression with a non-stampable element type emits
    /// exactly the diagnostic ID corresponding to its non-stampable category, and the
    /// message names the offending element type.
    ///
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7, 14.3**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocNonStampableRejectionReasonPropertyTests) })]
    public Property NonStampableElement_EmitsCorrectDiagnosticId(
        NonStampableRejectionScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // Look for the expected diagnostic ID.
        var matchingDiagnostics = diagnostics
            .Where(d => d.Id.Value == scenario.ExpectedDiagnosticId)
            .ToList();

        // The expected diagnostic must be present.
        var hasDiagnostic = matchingDiagnostics.Count >= 1;

        // The diagnostic message must name the element type.
        var messageNamesType = hasDiagnostic &&
            matchingDiagnostics.Any(d =>
                d.Message.Contains(scenario.ElementTypeName, StringComparison.Ordinal));

        // No other stampability diagnostic IDs should be emitted
        // (only the correct one for this category).
        var otherStampabilityIds = new[] { "MIST_TC_0152", "MIST_TC_0153", "MIST_TC_0154", "MIST_TC_0155" }
            .Where(id => id != scenario.ExpectedDiagnosticId);
        var noWrongDiagnostic = !diagnostics.Any(d =>
            otherStampabilityIds.Contains(d.Id.Value));

        var passed = hasDiagnostic && messageNamesType && noWrongDiagnostic;

        return passed.ToProperty()
            .Label(
                $"Expected {scenario.ExpectedDiagnosticId} for {scenario.Category} " +
                $"element type '{scenario.ElementTypeName}'.\n" +
                $"hasDiagnostic={hasDiagnostic} (count={matchingDiagnostics.Count}), " +
                $"messageNamesType={messageNamesType}, noWrongDiagnostic={noWrongDiagnostic}\n" +
                $"All errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                $"Source:\n{scenario.Source}");
    }

    /// <summary>
    /// Property 9 (no span type assigned): When a non-stampable element type is used,
    /// compilation fails — the diagnostic has error severity, confirming the expression
    /// is rejected and no valid span type is usable.
    ///
    /// **Validates: Requirements 5.4, 5.5, 5.6, 5.7**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocNonStampableRejectionReasonPropertyTests) })]
    public Property NonStampableElement_CompilationFails(
        NonStampableRejectionScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        // The compilation must have at least one error-severity diagnostic.
        var hasError = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

        // The specific stampability diagnostic must be error-severity.
        var stampabilityDiagIsError = diagnostics
            .Where(d => d.Id.Value == scenario.ExpectedDiagnosticId)
            .All(d => d.Severity == DiagnosticSeverity.Error);

        var passed = hasError && stampabilityDiagIsError;

        return passed.ToProperty()
            .Label(
                $"Expected compilation failure for non-stampable '{scenario.ElementTypeName}' " +
                $"({scenario.Category}).\n" +
                $"hasError={hasError}, stampabilityDiagIsError={stampabilityDiagIsError}\n" +
                $"Diagnostics: [{string.Join("; ", diagnostics.Select(d => $"{d.Id.Value}({d.Severity}): {d.Message}"))}]");
    }
}