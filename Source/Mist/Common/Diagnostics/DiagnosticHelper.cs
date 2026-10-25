using System.Collections.Immutable;
using Mist.Common.Source;

namespace Mist.Common.Diagnostics;

/// <summary>
/// Resolves where a node points by walking its origin chain to a parsed root, and places a range or
/// point a producer built by hand under the same rule. A span is valid only when it starts at or
/// after zero, is not negative in length and ends at or before the end of its text, with the end
/// computed in 64-bit arithmetic so no overflow can fake a fit; anything else resolves to an absent
/// location and a bounds fault for the factory to report.
/// </summary>
public static class DiagnosticHelper
{
    private const int MaxChainDepth = 100;

    public static OriginResolution ResolveLocation(Storm.Runtime.SyntaxNode node, Storm.Runtime.SourceSpan? span = null)
    {
        var sourceSpan = span ?? node.Span;

        return node.Origin switch
        {
            null => new OriginResolution(new Absent(AbsenceReason.NotYetAttached), []),
            TestOrigin => new OriginResolution(new Absent(AbsenceReason.TestOrigin), []),
            ParsedOrigin parsed => Place(parsed.Source, sourceSpan, []),
            SyntheticOrigin synthetic => ResolveSyntheticChain(synthetic, sourceSpan),
            _ => new OriginResolution(new Absent(AbsenceReason.Unavailable), []),
        };
    }

    public static OriginResolution Place(Location location, ImmutableArray<Frame> frames = default)
    {
        var fault = location switch
        {
            SourceRange range => Fault(range.File.Text, range.Offset, range.Length),
            SourcePoint point => Fault(point.File.Text, point.Offset, 0),
            _ => null,
        };

        return fault is null
            ? new OriginResolution(location, frames)
            : new OriginResolution(new Absent(AbsenceReason.OutOfBounds), frames, fault);
    }

    private static OriginResolution ResolveSyntheticChain(SyntheticOrigin synthetic, Storm.Runtime.SourceSpan span)
    {
        var frames = ImmutableArray.CreateBuilder<Frame>();
        var visited = new HashSet<NodeOrigin>(ReferenceEqualityComparer.Instance) { synthetic };
        frames.Add(new SynthesisFrame(synthetic.Reason));
        var current = synthetic.DerivedFrom;

        for(var depth = 1; depth <= MaxChainDepth; depth++)
        {
            switch(current)
            {
                case ParsedOrigin parsed:
                    return Place(parsed.Source, span, frames.ToImmutable());

                case TestOrigin:
                    return new OriginResolution(new Absent(AbsenceReason.TestOrigin), frames.ToImmutable());

                case null:
                    return new OriginResolution(new Absent(AbsenceReason.SyntheticWithoutRoot), frames.ToImmutable());

                case SyntheticOrigin next when !visited.Add(next):
                    frames.Add(new TruncatedFrame(depth, Cyclic: true));
                    return new OriginResolution(new Absent(AbsenceReason.SyntheticWithoutRoot), frames.ToImmutable());

                case SyntheticOrigin next:
                    frames.Add(new SynthesisFrame(next.Reason));
                    current = next.DerivedFrom;
                    break;

                default:
                    return new OriginResolution(new Absent(AbsenceReason.Unavailable), frames.ToImmutable());
            }
        }

        frames.Add(new TruncatedFrame(MaxChainDepth, Cyclic: false));
        return new OriginResolution(new Absent(AbsenceReason.SyntheticWithoutRoot), frames.ToImmutable());
    }

    private static OriginResolution Place(SourceText source, Storm.Runtime.SourceSpan span, ImmutableArray<Frame> frames)
    {
        return Fault(source, span.Start, span.Length) is { } fault
            ? new OriginResolution(new Absent(AbsenceReason.OutOfBounds), frames, fault)
            : new OriginResolution(new SourceRange(new SourceIdentity(source), span.Start, span.Length), frames);
    }

    private static string? Fault(SourceText source, int start, int length)
    {
        var end = (long)start + length;

        return start < 0 || length < 0 || end > source.Contents.Length
            ? $"span [{start}, +{length}) does not fit '{source.Path}' ({source.Contents.Length} characters)"
            : null;
    }
}