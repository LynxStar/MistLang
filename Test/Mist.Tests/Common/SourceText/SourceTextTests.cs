using Mist.Common.Text;

namespace Mist.Tests.Common.SourceText;

public class SourceTextTests
{
    [Fact]
    public void GetSpan_ReturnCorrectSlice()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "hello world");
        var span = new SourceSpan(6, 5);

        var result = source.GetSpan(span);

        Assert.Equal("world", result.ToString());
    }

    [Fact]
    public void GetSpan_EmptySpan_ReturnsEmpty()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "hello");
        var span = new SourceSpan(0, 0);

        var result = source.GetSpan(span);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void GetSpan_EntireContents()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "abc");
        var span = new SourceSpan(0, 3);

        var result = source.GetSpan(span);

        Assert.Equal("abc", result.ToString());
    }

    [Fact]
    public void ToLinePositionSpan_SingleLine()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "hello world");
        var span = new SourceSpan(6, 5);

        var lps = source.ToLinePositionSpan(span);

        Assert.Equal(new LinePosition(1, 7), lps.Start);
        Assert.Equal(new LinePosition(1, 12), lps.End);
    }

    [Fact]
    public void ToLinePositionSpan_MultiLine()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "line1\nline2\nline3");
        // "line2" starts at offset 6, length 5
        var span = new SourceSpan(6, 5);

        var lps = source.ToLinePositionSpan(span);

        Assert.Equal(new LinePosition(2, 1), lps.Start);
        Assert.Equal(new LinePosition(2, 6), lps.End);
    }

    [Fact]
    public void ToLinePositionSpan_SpanCrossesLines()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "ab\ncd\nef");
        // From 'b' (offset 1), length 3 => "b\nc", end offset = 4 which is 'd' at line 2, col 2
        var span = new SourceSpan(1, 3);

        var lps = source.ToLinePositionSpan(span);

        Assert.Equal(new LinePosition(1, 2), lps.Start);
        Assert.Equal(new LinePosition(2, 2), lps.End);
    }

    [Fact]
    public void ToLinePositionSpan_EmptyInput()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "");
        var span = new SourceSpan(0, 0);

        var lps = source.ToLinePositionSpan(span);

        Assert.Equal(new LinePosition(1, 1), lps.Start);
        Assert.Equal(new LinePosition(1, 1), lps.End);
    }

    [Fact]
    public void ToLinePositionSpan_SpanAtEndOfFile()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "abc\ndef");
        // Last char 'f' at offset 6, length 1
        var span = new SourceSpan(6, 1);

        var lps = source.ToLinePositionSpan(span);

        Assert.Equal(new LinePosition(2, 3), lps.Start);
        Assert.Equal(new LinePosition(2, 4), lps.End);
    }

    [Fact]
    public void ToLinePositionSpan_WindowsLineEndings()
    {
        var source = new Mist.Common.Text.SourceText("test.mist", "ab\r\ncd");
        // "cd" starts at offset 4, length 2
        var span = new SourceSpan(4, 2);

        var lps = source.ToLinePositionSpan(span);

        Assert.Equal(new LinePosition(2, 1), lps.Start);
        Assert.Equal(new LinePosition(2, 3), lps.End);
    }

    [Fact]
    public void SourceSpan_End_IsStartPlusLength()
    {
        var span = new SourceSpan(10, 5);
        Assert.Equal(15, span.End);
    }
}
