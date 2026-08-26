// From-end indices, range expressions, and what an element access yields once the ARGUMENT's type is
// consulted. Asserted on the MODEL because output cannot tell an element from a sub-sequence: a slice
// mistyped as its element type still emits `numbers[1..3]`, which Roslyn resolves to the right thing on
// its own, so a passing program says nothing about what Mist decided. Orders 123 and 124 of
// backlog/emitter-v2-capability-progression.yaml rest on this file.

using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class RangeAndIndexResolutionTests : ProgramModelProbe
{
    public RangeAndIndexResolutionTests()
        : base("MistRangeIndex_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Greeter
        {
            public string Name { get; set; }
        }
        """;

    protected override string Preamble { get; } = """
        int[] numbers = [10, 20, 30, 40, 50];
        string word = "hello";
        Greeter g = new Greeter();
        """;

    [Fact]
    public void AFromEndIndexIsAnIndex_NotTheIntegerItWraps()
    {
        var fromEnd = InitializerOf<UnaryExpression>("var i = ^1;");

        Assert.Equal(UnaryOperatorKind.FromEnd, fromEnd.OperatorKind);
        Assert.Equal("Standard.Index", fromEnd.Type.FullyQualifiedName);
        Assert.Equal("Standard.Int32", fromEnd.Operand.Type.FullyQualifiedName);
    }

    [Fact]
    public void ARangeIsARange()
    {
        var range = InitializerOf<RangeExpression>("var r = 1..3;");

        Assert.Equal("Standard.Range", range.Type.FullyQualifiedName);
        Assert.NotNull(range.Start);
        Assert.NotNull(range.End);
    }

    /// <summary>
    /// An omitted bound is null rather than a substituted edge value, because C# spells it by writing
    /// nothing and the two are different code. All four forms are asserted together so a change that
    /// collapses one into another cannot pass by satisfying the others.
    /// </summary>
    [Fact]
    public void EachBoundIsIndependentlyOmittable()
    {
        Assert.Equal((true, true), Bounds("var r = 1..3;"));
        Assert.Equal((false, true), Bounds("var r = ..3;"));
        Assert.Equal((true, false), Bounds("var r = 1..;"));
        Assert.Equal((false, false), Bounds("var r = ..;"));
    }

    [Fact]
    public void AFromEndBoundNestsInsideARange()
    {
        var range = InitializerOf<RangeExpression>("var r = 1..^1;");

        var end = Assert.IsType<UnaryExpression>(range.End);
        Assert.Equal(UnaryOperatorKind.FromEnd, end.OperatorKind);
        Assert.Equal("Standard.Index", end.Type.FullyQualifiedName);
    }

    [Fact]
    public void IndexingAnArrayYieldsItsElement()
    {
        Assert.Equal("Standard.Int32", ElementAccess("var x = numbers[0];").Type.FullyQualifiedName);
        Assert.Equal("Standard.Int32", ElementAccess("var x = numbers[^1];").Type.FullyQualifiedName);
    }

    /// <summary>
    /// The claim order 124 exists for: the SAME receiver and the SAME indexer yield different types
    /// according to what the argument is. Both halves are asserted because a slice typed as its element
    /// type is the exact failure the model must rule out, and only the pair distinguishes it.
    /// </summary>
    [Fact]
    public void SlicingAnArrayYieldsAnArray_NotItsElement()
    {
        var sliced = ElementAccess("var x = numbers[1..3];");
        var constructed = Assert.IsType<ConstructedType>(sliced.Type);

        Assert.Equal("Standard.Array<,>", constructed.Definition.FullyQualifiedName);
        Assert.Equal("Standard.Int32", constructed.TypeArguments[0].FullyQualifiedName);
    }

    [Fact]
    public void SlicingASlicedArrayStillYieldsAnIndexableArray()
    {
        var sliced = ElementAccess("var x = numbers[1..3][^1];");

        Assert.Equal("Standard.Int32", sliced.Type.FullyQualifiedName);
    }

    [Fact]
    public void SlicingAStringYieldsAString()
    {
        var sliced = ElementAccess("var x = word[1..3];");

        Assert.Equal("Standard.String", sliced.Type.FullyQualifiedName);
    }

    [Fact]
    public void SlicingAReceiverThatYieldsNoSubsequenceIsRefused()
    {
        var diagnostics = BuildRun("var bad = g[1..2];");

        _ = Assert.Single(Errors(diagnostics, "MIST_PM_RECEIVER_NOT_SLICEABLE"));
    }

    /// <summary>
    /// An INDEX that resolves nothing stays silent where a range does not. The asymmetry is deliberate:
    /// a missing indexer is a member that might simply not be declared, while a range that resolves
    /// nothing has no C# to write at all.
    /// </summary>
    [Fact]
    public void IndexingAReceiverWithNoIndexerReportsNothing()
    {
        var diagnostics = BuildRun("var bad = g[1];");

        Assert.Empty(Errors(diagnostics, "MIST_PM_RECEIVER_NOT_SLICEABLE"));
    }

    private (bool HasStart, bool HasEnd) Bounds(string statement)
    {
        var range = InitializerOf<RangeExpression>(statement);
        return (range.Start is not null, range.End is not null);
    }

    private IndexExpression ElementAccess(string statement)
    {
        return InitializerOf<IndexExpression>(statement);
    }
}