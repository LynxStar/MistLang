using System.Collections.Immutable;
using Mist.Common.Diagnostics;

namespace Mist.Common.Source;

/// <summary>
/// Which original text each range of a translated text came from, as contiguous segments from
/// offset zero. A verbatim segment shifts an offset into its original; a synthesized one answers
/// with the span of the construct it was written for, whatever the offset inside it.
/// </summary>
public sealed class OriginMap
{
    public OriginMap(ImmutableArray<OriginSegment> segments)
    {
        var end = 0;
        foreach(var segment in segments)
        {
            if(Fault(segment, end) is { } fault)
            {
                throw new ArgumentException(fault, nameof(segments));
            }

            end = segment.GeneratedEnd;
        }

        Segments = segments;
        Length = end;
    }

    public ImmutableArray<OriginSegment> Segments { get; }

    public int Length { get; }

    public SourceRange Map(int generatedOffset, int length)
    {
        if(Segments.IsEmpty || generatedOffset < 0 || length < 0 || (long)generatedOffset + length > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(generatedOffset),
                $"span [{generatedOffset}, +{length}) does not fit a translated text of {Length} characters");
        }

        var segment = Segments[IndexAt(generatedOffset)];
        var original = new SourceIdentity(segment.Original);
        if(!segment.Verbatim)
        {
            return new SourceRange(original, segment.OriginalStart, segment.OriginalLength);
        }

        var shift = generatedOffset - segment.GeneratedStart;
        return new SourceRange(original, segment.OriginalStart + shift, Math.Min(length, segment.Length - shift));
    }

    public bool Covers(string generated)
    {
        return generated.Length == Length && Segments.All(segment => !segment.Verbatim
            || generated.AsSpan(segment.GeneratedStart, segment.Length).SequenceEqual(
                segment.Original.Contents.AsSpan(segment.OriginalStart, segment.OriginalLength)));
    }

    private int IndexAt(int generatedOffset)
    {
        var low = 0;
        var high = Segments.Length - 1;
        while(low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if(Segments[middle].GeneratedStart <= generatedOffset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private static string? Fault(OriginSegment segment, int expectedStart)
    {
        if(segment.GeneratedStart != expectedStart || segment.Length <= 0)
        {
            return $"segment [{segment.GeneratedStart}, +{segment.Length}) does not continue the map at {expectedStart}";
        }

        if(segment.OriginalStart < 0 || segment.OriginalLength < 0
            || (long)segment.OriginalStart + segment.OriginalLength > segment.Original.Contents.Length)
        {
            return $"original span [{segment.OriginalStart}, +{segment.OriginalLength}) does not fit '{segment.Original.Path}'";
        }

        return segment.Verbatim && segment.OriginalLength != segment.Length
            ? $"verbatim segment at {segment.GeneratedStart} copies {segment.OriginalLength} characters into {segment.Length}"
            : null;
    }
}