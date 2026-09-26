// Tier 1: captured range descriptors preserve markers and semantic member identity while rejecting unsupported sources.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RangeBoundSnapshotTests
{
    private static readonly IMistType Integer = new StructType("Int32", "Standard", null, null, null);
    private static readonly IMistType Index = new StructType("Index", "Standard", null, null, null);
    private static readonly IMistType Holder = new ClassType("Holder", "Probe", null, null, null);

    [Fact]
    public void StoredIndexFacts_UseTheirCapturedMarkerInsteadOfNumericEqualityFacts()
    {
        var local = new LocalReferenceExpression(null!, new LocalVariable("end", Index));
        var captured = new RangeBound(Admitted.Exactly(3), null, true);

        Assert.Same(captured, RangeBound.Of(local, _ => Admitted.Exactly(99), _ => captured));
        Assert.Null(RangeBound.Of(local, _ => Admitted.Exactly(99)));
        Assert.Same(captured, RangeBound.Of(new CloneExpression(null!, local), indexes: _ => captured));
    }

    [Fact]
    public void ASelectedUserUnaryOperator_DoesNotSupplyAFromEndMarker()
    {
        var operand = new LiteralExpression(null!, 3L, Integer);
        var selected = new Operator(OperatorKind.Minus, Index, [new Parameter("offset", Integer)], default);
        var fromEnd = new UnaryExpression(null!, UnaryOperatorKind.FromEnd, operand, Index) { Operator = selected };
        var intrinsic = new UnaryExpression(null!, UnaryOperatorKind.FromEnd, operand, Index)
        {
            Operator = new Operator(OperatorKind.Minus, Index, [new Parameter("offset", Integer)], new MemberModifiers { IsIntrinsic = true }),
        };

        Assert.Null(RangeBound.Of(fromEnd));
        Assert.Equal(new RangeBound(Admitted.Exactly(3), null, true), RangeBound.Of(intrinsic));
    }

    [Fact]
    public void IntegralIndexCreation_UsesFromStartAndUserDomainsRemainUnknown()
    {
        var constant = new LiteralExpression(null!, 3L, Integer);
        var conversion = new ImplicitConversionExpression(null!, constant, Index, []);
        var counter = new LocalReferenceExpression(null!, new LocalVariable("counter", Holder));
        var user = new ImplicitConversionExpression(null!, counter, Index, []);

        Assert.Equal(new RangeBound(Admitted.Exactly(3), null, false), RangeBound.Of(conversion));
        Assert.Null(RangeBound.Of(user, _ => Admitted.Exactly(3), _ => new RangeBound(Admitted.Exactly(3), null, true)));
    }

    [Fact]
    public void IndexDomainWrappers_PreserveTheMarkerWithoutInventingOffsets()
    {
        var alias = new TypeAlias("Edge", "Probe", null, null, null!) { AliasedType = Index };
        IMistType[] types = [Index, alias, new RefinedType(alias, null!, "EdgeRefinement"), new LiftedType(alias, 3L), new UnknownValueType(alias), new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = (MistType)Index }];

        foreach(var type in types)
        {
            var value = new LocalReferenceExpression(null!, new LocalVariable("end", type));

            Assert.True(RangeBound.IsIndex(type));
            Assert.Null(RangeBound.Of(value, _ => Admitted.Exactly(3)));
            Assert.Equal(new RangeBound(Admitted.Exactly(3), null, true), RangeBound.Of(value, indexes: _ => new RangeBound(Admitted.Exactly(3), null, true)));
        }

        alias.AliasedType = alias;

        Assert.False(RangeBound.IsIndex(alias));
    }

    [Fact]
    public void BareAndExplicitThisFieldReads_ShareOnlyTheSameDeclaration()
    {
        var field = new Field("end", Integer, OwnershipKind.Unrestricted, default);
        var other = new Field("end", Integer, OwnershipKind.Unrestricted, default);
        var bare = new FieldReferenceExpression(null!, field);
        var member = FieldRead(new ThisExpression(null!, Holder), field);
        var different = FieldRead(new ThisExpression(null!, Holder), other);

        Assert.Equal(RangeBound.Of(bare, Interval), RangeBound.Of(member, Interval));
        Assert.NotEqual(RangeBound.Of(bare, Interval), RangeBound.Of(different, Interval));
    }

    [Fact]
    public void NonExactMemberCaptures_DistinguishReceiverSymbolsAndDeclarations()
    {
        var field = new Field("end", Integer, OwnershipKind.Unrestricted, default);
        var first = new LocalVariable("holder", Holder);
        var second = new LocalVariable("holder", Holder);
        var fromFirst = FieldRead(new LocalReferenceExpression(null!, first), field);
        var again = FieldRead(new LocalReferenceExpression(null!, first), field);
        var fromSecond = FieldRead(new LocalReferenceExpression(null!, second), field);

        Assert.Equal(RangeBound.Of(fromFirst, Interval), RangeBound.Of(again, Interval));
        Assert.NotEqual(RangeBound.Of(fromFirst, Interval), RangeBound.Of(fromSecond, Interval));
        Assert.Equal(RangeBound.Of(fromFirst, Exact), RangeBound.Of(fromSecond, Exact));
    }

    [Fact]
    public void Properties_UseTheirDeclarationAndNormalizeThisReads()
    {
        var property = new Property("End", Integer, default);
        var other = new Property("End", Integer, default);
        var bare = new PropertyReferenceExpression(null!, property);
        var member = PropertyRead(new ThisExpression(null!, Holder), property);
        var different = PropertyRead(new ThisExpression(null!, Holder), other);

        Assert.Equal(RangeBound.Of(bare, Interval), RangeBound.Of(member, Interval));
        Assert.NotEqual(RangeBound.Of(bare, Interval), RangeBound.Of(different, Interval));
    }

    [Fact]
    public void UnsupportedReceiversAndRefMembers_NeverAskForCapturedFacts()
    {
        var field = new Field("end", Integer, OwnershipKind.Unrestricted, default);
        var refField = new Field("end", Integer, OwnershipKind.Borrowed, new MemberModifiers { IsRef = true });
        var holder = new LocalVariable("holder", Holder);
        var alias = new LocalVariable("alias", Holder) { IsRef = true };
        var deep = FieldRead(FieldRead(new LocalReferenceExpression(null!, holder), field), field);
        var conditional = new MemberAccessExpression(null!, new LocalReferenceExpression(null!, holder), "end", Integer, isConditional: true, member: new FieldResult(field, Holder, []));
        Expression[] values = [deep, conditional, FieldRead(new LocalReferenceExpression(null!, alias), field), new FieldReferenceExpression(null!, refField)];

        foreach(var value in values)
        {
            Assert.Null(RangeBound.Of(value, _ => throw new InvalidOperationException("Unsupported member facts were queried.")));
        }
    }

    [Fact]
    public void GenuineLengthCallbacks_ProvideTheDescriptorAndKeepTheInlineMarker()
    {
        var local = new LocalReferenceExpression(null!, new LocalVariable("values", Holder));
        var length = new MemberAccessExpression(null!, local, "Length", Integer);
        var captured = new RangeBound(Admitted.Exactly(5), local.Local, false);
        var fromEnd = new UnaryExpression(null!, UnaryOperatorKind.FromEnd, length, Index);

        Assert.Null(RangeBound.Of(length, _ => Admitted.Exactly(5)));
        Assert.Equal(captured, RangeBound.Of(length, lengths: _ => captured));
        Assert.Equal(captured with { FromEnd = true }, RangeBound.Of(fromEnd, lengths: _ => captured));
    }

    [Fact]
    public void MemberIntervals_RemainIndependentEvenWhenTheirSymbolDescriptorMatches()
    {
        var field = new Field("end", Integer, OwnershipKind.Unrestricted, default);
        var start = RangeBound.Of(new FieldReferenceExpression(null!, field), Interval)!;
        var end = RangeBound.Of(new FieldReferenceExpression(null!, field), Interval)!;
        var range = new StoredRange(start, end);

        Assert.Equal(start.Symbol, end.Symbol);
        Assert.Equal(0, Lengths.Shortest(Lengths.OfSlice(range, Admitted.Exactly(5))));
    }

    private static MemberAccessExpression FieldRead(Expression receiver, Field field)
    {
        return new MemberAccessExpression(null!, receiver, field.Name, field.Type, member: new FieldResult(field, Holder, []));
    }

    private static MemberAccessExpression PropertyRead(Expression receiver, Property property)
    {
        return new MemberAccessExpression(null!, receiver, property.Name, property.Type, member: new PropertyResult(property, Holder, []));
    }

    private static Admitted Interval(Expression _)
    {
        return Admitted.Between(3, 4);
    }

    private static Admitted Exact(Expression _)
    {
        return Admitted.Exactly(3);
    }
}