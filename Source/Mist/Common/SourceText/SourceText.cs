namespace Mist.Common.Text;

/// <summary>
/// Represents the source text of a single compilation unit, with efficient
/// offset-to-line/column conversion via a lazily-cached line-start array.
/// </summary>
public sealed class SourceText
{
    private int[]? _lineStarts;

    public SourceText(string path, string contents)
    {
        Path = path;
        Contents = contents;
    }

    /// <summary>
    /// The file path of this source text.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The full contents of this source text.
    /// </summary>
    public string Contents { get; }

    /// <summary>
    /// Returns the characters covered by the given <paramref name="span"/>.
    /// </summary>
    public ReadOnlySpan<char> GetSpan(SourceSpan span)
    {
        return Contents.AsSpan(span.Start, span.Length);
    }

    /// <summary>
    /// Converts an offset-based <see cref="SourceSpan"/> to a 1-based <see cref="LinePositionSpan"/>.
    /// </summary>
    public LinePositionSpan ToLinePositionSpan(SourceSpan span)
    {
        var lineStarts = GetLineStarts();
        var start = OffsetToLinePosition(lineStarts, span.Start);
        var end = OffsetToLinePosition(lineStarts, span.End);
        return new LinePositionSpan(start, end);
    }

    private int[] GetLineStarts()
    {
        if(_lineStarts is not null)
        {
            return _lineStarts;
        }

        var starts = new List<int> { 0 };
        for(var i = 0; i < Contents.Length; i++)
        {
            if(Contents[i] == '\n')
            {
                starts.Add(i + 1);
            }
            else if(Contents[i] == '\r')
            {
                if(i + 1 < Contents.Length && Contents[i + 1] == '\n')
                {
                    starts.Add(i + 2);
                    i++; // skip the \n
                }
                else
                {
                    starts.Add(i + 1);
                }
            }
        }

        _lineStarts = starts.ToArray();
        return _lineStarts;
    }

    private static LinePosition OffsetToLinePosition(int[] lineStarts, int offset)
    {
        // Binary search for the line containing this offset.
        var lo = 0;
        var hi = lineStarts.Length - 1;
        while(lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if(lineStarts[mid] <= offset)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        var line = hi; // 0-based line index
        var column = offset - lineStarts[line];
        return new LinePosition(line + 1, column + 1); // 1-based
    }
}