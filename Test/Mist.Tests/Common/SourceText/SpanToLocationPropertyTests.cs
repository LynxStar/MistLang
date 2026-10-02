// Feature: unified-diagnostic-formatting, Property 5: span-to-location resolution
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Source;

namespace Mist.Tests.Common.SourceText;

/// <summary>
/// Data record representing a random source text and a span within its bounds.
/// </summary>
public record SourceTextWithSpan(string Path, string Contents, int SpanStart, int SpanLength);

/// <summary>
/// Property-based tests for span-to-location resolution.
///
/// **Validates: Requirements 4.1, 4.2, 4.3**
///
/// Property 5: For any SourceText with non-empty content and any SourceSpan within that
/// content with non-zero length, the LinePositionSpan produced by SourceText.ToLinePositionSpan()
/// SHALL have Start.Line >= 1 and Start.Column >= 1.
/// </summary>
[Trait("Tier", "1")]
public class SpanToLocationPropertyTests
{
    /// <summary>
    /// Generates random SourceText content with a valid SourceSpan within bounds.
    /// Content includes a mix of characters and newlines to exercise line/column computation.
    /// </summary>
    public static Arbitrary<SourceTextWithSpan> ArbitrarySourceTextWithSpan()
    {
        var gen =
            from path in Gen.Elements("test.mist", "src/main.mist", "lib/utils.mist", "foo/bar.mist")
            from contentParts in Gen.ListOf(
                Gen.Frequency(
                    (5, Gen.Elements("a", "bb", "ccc", "x", "123", " ", "\t")),
                    (2, Gen.Constant("\n")),
                    (1, Gen.Constant("\r\n"))))
            let contents = string.Concat(contentParts)
            where contents.Length > 0
            from spanStart in Gen.Choose(0, contents.Length - 1)
            from spanLength in Gen.Choose(1, contents.Length - spanStart)
            where spanLength > 0
            select new SourceTextWithSpan(path, contents, spanStart, spanLength);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 5: ToLinePositionSpan produces 1-based line and column positions.
    ///
    /// For any SourceText with non-empty content and any SourceSpan within bounds
    /// with non-zero length, Start.Line >= 1 and Start.Column >= 1.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanToLocationPropertyTests) })]
    public bool SpanToLocation_ProducesOneBasedPositions(SourceTextWithSpan data)
    {
        var sourceText = new Mist.Common.Source.SourceText(data.Path, data.Contents);
        var span = new SourceSpan(data.SpanStart, data.SpanLength);

        var linePositionSpan = sourceText.ToLinePositionSpan(span);

        return linePositionSpan.Start.Line >= 1 && linePositionSpan.Start.Column >= 1;
    }

    /// <summary>
    /// Property 5: End position is also 1-based and >= Start position.
    ///
    /// For any valid span within source text, the End line/column should also
    /// be 1-based and the end position should be at or after the start position.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanToLocationPropertyTests) })]
    public bool SpanToLocation_EndPositionIsValid(SourceTextWithSpan data)
    {
        var sourceText = new Mist.Common.Source.SourceText(data.Path, data.Contents);
        var span = new SourceSpan(data.SpanStart, data.SpanLength);

        var linePositionSpan = sourceText.ToLinePositionSpan(span);

        // End must also be 1-based
        if(linePositionSpan.End.Line < 1 || linePositionSpan.End.Column < 1)
        {
            return false;
        }

        // End line must be >= Start line
        if(linePositionSpan.End.Line < linePositionSpan.Start.Line)
        {
            return false;
        }

        // If on the same line, End column must be >= Start column
        if(linePositionSpan.End.Line == linePositionSpan.Start.Line
            && linePositionSpan.End.Column < linePositionSpan.Start.Column)
        {
            return false;
        }

        return true;
    }
}