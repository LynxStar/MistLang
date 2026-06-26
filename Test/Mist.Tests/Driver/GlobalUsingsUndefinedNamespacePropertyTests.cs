// Feature: global-usings, Property 7: Undefined namespace warning
//
// **Validates: Requirements 4.3**
//
// For any namespace name listed in global usings that does not exist as either
// a user-defined namespace or an ambient namespace, the semantic model builder
// SHALL produce a MIST_GLOBAL_USING_UNDEFINED_NAMESPACE warning diagnostic.

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
/// Property-based tests verifying that undefined global using namespaces
/// produce a <c>MIST_GLOBAL_USING_UNDEFINED_NAMESPACE</c> warning diagnostic.
/// All tests are pure in-process — no process spawning.
///
/// **Validates: Requirements 4.3**
/// </summary>
public sealed class GlobalUsingsUndefinedNamespacePropertyTests : IDisposable
{
    private readonly string outputDir;

    public GlobalUsingsUndefinedNamespacePropertyTests()
    {
        outputDir = Path.Combine(
            Path.GetTempPath(),
            "mist_gu_undef_prop_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new MistLexer(),
        new MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    // ─── Helpers ─────────────────────────────────────────────────────────

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

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid namespace identifier segment: starts with letter or underscore,
    /// followed by alphanumeric or underscore characters.
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        var startChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(0, 8).SelectMany(restLen =>
                Gen.ArrayOf(Gen.Elements(restChars), restLen)
                    .Select(rest => first + new string(rest))));
    }

    /// <summary>
    /// Generates a random non-existent namespace name. Uses a "NonExistent_" prefix
    /// followed by random identifier segments to ensure the namespace won't collide
    /// with any stdlib or user-defined namespaces.
    /// </summary>
    private static Gen<string> GenNonExistentNamespace()
    {
        return Gen.Choose(1, 3).SelectMany(segmentCount =>
            Gen.ArrayOf(GenIdentifier(), segmentCount)
                .Select(segments => "NonExistent_" + string.Join(".", segments)));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Undefined namespace warning
    // Feature: global-usings, Property 7: Undefined namespace warning
    //
    // **Validates: Requirements 4.3**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 7: For any namespace name listed in global usings that does not
    /// exist as either a user-defined namespace or an ambient namespace, the
    /// semantic model builder SHALL produce a MIST_GLOBAL_USING_UNDEFINED_NAMESPACE
    /// warning diagnostic containing the namespace name.
    ///
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Fact]
    public void P7_UndefinedGlobalUsingNamespace_ProducesWarning()
    {
        RunProperty(GenNonExistentNamespace(), nonExistentNs =>
        {
            // Create a minimal source file with a namespace that is NOT the global using
            var source = """
                namespace MyApp;

                public class Program
                {
                    public static void Main() { }
                }
                """;

            var filePath = Path.Combine(outputDir, $"Test_{Guid.NewGuid():N}.mist");
            File.WriteAllText(filePath, source);

            var options = new CompilerOptions(
                ProjectFolder: outputDir,
                SourcePaths: ImmutableArray.Create(filePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: outputDir,
                BinDirectory: Path.Combine(outputDir, "bin"),
                ProjectName: "MyApp",
                NamespaceRoot: "MyApp",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty,
                EmitToDisk: false,
                GlobalUsings: ImmutableArray.Create(nonExistentNs));

            var result = SharedDriver.Compile(options);

            // Assert: MIST_GLOBAL_USING_UNDEFINED_NAMESPACE warning is produced
            var undefinedNsDiags = result.Diagnostics
                .Where(d => d.Id.Value == "MIST_GLOBAL_USING_UNDEFINED_NAMESPACE")
                .ToList();

            Assert.NotEmpty(undefinedNsDiags);

            // Assert: the warning message contains the non-existent namespace name
            var matchingDiag = undefinedNsDiags.FirstOrDefault(d =>
                d.Message.Contains(nonExistentNs));

            Assert.True(matchingDiag is not null,
                $"Expected MIST_GLOBAL_USING_UNDEFINED_NAMESPACE warning containing '{nonExistentNs}'. " +
                $"Got diagnostics: [{string.Join(", ", undefinedNsDiags.Select(d => d.Message))}]");

            // Assert: the diagnostic is a warning (not an error)
            Assert.Equal(DiagnosticSeverity.Warning, matchingDiag!.Severity);
        });
    }
}