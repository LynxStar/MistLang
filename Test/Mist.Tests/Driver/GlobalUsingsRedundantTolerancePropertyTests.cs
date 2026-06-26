// Feature: global-usings, Property 6: Redundant explicit using tolerance
//
// For any namespace that appears both as a global using and as an explicit
// using declaration in a source file, the semantic model builder SHALL produce
// no diagnostic related to the redundancy.
//
// Uses in-process CompilerDriver.Compile() with EmitToDisk = false.
// No external process is spawned per iteration.
//
// **Validates: Requirements 4.2**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
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

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests verifying that redundant explicit using declarations
/// (where the namespace is also a global using) produce no redundancy diagnostic
/// and the type still resolves correctly.
///
/// Property 6: Redundant explicit using tolerance
///
/// Uses a shared <see cref="CompilerDriver"/> instance per the test-process-spawning policy.
/// All iterations are in-process only — no process spawning.
///
/// **Validates: Requirements 4.2**
/// </summary>
public sealed class GlobalUsingsRedundantTolerancePropertyTests
{
    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                var sampleStr = samples[idx]?.ToString() ?? "<null>";
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: [{sampleStr}]\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    private static ImmutableArray<Diagnostic> CompileWithGlobalUsings(
        string definingSource, string consumingSource, ImmutableArray<string> globalUsings)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var defPath = Path.Combine(tempDir, "Definitions.mist");
            var conPath = Path.Combine(tempDir, "Consumer.mist");
            File.WriteAllText(defPath, definingSource);
            File.WriteAllText(conPath, consumingSource);

            var options = new CompilerOptions(
                ProjectFolder: tempDir,
                SourcePaths: ImmutableArray.Create(defPath, conPath),
                Backend: new BackendId("csharp"),
                OutputDirectory: tempDir,
                BinDirectory: Path.Combine(tempDir, "bin"),
                ProjectName: "TestProject",
                NamespaceRoot: "TestProject",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false,
                GlobalUsings: globalUsings);

            var result = SharedDriver.Compile(options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a valid identifier: starts with a letter, followed by letters/digits.
    /// Avoids Mist keywords by using a prefix.
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        var startChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(2, 8).SelectMany(restLen =>
                Gen.ArrayOf(Gen.Elements(restChars), restLen)
                    .Select(rest => first + new string(rest))));
    }

    /// <summary>
    /// Generates a valid namespace name (1–3 segments).
    /// </summary>
    private static Gen<string> GenNamespace()
    {
        return Gen.Choose(1, 3).SelectMany(segCount =>
            Gen.ArrayOf(GenIdentifier(), segCount)
                .Select(segs => string.Join(".", segs)));
    }

    /// <summary>
    /// Generates a scenario for the redundant explicit using tolerance test:
    /// a namespace name and a type name within that namespace.
    /// </summary>
    private static Gen<RedundantUsingScenario> GenScenario()
    {
        return GenNamespace().SelectMany(ns =>
            GenIdentifier().Select(typeName =>
                new RedundantUsingScenario(ns, typeName)));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Data model
    // ═══════════════════════════════════════════════════════════════════════

    private record RedundantUsingScenario(string Namespace, string TypeName)
    {
        public override string ToString() =>
            $"namespace {Namespace}; class {TypeName} — global using + explicit using {Namespace}";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Redundant explicit using tolerance
    // Feature: global-usings, Property 6: Redundant explicit using tolerance
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 6: For any namespace that appears both as a global using and as an
    /// explicit using declaration in a source file, the compiler SHALL produce no
    /// diagnostic related to redundancy, and the type SHALL still resolve correctly
    /// (no MIST_UNDEFINED_TYPE).
    ///
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Fact]
    public void P6_RedundantExplicitUsing_ProducesNoRedundancyDiagnostic()
    {
        RunProperty(GenScenario(), scenario =>
        {
            // File 1: defines a namespace with a type
            var definingSource = $"namespace {scenario.Namespace};\n\npublic class {scenario.TypeName} {{ }}";

            // File 2: has BOTH an explicit using AND the namespace is a global using,
            // and uses the type by simple name
            var consumingSource =
                $"using {scenario.Namespace};\n\n" +
                $"namespace Consumer;\n\n" +
                $"public class MyConsumer\n{{\n    {scenario.TypeName} myField;\n}}";

            var globalUsings = ImmutableArray.Create(scenario.Namespace);
            var diagnostics = CompileWithGlobalUsings(definingSource, consumingSource, globalUsings);

            // Assert: no "redundant" or "duplicate" diagnostic is produced
            var hasRedundancyDiag = diagnostics.Any(d =>
                d.Message.Contains("redundant", StringComparison.OrdinalIgnoreCase) ||
                d.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

            Assert.False(hasRedundancyDiag,
                $"Redundant explicit using for global using namespace '{scenario.Namespace}' " +
                $"should not produce a diagnostic. " +
                $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");

            // Assert: the type still resolves correctly (no MIST_UNDEFINED_TYPE)
            var hasUndefinedType = diagnostics.Any(d =>
                d.Id.Value == "MIST_UNDEFINED_TYPE" &&
                d.Message.Contains(scenario.TypeName));

            Assert.False(hasUndefinedType,
                $"Type '{scenario.TypeName}' should resolve when both global and explicit using " +
                $"are present for namespace '{scenario.Namespace}'. " +
                $"Diagnostics: [{string.Join(", ", diagnostics.Select(d => $"{d.Id.Value}: {d.Message}"))}]");
        });
    }
}