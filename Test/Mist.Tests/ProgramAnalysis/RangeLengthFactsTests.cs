// Tier 1: enumerate concrete runtime slices to check conservative interval lengths and endpoint classifications.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RangeLengthFactsTests
{
    [Fact]
    public void NumericFacts_DoNotDescribeBooleanRealOrUserIndexConversions()
    {
        IMistType[] types = [
            new StructType("Boolean", "Standard", null, null, null),
            new StructType("Single", "Standard", null, null, null),
            new StructType("Double", "Standard", null, null, null),
            new StructType("Decimal", "Standard", null, null, null),
            new StructType("Index", "Standard", null, null, null),
            new ClassType("Counter", "Probe", null, null, null),
        ];

        foreach(var type in types)
        {
            var local = new LocalReferenceExpression(null!, new LocalVariable("end", type));

            Assert.Null(RangeBound.Of(local, _ => Admitted.Exactly(3)));
            Assert.Null(RangeBound.Of(new LiteralExpression(null!, 3L, type)));
        }
    }

    [Fact]
    public void IntegralValueDomains_PreserveBothOffsetMarkersThroughWrappers()
    {
        foreach(var name in new[] { "Int32", "Int64", "Int16", "UInt32", "UInt64", "UInt16", "Byte", "SByte", "Char" })
        {
            var integer = new StructType(name, "Standard", null, null, null);
            var alias = new TypeAlias("Offset", "Probe", null, null, null!) { AliasedType = integer };
            IMistType[] types = [integer, alias, new RefinedType(alias, null!, "PositiveOffset"), new LiftedType(alias, 3L), new UnknownValueType(alias), new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = integer }];

            foreach(var type in types)
            {
                var local = new LocalReferenceExpression(null!, new LocalVariable("end", type));
                var fromEnd = new UnaryExpression(null!, UnaryOperatorKind.FromEnd, local, new StructType("Index", "Standard", null, null, null));

                Assert.Equal(new RangeBound(Admitted.Exactly(3), null, false), RangeBound.Of(local, _ => Admitted.Exactly(3)));
                Assert.Equal(new RangeBound(Admitted.Exactly(3), null, true), RangeBound.Of(fromEnd, _ => Admitted.Exactly(3)));
            }
        }
    }

    [Fact]
    public void UnresolvedAndCyclicDomains_DoNotCaptureOffsets()
    {
        var alias = new TypeAlias("Offset", "Probe", null, null, null!);
        var dependent = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true };

        Assert.Null(RangeBound.Of(new LocalReferenceExpression(null!, new LocalVariable("end", alias)), _ => Admitted.Exactly(3)));
        Assert.Null(RangeBound.Of(new LocalReferenceExpression(null!, new LocalVariable("end", dependent)), _ => Admitted.Exactly(3)));

        alias.AliasedType = dependent;
        dependent.Domain = alias;

        Assert.Null(RangeBound.Of(new LocalReferenceExpression(null!, new LocalVariable("end", alias)), _ => Admitted.Exactly(3)));
    }

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