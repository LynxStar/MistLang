namespace Mist.Common.Source;

public sealed class SourceText
{
    private int[]? lineStarts;

    public SourceText(string path, string contents)
    {
        Path = path;
        Contents = contents;
    }

    public string Path { get; }

    public string Contents { get; }

    public string ContentHash => field ??= Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Contents)));

    public ReadOnlySpan<char> GetSpan(SourceSpan span)
    {
        return Contents.AsSpan(span.Start, span.Length);
    }

    public LinePositionSpan ToLinePositionSpan(SourceSpan span)
    {
        var lineStarts = GetLineStarts();
        var start = OffsetToLinePosition(lineStarts, span.Start);
        var end = OffsetToLinePosition(lineStarts, span.End);
        return new LinePositionSpan(start, end);
    }

    private int[] GetLineStarts()
    {
        if(lineStarts is not null)
        {
            return lineStarts;
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
                    i++;
                }
                else
                {
                    starts.Add(i + 1);
                }
            }
        }

        lineStarts = starts.ToArray();
        return lineStarts;
    }

    private static LinePosition OffsetToLinePosition(int[] lineStarts, int offset)
    {
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

        var line = hi;
        var column = offset - lineStarts[line];
        return new LinePosition(line + 1, column + 1);
    }
}