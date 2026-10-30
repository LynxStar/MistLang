using Mist.Common.Diagnostics;
using Mist.Common.Source;

namespace Mist.Lexing;

/// <summary>
/// Places a Storm runtime entry, which carries only a line and a column, as a point in the text
/// it was lexed or parsed from. The offset is recovered the way Storm counts: a line ends at a
/// line feed and nothing else, so the point lands on the character Storm meant even in a file
/// with carriage returns.
/// </summary>
internal static class StormEntryLocation
{
    public static Location Of(SourceText source, string? entryPath, int line, int column)
    {
        var path = entryPath ?? source.Path;
        if(line <= 0 || column <= 0)
        {
            return new FileOnly(path);
        }

        var offset = 0;
        for(var remaining = line - 1; remaining > 0; remaining--)
        {
            offset = source.Contents.IndexOf('\n', offset);
            if(offset < 0)
            {
                return new FileOnly(path);
            }

            offset++;
        }

        offset += column - 1;
        if(offset > source.Contents.Length || !string.Equals(path, source.Path, StringComparison.Ordinal))
        {
            return new FileOnly(path);
        }

        return new SourcePoint(new SourceIdentity(source), offset);
    }
}