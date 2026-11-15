using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Spans are offsets within one file, so files routinely overlap numerically; only the node's Origin picks the file.
/// </summary>
public class OverlappingSpanResolutionPropertyTests
{
    private static string MakePath(int index)
    {
        return $"src/file{index}.mist";
    }

    private static string MakeContent(int minLength)
    {
        var line = "let x = 42\n";
        var repetitions = minLength / line.Length + 2;
        return string.Concat(Enumerable.Repeat(line, repetitions));
    }

#pragma warning disable CA1034
    public record OverlappingScenario(int FileCount, int TargetFileIndex, int SpanStart, int SpanLength)
    {
        public override string ToString()
        {
            return $"OverlappingScenario(Files={FileCount}, Target={TargetFileIndex}, Span=[{SpanStart},{SpanStart + SpanLength}))";
        }
    }
#pragma warning restore CA1034

    private static Gen<OverlappingScenario> GenOverlappingScenario()
    {
        return from fileCount in Gen.Choose(2, 5)
               from spanStart in Gen.Choose(0, 20)
               from spanLength in Gen.Choose(1, 15)
               from targetIndex in Gen.Choose(0, fileCount - 1)
               select new OverlappingScenario(fileCount, targetIndex, spanStart, spanLength);
    }

    public static Arbitrary<OverlappingScenario> ArbOverlappingScenario()
    {
        return GenOverlappingScenario().ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverlappingSpanResolutionPropertyTests) })]
    public bool ResolveLocation_WithOverlappingSpans_ReturnsCorrectFilePath(OverlappingScenario scenario)
    {
        var minContentLength = scenario.SpanStart + scenario.SpanLength + 1;
        var sourceTexts = Enumerable.Range(0, scenario.FileCount)
            .Select(i => new Mist.Common.Source.SourceText(MakePath(i), MakeContent(minContentLength)))
            .ToArray();

        var node = new BlockNode
        {
            Span = new Storm.Runtime.SourceSpan(scenario.SpanStart, scenario.SpanLength)
        };

        var targetSource = sourceTexts[scenario.TargetFileIndex];
        node.Origin = new ParsedOrigin(targetSource);

        return DiagnosticHelper.ResolveLocation(node).Location.Path == targetSource.Path;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(OverlappingSpanResolutionPropertyTests) })]
    public bool ResolveLocation_EachFileResolvesIndependently(OverlappingScenario scenario)
    {
        var minContentLength = scenario.SpanStart + scenario.SpanLength + 1;
        var content = MakeContent(minContentLength);
        var sourceTexts = Enumerable.Range(0, scenario.FileCount)
            .Select(i => new Mist.Common.Source.SourceText(MakePath(i), content))
            .ToArray();

        for(var i = 0; i < scenario.FileCount; i++)
        {
            var node = new BlockNode
            {
                Span = new Storm.Runtime.SourceSpan(scenario.SpanStart, scenario.SpanLength),
                Origin = new ParsedOrigin(sourceTexts[i])
            };

            if(DiagnosticHelper.ResolveLocation(node).Location.Path != sourceTexts[i].Path)
            {
                return false;
            }
        }

        return true;
    }
}