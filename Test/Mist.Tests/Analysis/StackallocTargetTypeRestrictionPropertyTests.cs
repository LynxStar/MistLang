// Feature: stack-only-types, Property 6: Stackalloc target-type restriction
//
// **Validates: Requirements 4.6, 14.4**
//
// In-process; assert accepted iff var / Span family / ReadOnlySpan family target,
// else rejected with MIST_TC_0150 whose message names both types.

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
/// Describes a stackalloc target-type scenario for property testing.
/// </summary>
public sealed record StackallocTargetTypeScenario(
    string Source,
    bool ShouldBeAccepted,
    string TargetDescription,
    string Description)
{
    public override string ToString()
    {
        return Description;
    }
}

// ─── Property Tests ─────────────────────────────────────────────────────────

/// <summary>
/// Property-based tests for stackalloc target-type restriction
/// (Property 6: Stackalloc target-type restriction).
///
/// Verifies that <see cref="TypeCheckerAnalyzer"/> accepts a stackalloc expression
/// only when the target type is <c>var</c>, <c>Span&lt;T&gt;</c> family, or
/// <c>ReadOnlySpan&lt;T&gt;</c> family, and rejects with <c>MIST_TC_0150</c>
/// (naming both the result type and the expected type) for all other targets.
///
/// Uses in-process compilation via a shared <see cref="CompilerDriver"/> instance.
/// Zero process spawns.
///
/// **Validates: Requirements 4.6, 14.4**
/// </summary>
public class StackallocTargetTypeRestrictionPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Element type pool: stampable primitives ─────────────────────────

    private static readonly string[] StampableTypes =
        ["int", "uint", "long", "byte", "short", "float", "double", "bool", "char"];

    /// <summary>
    /// Maps Mist primitive type keywords to their DisplayName forms as used
    /// in diagnostic messages.
    /// </summary>
    private static string GetDisplayName(string mistType)
    {
        return mistType switch
        {
            "int" => "Int32",
            "uint" => "UInt32",
            "long" => "Int64",
            "ulong" => "UInt64",
            "short" => "Int16",
            "ushort" => "UInt16",
            "byte" => "Byte",
            "sbyte" => "SByte",
            "float" => "Single",
            "double" => "Double",
            "bool" => "Boolean",
            "char" => "Char",
            _ => mistType
        };
    }

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a scenario where stackalloc is assigned to a <c>var</c> declaration.
    /// Expected: accepted (no MIST_TC_0150).
    /// </summary>
    private static Gen<StackallocTargetTypeScenario> GenVarTargetScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 64)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram($"var s = stackalloc {elementType}[{size}];")
               select new StackallocTargetTypeScenario(
                   source,
                   ShouldBeAccepted: true,
                   TargetDescription: "var",
                   $"var s = stackalloc {elementType}[{size}] → accepted");
    }

    /// <summary>
    /// Generates a scenario where stackalloc is assigned to an explicit
    /// <c>Span&lt;T, unknown&gt;</c> target.
    /// Expected: accepted (no MIST_TC_0150).
    /// </summary>
    private static Gen<StackallocTargetTypeScenario> GenExplicitSpanTargetScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 64)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"Span<{elementType}, unknown> s = stackalloc {elementType}[{size}];")
               select new StackallocTargetTypeScenario(
                   source,
                   ShouldBeAccepted: true,
                   TargetDescription: $"Span<{elementType}, unknown>",
                   $"Span<{elementType}, unknown> s = stackalloc {elementType}[{size}] → accepted");
    }

    /// <summary>
    /// Generates a scenario where stackalloc is assigned to an explicit
    /// <c>ReadOnlySpan&lt;T, unknown&gt;</c> target.
    /// Expected: accepted (no MIST_TC_0150).
    /// </summary>
    private static Gen<StackallocTargetTypeScenario> GenExplicitReadOnlySpanTargetScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 64)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[{size}];")
               select new StackallocTargetTypeScenario(
                   source,
                   ShouldBeAccepted: true,
                   TargetDescription: $"ReadOnlySpan<{elementType}, unknown>",
                   $"ReadOnlySpan<{elementType}, unknown> s = stackalloc {elementType}[{size}] → accepted");
    }

    /// <summary>
    /// Generates a scenario where stackalloc is assigned to an <c>int</c> target.
    /// Expected: rejected with MIST_TC_0150.
    /// The diagnostic will display the target type as 'Int32' (its DisplayName).
    /// </summary>
    private static Gen<StackallocTargetTypeScenario> GenIntTargetScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 64)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"int s = stackalloc {elementType}[{size}];")
               select new StackallocTargetTypeScenario(
                   source,
                   ShouldBeAccepted: false,
                   TargetDescription: "Int32",
                   $"int s = stackalloc {elementType}[{size}] → rejected MIST_TC_0150");
    }

    /// <summary>
    /// Generates a scenario where stackalloc is assigned to a <c>string</c> target.
    /// Expected: rejected with MIST_TC_0150.
    /// The diagnostic will display the target type as 'String' (its DisplayName).
    /// </summary>
    private static Gen<StackallocTargetTypeScenario> GenStringTargetScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 64)
               let elementType = StampableTypes[typeIdx]
               let source = WrapInProgram(
                   $"string s = stackalloc {elementType}[{size}];")
               select new StackallocTargetTypeScenario(
                   source,
                   ShouldBeAccepted: false,
                   TargetDescription: "String",
                   $"string s = stackalloc {elementType}[{size}] → rejected MIST_TC_0150");
    }

    /// <summary>
    /// Generates a scenario where stackalloc is assigned to an array target.
    /// Expected: rejected with MIST_TC_0150.
    /// The diagnostic will display the target type using the CLR DisplayName
    /// (e.g., 'Int32[]' for <c>int[]</c>).
    /// </summary>
    private static Gen<StackallocTargetTypeScenario> GenArrayTargetScenario()
    {
        return from typeIdx in Gen.Choose(0, StampableTypes.Length - 1)
               from size in Gen.Choose(1, 64)
               let elementType = StampableTypes[typeIdx]
               let displayName = GetDisplayName(elementType)
               let source = WrapInProgram(
                   $"{elementType}[] s = stackalloc {elementType}[{size}];")
               select new StackallocTargetTypeScenario(
                   source,
                   ShouldBeAccepted: false,
                   TargetDescription: $"{displayName}[]",
                   $"{elementType}[] s = stackalloc {elementType}[{size}] → rejected MIST_TC_0150");
    }

    /// <summary>
    /// Combined generator: all scenario types with equal probability.
    /// </summary>
    public static Arbitrary<StackallocTargetTypeScenario> ArbitraryScenario()
    {
        return Gen.OneOf(
            GenVarTargetScenario(),
            GenExplicitSpanTargetScenario(),
            GenExplicitReadOnlySpanTargetScenario(),
            GenIntTargetScenario(),
            GenStringTargetScenario(),
            GenArrayTargetScenario()).ToArbitrary();
    }

    // ─── Program wrapping ───────────────────────────────────────────────

    private static string WrapInProgram(string body)
    {
        return $$"""
            class Program {
                static void Main() {
                    {{body}}
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
    // Property 6: Stackalloc target-type restriction
    //
    // **Validates: Requirements 4.6, 14.4**
    //
    // A stackalloc expression is accepted iff the target type is:
    // - var (infers Span<T, Length>)
    // - Span<T, Length> / Span<T> (explicit mutable span family)
    // - ReadOnlySpan<T, Length> / ReadOnlySpan<T> (explicit read-only span family)
    //
    // Otherwise, MIST_TC_0150 is emitted whose message names both the
    // stackalloc result type and the target type.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6: A stackalloc expression is accepted when target is var / Span family /
    /// ReadOnlySpan family, and rejected with MIST_TC_0150 for all other targets.
    ///
    /// **Validates: Requirements 4.6, 14.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocTargetTypeRestrictionPropertyTests) })]
    public Property StackallocExpression_AcceptedIffValidTarget(
        StackallocTargetTypeScenario scenario)
    {
        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var targetMismatchDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0150")
            .ToList();

        if(scenario.ShouldBeAccepted)
        {
            // Valid target: no MIST_TC_0150 should be emitted.
            var passed = targetMismatchDiagnostics.Count == 0;
            return passed.ToProperty()
                .Label(
                    $"Expected no MIST_TC_0150 for valid target '{scenario.TargetDescription}', " +
                    $"but got {targetMismatchDiagnostics.Count} diagnostic(s).\n" +
                    $"Messages: [{string.Join("; ", targetMismatchDiagnostics.Select(d => d.Message))}]\n" +
                    $"All errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                    $"Source:\n{scenario.Source}");
        }
        else
        {
            // Invalid target: exactly one MIST_TC_0150 should be emitted.
            var hasDiagnostic = targetMismatchDiagnostics.Count >= 1;

            // The message must name both types (R14.4):
            // - The stackalloc result type (a Span DisplayName)
            // - The target type
            var messageNamesBothTypes = hasDiagnostic &&
                targetMismatchDiagnostics.Any(d =>
                    d.Message.Contains("Span") &&
                    d.Message.Contains(scenario.TargetDescription, StringComparison.OrdinalIgnoreCase));

            var passed = hasDiagnostic && messageNamesBothTypes;
            return passed.ToProperty()
                .Label(
                    $"Expected MIST_TC_0150 for invalid target '{scenario.TargetDescription}' " +
                    $"with message naming both types.\n" +
                    $"Got {targetMismatchDiagnostics.Count} MIST_TC_0150 diagnostic(s).\n" +
                    $"Messages: [{string.Join("; ", targetMismatchDiagnostics.Select(d => d.Message))}]\n" +
                    $"hasDiagnostic={hasDiagnostic}, messageNamesBothTypes={messageNamesBothTypes}\n" +
                    $"All errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                    $"Source:\n{scenario.Source}");
        }
    }

    /// <summary>
    /// Property 6 (message quality): When MIST_TC_0150 is emitted, the diagnostic
    /// message always names the stackalloc result type (a Span family type).
    ///
    /// **Validates: Requirement 14.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(StackallocTargetTypeRestrictionPropertyTests) })]
    public Property StackallocTargetMismatch_MessageNamesResultType(
        StackallocTargetTypeScenario scenario)
    {
        // Only test rejection scenarios.
        if(scenario.ShouldBeAccepted)
        {
            return true.ToProperty().Label("Skipped: valid target scenario");
        }

        var diagnostics = CompileAndGetDiagnostics(scenario.Source);

        var targetMismatchDiagnostics = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0150")
            .ToList();

        if(targetMismatchDiagnostics.Count == 0)
        {
            return false.ToProperty()
                .Label(
                    $"No MIST_TC_0150 emitted for invalid target '{scenario.TargetDescription}'.\n" +
                    $"All errors: [{string.Join("; ", diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{d.Id.Value}: {d.Message}"))}]\n" +
                    $"Source:\n{scenario.Source}");
        }

        // The message should mention the Span type (the result of stackalloc).
        var mentionsSpan = targetMismatchDiagnostics.All(d =>
            d.Message.Contains("Span", StringComparison.Ordinal));

        return mentionsSpan.ToProperty()
            .Label(
                $"MIST_TC_0150 message does not mention the Span result type.\n" +
                $"Messages: [{string.Join("; ", targetMismatchDiagnostics.Select(d => d.Message))}]");
    }
}