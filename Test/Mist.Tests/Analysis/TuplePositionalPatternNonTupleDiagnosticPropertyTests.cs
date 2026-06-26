// Feature: tuple-pattern-matching, Property 3: Non-tuple type diagnostic
// **Validates: Requirements 4.1, 8.1**

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

/// <summary>
/// Property-based tests verifying:
///
/// Property 3: Non-tuple type diagnostic.
///
/// For any scrutinee type that is not a <c>TupleType</c> and not an <c>ErrorType</c>,
/// applying a tuple positional pattern SHALL cause the type checker to emit exactly
/// one diagnostic error whose message includes the actual scrutinee type's display name.
///
/// Uses the full compilation pipeline via a shared <see cref="CompilerDriver"/> instance
/// to comply with the test-process-spawning policy.
///
/// **Validates: Requirements 4.1, 8.1**
/// </summary>
public class TuplePositionalPatternNonTupleDiagnosticPropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

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

    // ─── Test data models ────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario where a tuple positional pattern is applied to a
    /// non-tuple scrutinee type.
    /// </summary>
#pragma warning disable CA1034
    public record NonTupleScenario(string TypeKeyword, string Initializer, string DisplayName)
    {
        public override string ToString() =>
            $"{TypeKeyword} x = {Initializer}; x is (_, _)";
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Non-tuple types with their corresponding literal initializers and expected
    /// display names in diagnostics. These are all primitive/built-in types that
    /// are guaranteed to NOT be tuple types.
    /// </summary>
    private static readonly (string TypeKeyword, string Initializer, string DisplayName)[] NonTupleTypes =
    [
        ("int", "42", "Int32"),
        ("string", "\"hello\"", "String"),
        ("bool", "true", "Boolean"),
        ("float", "1.0f", "Single"),
        ("double", "3.14", "Double"),
        ("long", "100", "Int64"),
        ("char", "'a'", "Char"),
        ("uint", "1", "UInt32"),
        ("short", "1", "Int16"),
        ("byte", "1", "Byte"),
    ];

    public static Arbitrary<NonTupleScenario> ArbitraryNonTupleScenario()
    {
        var gen = from entry in Gen.Elements(NonTupleTypes)
                  select new NonTupleScenario(entry.TypeKeyword, entry.Initializer, entry.DisplayName);
        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Non-tuple type diagnostic
    //
    // **Validates: Requirements 4.1, 8.1**
    //
    // For any scrutinee type that is not a TupleType and not an ErrorType,
    // applying a tuple positional pattern SHALL cause the type checker to emit
    // exactly one diagnostic error whose message includes the actual scrutinee
    // type's display name.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(TuplePositionalPatternNonTupleDiagnosticPropertyTests) })]
    public Property NonTupleScrutinee_EmitsExactlyOneDiagnosticWithTypeName(NonTupleScenario scenario)
    {
        var source = $$"""
            class Program {
                public static void Main() {
                    {{scenario.TypeKeyword}} x = {{scenario.Initializer}};
                    if (x is (_, _)) {
                    }
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        // Filter to only the MIST_TC_0113 diagnostic (non-tuple type for positional pattern)
        var nonTupleDiags = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0113")
            .ToList();

        // Exactly one diagnostic should be emitted
        var hasExactlyOne = nonTupleDiags.Count == 1;

        // The diagnostic message should include the type's display name
        var messageIncludesTypeName = hasExactlyOne &&
            nonTupleDiags[0].Message.Contains(scenario.DisplayName, StringComparison.Ordinal);

        return (hasExactlyOne && messageIncludesTypeName).ToProperty()
            .Label($"Expected exactly 1 MIST_TC_0113 diagnostic containing '{scenario.DisplayName}' " +
                   $"for type '{scenario.TypeKeyword}'. " +
                   $"Got {nonTupleDiags.Count} MIST_TC_0113 diagnostic(s). " +
                   $"Messages: [{string.Join("; ", nonTupleDiags.Select(d => d.Message))}]. " +
                   $"All diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
    }
}