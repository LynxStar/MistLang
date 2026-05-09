// Feature: refinement-types, Property 3: Compile-time enforcement correctness

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
/// Property-based tests verifying compile-time enforcement correctness for
/// refinement types. For any refinement type with a relational predicate and
/// for any integer literal, the type checker SHALL accept the assignment if and
/// only if the literal satisfies the predicate.
///
/// Specifically: for <c>type T = int >= N;</c> and literal <c>L</c>, assignment
/// is accepted iff <c>L >= N</c>.
///
/// Uses the full compilation pipeline (lexer → parser → semantic model → type
/// checker) via a shared CompilerDriver instance to comply with the
/// test-process-spawning policy.
///
/// **Validates: Requirements 5.1, 5.2, 5.4**
/// </summary>
public class RefinementEnforcementPropertyTests
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

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a scenario: a refinement type <c>type T = int >= Threshold;</c>
    /// and a literal assignment <c>T x = Literal;</c>.
    /// </summary>
#pragma warning disable CA1034
    public record EnforcementScenario(int Threshold, int Literal)
    {
        /// <summary>Whether the literal satisfies the predicate (L >= N).</summary>
        public bool ShouldAccept => Literal >= Threshold;

        public override string ToString() =>
            $"type T = int >= {Threshold}; T x = {Literal}; (expect {(ShouldAccept ? "accept" : "reject")})";
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<EnforcementScenario> GenEnforcementScenario()
    {
        return from threshold in Gen.Choose(0, 200)
               from literal in Gen.Choose(0, 200)
               select new EnforcementScenario(threshold, literal);
    }

    public static Arbitrary<EnforcementScenario> ArbitraryEnforcementScenario() =>
        GenEnforcementScenario().ToArbitrary();

    // ─── Property test ───────────────────────────────────────────────────

    /// <summary>
    /// Property 3: Compile-time enforcement correctness.
    ///
    /// For any refinement type <c>type T = int >= N;</c> and any integer literal L,
    /// the type checker accepts the assignment <c>T x = L;</c> if and only if L >= N.
    ///
    /// When L >= N: no MIST_TC_0097 diagnostic is emitted.
    /// When L &lt; N: exactly one MIST_TC_0097 diagnostic is emitted.
    ///
    /// **Validates: Requirements 5.1, 5.2, 5.4**
    /// </summary>
    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(RefinementEnforcementPropertyTests) })]
    public bool LiteralAccepted_IffSatisfiesPredicate(EnforcementScenario scenario)
    {
        var source = $$"""
            type T = int >= {{scenario.Threshold}};
            class Program {
                public static void Main() {
                    T x = {{scenario.Literal}};
                }
            }
            """;

        var diagnostics = CompileAndGetDiagnostics(source);

        var refinementViolations = diagnostics
            .Where(d => d.Id.Value == "MIST_TC_0097")
            .ToList();

        if (scenario.ShouldAccept)
        {
            // Literal satisfies predicate: no violation diagnostic expected
            return refinementViolations.Count == 0;
        }
        else
        {
            // Literal violates predicate: exactly one violation diagnostic expected
            return refinementViolations.Count == 1;
        }
    }
}
