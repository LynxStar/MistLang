using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Source;

namespace Mist.Tests.Common.SourceText;

public record SourceTextWithSpan(string Path, string Contents, int SpanStart, int SpanLength);

[Trait("Tier", "1")]
public class SpanToLocationPropertyTests
{
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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanToLocationPropertyTests) })]
    public bool SpanToLocation_ProducesOneBasedPositions(SourceTextWithSpan data)
    {
        var sourceText = new Mist.Common.Source.SourceText(data.Path, data.Contents);
        var span = new SourceSpan(data.SpanStart, data.SpanLength);

        var linePositionSpan = sourceText.ToLinePositionSpan(span);

        return linePositionSpan.Start.Line >= 1 && linePositionSpan.Start.Column >= 1;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SpanToLocationPropertyTests) })]
    public bool SpanToLocation_EndPositionIsValid(SourceTextWithSpan data)
    {
        var sourceText = new Mist.Common.Source.SourceText(data.Path, data.Contents);
        var span = new SourceSpan(data.SpanStart, data.SpanLength);

        var linePositionSpan = sourceText.ToLinePositionSpan(span);

        if(linePositionSpan.End.Line < 1 || linePositionSpan.End.Column < 1)
        {
            return false;
        }

        if(linePositionSpan.End.Line < linePositionSpan.Start.Line)
        {
            return false;
        }

        if(linePositionSpan.End.Line == linePositionSpan.Start.Line
            && linePositionSpan.End.Column < linePositionSpan.Start.Column)
        {
            return false;
        }

        return true;
    }
}