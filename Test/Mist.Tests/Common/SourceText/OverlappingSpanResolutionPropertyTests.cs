// Feature: node-origin-tracking, Property 6: Overlapping Spans Resolve Correctly via Origin
// **Validates: Requirements 4.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Generated;
using Storm.Runtime;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Property-based tests for overlapping span resolution via Origin.
///
/// Property 6: Overlapping Spans Resolve Correctly via Origin.
/// For any set of N source files with numerically overlapping span ranges, and for any node
/// whose Origin is a ParsedOrigin referencing file F, DiagnosticHelper.ResolveLocation SHALL
/// return file F's path — regardless of whether other files have spans that numerically contain
/// the node's span.
/// </summary>
public class OverlappingSpanResolutionPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a unique file path from an index.
    /// </summary>
    private static string MakePath(int index) => $"src/file{index}.mist";

    /// <summary>
    /// Generates source content of at least the given length.
    /// </summary>
    private static string MakeContent(int minLength)
    {
        // Create content that is at least minLength characters
        var line = "let x = 42\n";
        var repetitions = (minLength / line.Length) + 2;
        return string.Concat(Enumerable.Repeat(line, repetitions));
    }

    /// <summary>
    /// Input record for the property test: a set of files with overlapping spans
    /// and a target file index whose node we resolve.
    /// </summary>
#pragma warning disable CA1034
    public record OverlappingScenario(int FileCount, int TargetFileIndex, int SpanStart, int SpanLength)
    {
        public override string ToString() =>
            $"OverlappingScenario(Files={FileCount}, Target={TargetFileIndex}, Span=[{SpanStart},{SpanStart + SpanLength}))";
    }
#pragma warning restore CA1034

    /// <summary>
    /// Generates scenarios with N files (2-5) that all have content large enough
    /// to contain the same span, and a target file index.
    /// </summary>
    private static Gen<OverlappingScenario> GenOverlappingScenario()
    {
        return from fileCount in Gen.Choose(2, 5)
               from spanStart in Gen.Choose(0, 20)
               from spanLength in Gen.Choose(1, 15)
               from targetIndex in Gen.Choose(0, fileCount - 1)
               select new OverlappingScenario(fileCount, targetIndex, spanStart, spanLength);
    }

    public static Arbitrary<OverlappingScenario> ArbOverlappingScenario() =>
        GenOverlappingScenario().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any set of N source files with numerically overlapping span ranges,
    /// and for any node whose Origin is a ParsedOrigin referencing file F,
    /// ResolveLocation SHALL return file F's path — regardless of whether other
    /// files have spans that numerically contain the node's span.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverlappingSpanResolutionPropertyTests) })]
    public bool ResolveLocation_WithOverlappingSpans_ReturnsCorrectFilePath(OverlappingScenario scenario)
    {
        // Create N source files, all with content large enough to contain the same span
        var minContentLength = scenario.SpanStart + scenario.SpanLength + 1;
        var sourceTexts = Enumerable.Range(0, scenario.FileCount)
            .Select(i => new Mist.Common.Text.SourceText(MakePath(i), MakeContent(minContentLength)))
            .ToArray();

        // Create a node with a span that overlaps across all files
        var node = new BlockNode();
        node.Span = new Storm.Runtime.SourceSpan(scenario.SpanStart, scenario.SpanLength);

        // Assign the node's Origin to the target file
        var targetSource = sourceTexts[scenario.TargetFileIndex];
        node.Origin = new ParsedOrigin(targetSource);

        // Resolve the location — it should return the target file's path
        var (path, _, _) = DiagnosticHelper.ResolveLocation(node);

        return path == targetSource.Path;
    }

    /// <summary>
    /// For any node with a ParsedOrigin, even when other SourceText instances exist
    /// with identical span ranges, the resolved path is always the Origin's source path.
    /// This verifies that resolution is O(1) via Origin, not a search.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverlappingSpanResolutionPropertyTests) })]
    public bool ResolveLocation_EachFileResolvesIndependently(OverlappingScenario scenario)
    {
        // Create N source files with identical content (maximally overlapping spans)
        var minContentLength = scenario.SpanStart + scenario.SpanLength + 1;
        var content = MakeContent(minContentLength);
        var sourceTexts = Enumerable.Range(0, scenario.FileCount)
            .Select(i => new Mist.Common.Text.SourceText(MakePath(i), content))
            .ToArray();

        // For each file, create a node with the same span but different Origin
        // and verify each resolves to its own file
        for(var i = 0; i < scenario.FileCount; i++)
        {
            var node = new BlockNode();
            node.Span = new Storm.Runtime.SourceSpan(scenario.SpanStart, scenario.SpanLength);
            node.Origin = new ParsedOrigin(sourceTexts[i]);

            var (path, _, _) = DiagnosticHelper.ResolveLocation(node);

            if(path != sourceTexts[i].Path)
            {
                return false;
            }
        }

        return true;
    }
}