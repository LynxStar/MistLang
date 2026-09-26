// Tier 1: enumerate concrete runtime slices to check conservative interval lengths and endpoint classifications.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RangeLengthFactsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AUserRangeIndexer_DoesNotInheritIntrinsicSliceLengths(bool stored)
    {
        var integer = new StructType("Int32", "Standard", null, null, null);
        var rangeType = new StructType("Range", "Standard", null, null, null);
        var custom = new ClassType("Custom", "Probe", null, null, null);
        var array = new ConstructedType(new ClassType("Array<,>", "Standard", null, null, null), [integer, new UnknownValueType(integer)]);
        var range = new RangeExpression(null!, new LiteralExpression(null!, 0L, integer), new LiteralExpression(null!, 3L, integer), rangeType);
        var index = new IndexExpression(
            null!, new LiteralExpression(null!, null, custom), stored ? new LocalReferenceExpression(null!, new LocalVariable("window", rangeType)) : range, array);

        Assert.Null(Lengths.Of(index, _ => null, _ => StoredRange.Of(range)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AbstractRangeFacts_ContainEverySuccessfulConcreteSlice(bool startFromEnd, bool endFromEnd)
    {
        for(var minimumLength = 0; minimumLength <= 6; minimumLength++)
        {
            for(var lowStart = -1; lowStart <= 6; lowStart++)
            {
                for(var lowEnd = -1; lowEnd <= 6; lowEnd++)
                {
                    var range = new StoredRange(
                        new RangeBound(Admitted.Between(lowStart, lowStart + 1), null, startFromEnd),
                        new RangeBound(Admitted.Between(lowEnd, lowEnd + 1), null, endFromEnd));
                    var lengths = Admitted.Between(minimumLength, minimumLength + 1);
                    var safety = Lengths.SafetyOf(range, lengths);
                    var inferred = Lengths.OfSlice(range, lengths);
                    var withoutReceiver = Lengths.OfSlice(range, source: null);

                    for(var length = minimumLength; length <= minimumLength + 1; length++)
                    {
                        for(var startOffset = lowStart; startOffset <= lowStart + 1; startOffset++)
                        {
                            for(var endOffset = lowEnd; endOffset <= lowEnd + 1; endOffset++)
                            {
                                var start = startFromEnd ? length - startOffset : startOffset;
                                var end = endFromEnd ? length - endOffset : endOffset;
                                var succeeds = startOffset >= 0 && endOffset >= 0 && start >= 0 && end <= length && start <= end;

                                Assert.True(safety != RangeSafety.Proven || succeeds);
                                Assert.True(safety != RangeSafety.Violated || !succeeds);

                                if(succeeds)
                                {
                                    Assert.NotNull(inferred);
                                    Assert.True(inferred.Contains(end - start));
                                    Assert.True(withoutReceiver is null || withoutReceiver.Contains(end - start));
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}