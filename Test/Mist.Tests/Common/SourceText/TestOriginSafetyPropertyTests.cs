using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Storm.Runtime;
using SourceSpan = Storm.Runtime.SourceSpan;

namespace Mist.Tests.Common.SourceText;

public class TestOriginSafetyPropertyTests
{
    private static Gen<SourceSpan> GenSourceSpan()
    {
        var genNormal = from start in Gen.Choose(0, 10000)
                        from length in Gen.Choose(0, 10000)
                        select new SourceSpan(start, length);

        var genZeroLength = from start in Gen.Choose(0, int.MaxValue / 2)
                            select new SourceSpan(start, 0);

        var genLargeOffset = from start in Gen.Choose(100000, int.MaxValue / 2)
                             from length in Gen.Choose(0, 1000)
                             select new SourceSpan(start, length);

        var genMaxInt = Gen.Elements(
            new SourceSpan(int.MaxValue, 0),
            new SourceSpan(int.MaxValue, 1),
            new SourceSpan(0, int.MaxValue),
            new SourceSpan(int.MaxValue, int.MaxValue),
            new SourceSpan(int.MaxValue / 2, int.MaxValue / 2));

        return Gen.OneOf(genNormal, genZeroLength, genLargeOffset, genMaxInt);
    }

    private static Gen<SyntaxNode> GenSyntaxNode()
    {
        return Gen.OneOf(
            Gen.Constant<SyntaxNode>(new BlockNode()),
            Gen.Constant<SyntaxNode>(new IntTypePrimitiveTypeNode()),
            Gen.Constant<SyntaxNode>(new CompilationUnitNode()));
    }

    public static Arbitrary<SourceSpan> ArbSourceSpan()
    {
        return GenSourceSpan().ToArbitrary();
    }

    public static Arbitrary<SyntaxNode> ArbSyntaxNode()
    {
        return GenSyntaxNode().ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TestOriginSafetyPropertyTests) })]
    public bool TestOrigin_ResolveLocation_NeverThrows_ReturnsUnknown(SyntaxNode node, SourceSpan span)
    {
        node.Origin = TestOrigin.Instance;
        node.Span = span;

        var resolved = DiagnosticHelper.ResolveLocation(node);

        return resolved.Location is Absent { Reason: AbsenceReason.TestOrigin }
            && resolved.Frames.IsEmpty;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TestOriginSafetyPropertyTests) })]
    public bool NullOrigin_ResolveLocation_NeverThrows_ReturnsUnknown(SyntaxNode node, SourceSpan span)
    {
        node.Origin = null;
        node.Span = span;

        var resolved = DiagnosticHelper.ResolveLocation(node);

        return resolved.Location is Absent { Reason: AbsenceReason.NotYetAttached }
            && resolved.Frames.IsEmpty;
    }
}