using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Driver;

namespace Mist.Tests.Driver;

public class SyntaxTreeMetricsPropertyTests
{
#pragma warning disable CA1034 // Nested type is a test fixture used by FsCheck Arbitrary.
    public sealed class TestNode : Storm.Runtime.SyntaxNode
    {
        private readonly Storm.Runtime.SyntaxNode[] children;

        public TestNode(Storm.Runtime.SyntaxNode[] children)
        {
            this.children = children;
        }

        public override IEnumerable<Storm.Runtime.SyntaxNode> Children()
        {
            return children;
        }
    }
#pragma warning restore CA1034

    private static Gen<TestNode> GenTree(int maxDepth)
    {
        if(maxDepth <= 0)
        {
            return Gen.Constant(new TestNode(Array.Empty<Storm.Runtime.SyntaxNode>()));
        }

        return Gen.Choose(0, 4).SelectMany(childCount =>
            Gen.ArrayOf(GenTree(maxDepth - 1), childCount)
                .Select(children => new TestNode(children)));
    }

    public static Arbitrary<TestNode> ArbTestNode()
    {
        return GenTree(5).ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SyntaxTreeMetricsPropertyTests) })]
    public bool CountNodes_EqualsOnePlusSumOfChildCounts(TestNode root)
    {
        var actual = SyntaxTreeMetrics.CountNodes(root);
        var expected = 1 + root.Children().Sum(SyntaxTreeMetrics.CountNodes);
        return actual == expected;
    }
}