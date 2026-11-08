using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FactFlowTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void ReserveChainsTheGuardAndExactProduct(bool capacityType, bool directArgument)
    {
        var h = new HandBuiltRefinements();
        var used = HandBuiltRefinements.Local(h.Int, "used");
        var capacity = HandBuiltRefinements.Local(capacityType ? h.Between(h.Int, 0L, 1_000_000L, "Capacity") : h.Int, "capacity");
        var doubled = HandBuiltRefinements.Local(h.Int, "doubled");
        var available = new Parameter("available", h.Int);
        var limit = new LocalVariable("available", h.Int) { Parameter = available };
        var neededType = h.Refine(h.Int, ComparisonKind.LessThanOrEqual, HandBuiltRefinements.Read(limit), null);
        var reserve = new Method("Reserve", h.Int, [new Parameter("needed", neededType), available], new MemberModifiers { IsReadonly = true });
        var product = h.Binary(capacity, BinaryOperatorKind.Multiply, h.Literal(2L), h.Int);
        var argument = directArgument ? product : doubled;
        var call = new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [used, argument], h.Int, reserve, [neededType, h.Int]);
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(used, BinaryOperatorKind.LessThanOrEqual, capacity),
            HandBuiltRefinements.Give(doubled, product), HandBuiltRefinements.Evaluate(call)));

        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        if(capacityType)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal("MIST0277", Assert.Single(findings).Number.Value);
        }
    }

    [Fact]
    public void NeedsTenUsesTheCopiedValueAfterTheSourceIsReassigned()
    {
        var h = new HandBuiltRefinements();
        var x = HandBuiltRefinements.Local(h.Int, "x");
        var y = HandBuiltRefinements.Local(h.Int, "y");
        var random = new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Int, null, []);
        var body = HandBuiltModel.Block(
            HandBuiltRefinements.Give(x, h.Literal(5L)),
            HandBuiltRefinements.Give(y, h.Binary(x, BinaryOperatorKind.Multiply, h.Literal(2L), h.Int)),
            HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(x, random)),
            HandBuiltRefinements.Need(y, h.Refine(h.Int, ComparisonKind.Equal, 10L, "Ten")));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ACallRebindsTheMutableFieldWhenFieldEffectsMayWrite(bool writes)
    {
        var h = new HandBuiltRefinements();
        var owner = HandBuiltModel.Class("Owner");
        var field = new Field("count", h.Int, default, new MemberModifiers()) { DeclaringType = owner };
        var read = new FieldReferenceExpression(HandBuiltModel.Node(), field);
        var method = new Method("Touch", h.Int, [], new MemberModifiers { IsReadonly = !writes }) { DeclaringType = owner };
        var call = new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [], h.Int, method, []);
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(read, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0L)),
            HandBuiltRefinements.Evaluate(call), HandBuiltRefinements.Need(read, h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L))));

        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        if(writes)
        {
            Assert.Equal("MIST0272", Assert.Single(findings).Number.Value);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void ARefCallForgetsTheArgumentsHolder()
    {
        var h = new HandBuiltRefinements();
        var x = HandBuiltRefinements.Local(h.Int, "x");
        var parameter = new Parameter("value", h.Int, new ParameterModifier(false, true, default, ByRefDirection.Ref));
        var method = new Method("Reset", h.Int, [parameter], new MemberModifiers());
        var call = new InvocationExpression(HandBuiltModel.Node(), h.Literal(0L), [x], h.Int, method, [h.Int]);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(x, h.Literal(10L)), HandBuiltRefinements.Evaluate(call),
            HandBuiltRefinements.Need(x, h.Refine(h.Int, ComparisonKind.Equal, 10L, "Ten")));

        Assert.Equal("MIST0272", Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body)).Number.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompoundAssignmentRecordsTheOldValueOnlyWhenTheSumFits(bool bounded)
    {
        var h = new HandBuiltRefinements();
        var total = HandBuiltRefinements.Local(h.Int, "total");
        var i = HandBuiltRefinements.Local(bounded ? h.Between(h.Int, 0L, 9L) : h.Int, "i");
        var compound = new CompoundAssignmentExpression(HandBuiltModel.Node(), total, BinaryOperatorKind.Add, i, h.Int, null, IncrementForm.None);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(total, h.Literal(1L)), HandBuiltRefinements.Evaluate(compound),
            HandBuiltRefinements.Need(total, h.Between(h.Int, 1L, 10L)));

        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        if(bounded)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal("MIST0272", Assert.Single(findings).Number.Value);
        }
    }

    [Fact]
    public void ExactnessChecksEachNestedOperationBeforeKeepingTheOuterEquality()
    {
        var h = new HandBuiltRefinements();
        var x = HandBuiltRefinements.Local(h.Int, "x");
        var wrapped = h.Binary(x, BinaryOperatorKind.Add, h.Literal(1L), h.Int);
        var result = h.Binary(wrapped, BinaryOperatorKind.Subtract, x, h.Int);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Need(result, h.Refine(h.Int, ComparisonKind.Equal, 1L)));

        Assert.Equal("MIST0272", Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body)).Number.Value);
    }

    [Fact]
    public void ASharedMutableMemberDoesNotReuseTheValueTheGuardRead()
    {
        var h = new HandBuiltRefinements();
        var owner = HandBuiltModel.Class("Shared");
        var field = new Field("count", h.Int, default, new MemberModifiers()) { DeclaringType = owner };
        var instance = HandBuiltRefinements.Local(owner, "shared");
        var read = new MemberAccessExpression(HandBuiltModel.Node(), instance, "count", h.Int, member: new FieldResult(field, owner, []));
        var body = HandBuiltModel.Block(HandBuiltRefinements.If(h.Binary(read, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0L)),
            HandBuiltRefinements.Need(read, h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L))));

        Assert.Equal("MIST0272", Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), body)).Number.Value);
    }

    [Fact]
    public void ReferenceCopiesShareTheSameStableMemberValue()
    {
        var h = new HandBuiltRefinements();
        var owner = HandBuiltModel.Class("Stable");
        var field = new Field("count", h.Int, default, new MemberModifiers { IsReadonly = true }) { DeclaringType = owner };
        var original = HandBuiltRefinements.Local(owner, "original");
        var alias = HandBuiltRefinements.Local(owner, "alias");
        var first = new MemberAccessExpression(HandBuiltModel.Node(), original, "count", h.Int, member: new FieldResult(field, owner, []));
        var second = new MemberAccessExpression(HandBuiltModel.Node(), alias, "count", h.Int, member: new FieldResult(field, owner, []));
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(alias, original),
            HandBuiltRefinements.If(h.Binary(first, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0L)),
                HandBuiltRefinements.Need(second, h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L))));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALoopUsesTheIntegralDomainOfItsDependentLength(bool fixedLength)
    {
        var h = new HandBuiltRefinements();
        var array = HandBuiltRefinements.Local(h.Sequence(), "items");
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var original = h.Length(array);
        var length = new MemberAccessExpression(HandBuiltModel.Node(), array, "Length",
            fixedLength ? new LiftedType(h.Int, 3L) : new UnknownValueType(h.Int), member: original.Member);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(i, h.Literal(0L)),
            new WhileStatement(h.Binary(i, BinaryOperatorKind.LessThan, length), HandBuiltModel.Block(
                HandBuiltRefinements.Evaluate(h.At(array, i)),
                HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(i, h.Binary(i, BinaryOperatorKind.Add, h.Literal(1L), h.Int))))));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArithmeticUsesTheValueReadBeforeALaterOperandWritesTheHolder(bool compound)
    {
        var h = new HandBuiltRefinements();
        var x = HandBuiltRefinements.Local(h.Int, "x");
        var y = HandBuiltRefinements.Local(h.Int, "y");
        var write = HandBuiltRefinements.Assign(x, h.Literal(3L));
        Statement calculate = compound
            ? HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), x, BinaryOperatorKind.Add, write, h.Int, null, IncrementForm.None))
            : HandBuiltRefinements.Give(y, h.Binary(x, BinaryOperatorKind.Add, write, h.Int));
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(x, h.Literal(5L)), calculate,
            HandBuiltRefinements.Need(compound ? x : y, h.Refine(h.Int, ComparisonKind.Equal, 8L)));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Fact]
    public void ALoopWithAnInnerJoinKeepsTheNoWrapBoundForItsIncrement()
    {
        var h = new HandBuiltRefinements();
        var array = HandBuiltRefinements.Local(h.Sequence(), "items");
        var i = HandBuiltRefinements.Local(h.Int, "i");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(i, h.Literal(0L)),
            new WhileStatement(h.Binary(i, BinaryOperatorKind.LessThan, h.Length(array)), HandBuiltModel.Block(
                HandBuiltRefinements.If(h.Binary(i, BinaryOperatorKind.GreaterThan, h.Literal(0L)), HandBuiltRefinements.Evaluate(h.Call(h.Literal(1L), h.Int))),
                HandBuiltRefinements.Evaluate(h.At(array, i)),
                HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(i, h.Binary(i, BinaryOperatorKind.Add, h.Literal(1L), h.Int))))));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnInvalidInitializerDoesNotHideAnIndependentLaterRefusal(bool boolean)
    {
        var h = new HandBuiltRefinements();
        var target = boolean ? h.Refine(h.Bool, ComparisonKind.Equal, true, "Yes") : h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, "Adult");
        var body = HandBuiltModel.Block(HandBuiltRefinements.Need(boolean ? h.Literal(false) : h.Literal(7L), target),
            HandBuiltRefinements.Need(boolean ? h.Literal(false) : h.Literal(7L), target));

        Assert.Equal(2, h.Built.Analyze(new RefinementAnalyzer(), body).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AParameterWhereClauseEntersWithItsSiblingParametersTypeFacts(bool bounded)
    {
        var h = new HandBuiltRefinements();
        var available = new Parameter("available", bounded ? h.Between(h.Int, 0L, 10L) : h.Int);
        var limit = new LocalVariable("available", available.Type) { Parameter = available };
        var neededType = h.Refine(h.Int, ComparisonKind.LessThanOrEqual, HandBuiltRefinements.Read(limit));
        var needed = new LocalVariable("needed", neededType) { Parameter = new Parameter("needed", neededType) };
        var body = HandBuiltModel.Block(HandBuiltRefinements.Need(HandBuiltRefinements.Read(needed), h.Refine(h.Int, ComparisonKind.LessThanOrEqual, 10L)));

        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        if(bounded)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal("MIST0273", Assert.Single(findings).Number.Value);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACallBindsMemberContractsToItsActualReceiver(bool thisInstance)
    {
        var h = new HandBuiltRefinements();
        var owner = HandBuiltModel.Class("Owner");
        var field = new Field("count", h.Int, default, new MemberModifiers()) { DeclaringType = owner };
        var own = new FieldReferenceExpression(HandBuiltModel.Node(), field);
        var target = h.Refine(h.Int, ComparisonKind.LessThanOrEqual, own, null);
        var method = new Method("Take", h.Int, [new Parameter("needed", target)], new MemberModifiers { IsReadonly = true }) { DeclaringType = owner };
        Expression instance = thisInstance ? new ThisExpression(HandBuiltModel.Node(), owner) : HandBuiltRefinements.Local(owner, "other");
        var receiver = new MemberAccessExpression(HandBuiltModel.Node(), instance, "Take", h.Int);
        var call = new InvocationExpression(HandBuiltModel.Node(), receiver, [h.Literal(7L)], h.Int, method, [target]);
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(own, h.Literal(10L))), HandBuiltRefinements.Evaluate(call));

        var findings = h.Built.Analyze(new RefinementAnalyzer(), body);

        if(thisInstance)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal("MIST0277", Assert.Single(findings).Number.Value);
        }
    }

    [Fact]
    public void UserOperatorEffectsPrecedeTheAssignmentOfItsPromisedResult()
    {
        var h = new HandBuiltRefinements();
        var owner = HandBuiltModel.Class("Owner");
        var field = new Field("count", h.Int, default, new MemberModifiers()) { DeclaringType = owner };
        var target = new FieldReferenceExpression(HandBuiltModel.Node(), field);
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L);
        var operation = new Operator(OperatorKind.Plus, positive, [new Parameter("left", h.Int), new Parameter("right", h.Int)], new MemberModifiers { IsStatic = true });
        var compound = new CompoundAssignmentExpression(HandBuiltModel.Node(), target, BinaryOperatorKind.Add, h.Literal(1L), h.Int, operation, IncrementForm.None)
        { ResultType = positive };
        var body = HandBuiltModel.Block(HandBuiltRefinements.Evaluate(compound), HandBuiltRefinements.Need(target, positive));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

    [Fact]
    public void APerVariableJoinPreservesSeparatedIntervalsUntilWidening()
    {
        var h = new HandBuiltRefinements();
        var value = HandBuiltRefinements.Local(h.Int, "value");
        var target = HandBuiltRefinements.RefinePattern(h.Int, new DisjunctionPattern(HandBuiltModel.Node(), h.Int,
            [h.Compare(h.Int, ComparisonKind.Equal, 1L), h.Compare(h.Int, ComparisonKind.Equal, 100L)]));
        var body = HandBuiltModel.Block(HandBuiltRefinements.Give(value, h.Literal(1L)),
            HandBuiltRefinements.If(HandBuiltRefinements.Local(h.Bool, "flag"), HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(value, h.Literal(100L)))),
            HandBuiltRefinements.Need(value, target));

        Assert.Empty(h.Built.Analyze(new RefinementAnalyzer(), body));
    }

}