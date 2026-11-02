using System.Collections.Immutable;
using System.Text;
using Mist.Common.Source;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal sealed record SourcePiece(SourceText Source, SourceSpan Span)
{
    public string Text => Source.Contents.Substring(Span.Start, Span.Length);
}

internal readonly record struct MistPiece(string? Text, SourcePiece? Copied)
{
    public static implicit operator MistPiece(string text)
    {
        return new MistPiece(text, null);
    }

    public static implicit operator MistPiece(SourcePiece copied)
    {
        return new MistPiece(null, copied);
    }
}

internal sealed class MistWriter
{
    private readonly StringBuilder text = new();
    private readonly ImmutableArray<OriginSegment>.Builder segments = ImmutableArray.CreateBuilder<OriginSegment>();

    public void Verbatim(SourceText original, SourceSpan span)
    {
        if(span.Length == 0)
        {
            return;
        }

        segments.Add(new OriginSegment(text.Length, span.Length, original, span.Start, span.Length, Verbatim: true));
        _ = text.Append(original.Contents, span.Start, span.Length);
    }

    public void Synthesized(string value, SourceText original, SourceSpan construct)
    {
        if(value.Length == 0)
        {
            return;
        }

        if(segments.Count > 0 && segments[^1] is { Verbatim: false } last && last.GeneratedEnd == text.Length
            && ReferenceEquals(last.Original, original) && last.OriginalStart == construct.Start && last.OriginalLength == construct.Length)
        {
            segments[^1] = last with { Length = last.Length + value.Length };
        }
        else
        {
            segments.Add(new OriginSegment(text.Length, value.Length, original, construct.Start, construct.Length, Verbatim: false));
        }

        _ = text.Append(value);
    }

    public void Write(SourceText original, SourceSpan construct, params ReadOnlySpan<MistPiece> pieces)
    {
        foreach(var piece in pieces)
        {
            if(piece.Copied is { } copied)
            {
                Verbatim(copied.Source, copied.Span);
            }
            else
            {
                Synthesized(piece.Text ?? "", original, construct);
            }
        }
    }

    public SourceText ToSourceText(string path)
    {
        var contents = text.ToString();
        return new SourceText(path, contents) { Origin = segments.Count == 0 ? null : new OriginMap(segments.ToImmutable()) };
    }
}