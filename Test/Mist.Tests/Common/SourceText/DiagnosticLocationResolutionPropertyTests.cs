using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;

namespace Mist.Tests.Common.SourceText;

public class DiagnosticLocationResolutionPropertyTests
{
#pragma warning disable CA1034
    public record LocationInput(Mist.Common.Source.SourceText Source, int SpanStart, int SpanLength)
    {
        public override string ToString()
        {
            return $"LocationInput(Path=\"{Source.Path}\", ContentsLength={Source.Contents.Length}, SpanStart={SpanStart}, SpanLength={SpanLength})";
        }
    }
#pragma warning restore CA1034

    private static Gen<string> GenNonEmptyContents()
    {
        return Gen.Elements(
            "a",
            "hello world",
            "line1\nline2\nline3",
            "class Foo {\n  let x = 1\n  let y = 2\n}",
            "fn main() {\n  print(42)\n}",
            "ab\r\ncd\r\nef",
            "single",
            "a\nb\nc\nd\ne\nf\ng\nh");
    }

    private static Gen<LocationInput> GenLocationInput()
    {
        return from path in Gen.Elements("file.mist", "src/main.mist", "lib/utils.mist", "test.mist", "a/b/c.mist")
               from contents in GenNonEmptyContents()
               from start in Gen.Choose(0, contents.Length - 1)
               from length in Gen.Choose(1, contents.Length - start)
               select new LocationInput(new Mist.Common.Source.SourceText(path, contents), start, length);
    }

    public static Arbitrary<LocationInput> ArbLocationInput()
    {
        return GenLocationInput().ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticLocationResolutionPropertyTests) })]
    public bool ResolveLocation_ReturnsCorrectPath(LocationInput input)
    {
        var node = new BlockNode
        {
            Origin = new ParsedOrigin(input.Source),
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        return DiagnosticHelper.ResolveLocation(node).Location.Path == input.Source.Path;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticLocationResolutionPropertyTests) })]
    public bool ResolveLocation_ReturnsCorrectLinePositionSpan(LocationInput input)
    {
        var node = new BlockNode
        {
            Origin = new ParsedOrigin(input.Source),
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        var location = DiagnosticHelper.ResolveLocation(node).Location.Position();

        var expectedSpan = new SourceSpan(input.SpanStart, input.SpanLength);
        var expectedLocation = input.Source.ToLinePositionSpan(expectedSpan);

        return location.Start.Line == expectedLocation.Start.Line
            && location.Start.Column == expectedLocation.Start.Column
            && location.End.Line == expectedLocation.End.Line
            && location.End.Column == expectedLocation.End.Column;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(DiagnosticLocationResolutionPropertyTests) })]
    public bool ResolveLocation_ParsedOrigin_HasNullSyntheticContext(LocationInput input)
    {
        var node = new BlockNode
        {
            Origin = new ParsedOrigin(input.Source),
            Span = new Storm.Runtime.SourceSpan(input.SpanStart, input.SpanLength)
        };

        var resolved = DiagnosticHelper.ResolveLocation(node);

        return resolved.Frames.IsEmpty && resolved.Location is SourceRange { File.Path: var path } && path == input.Source.Path;
    }
}