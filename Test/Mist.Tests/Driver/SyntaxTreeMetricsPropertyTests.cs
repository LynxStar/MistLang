// Feature: compiler-progress-output, Property 1: AST node count is recursive sum
// **Validates: Requirements 5.2**
//
// Property-based test verifying that CountNodes(root) == 1 + sum(CountNodes(child))
// for arbitrary tree structures. This exercises the recursive node-counting logic
// in SyntaxTreeMetrics without requiring real parsed ASTs.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Driver;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for <see cref="SyntaxTreeMetrics.CountNodes"/>.
/// Generates arbitrary tree structures and verifies the recursive sum property.
/// </summary>
public class SyntaxTreeMetricsPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Test node implementation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Concrete <see cref="Storm.Runtime.SyntaxNode"/> for testing purposes.
    /// Holds a fixed list of children and returns them from <see cref="Children"/>.
    /// </summary>
#pragma warning disable CA1034 // Nested type is a test fixture used by FsCheck Arbitrary.
    public sealed class TestNode : Storm.Runtime.SyntaxNode
    {
        private readonly Storm.Runtime.SyntaxNode[] children;

        public TestNode(Storm.Runtime.SyntaxNode[] children)
        {
            this.children = children;
        }

        public override IEnumerable<Storm.Runtime.SyntaxNode> Children() => children;
    }
#pragma warning restore CA1034

    // ═══════════════════════════════════════════════════════════════════════
    // Arbitrary / Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates arbitrary tree structures with bounded depth to avoid
    /// stack overflow. Max depth is 5, max branching factor is 4.
    /// </summary>
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

    public static Arbitrary<TestNode> ArbTestNode() =>
        GenTree(5).ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property test
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1: AST node count is recursive sum.
    /// For any tree, CountNodes(root) == 1 + sum(CountNodes(child) for each child).
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(SyntaxTreeMetricsPropertyTests) })]
    public bool CountNodes_EqualsOnePlusSumOfChildCounts(TestNode root)
    {
        var actual = SyntaxTreeMetrics.CountNodes(root);
        var expected = 1 + root.Children().Sum(child => SyntaxTreeMetrics.CountNodes(child));
        return actual == expected;
    }
}