using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// What <see cref="ReadonlyInference"/> infers for an unmarked member from the effects of a compound or
/// increment write and of an inserted whole-type clone (readonly-contexts Requirement 10.6, amended by
/// purity Requirement 11). It is asked without a purity oracle, as the emitter asks it before spelling
/// C# <c>readonly</c> on a struct member, so a wrong "readonly" here is a struct member Roslyn rejects or
/// a caller admitted into a readonly context.
/// </summary>
[Trait("Tier", "2")]
public sealed class ReadonlyInferenceEffectTests
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType counter = HandBuiltModel.Class("Counter");
    private readonly ClassType integer;
    private readonly Field count;
    private readonly Field total;

    public ReadonlyInferenceEffectTests()
    {
        integer = model.Type("Int32");
        count = new Field("Count", integer, default, default) { DeclaringType = counter };
        total = new Field("Total", integer, default, new MemberModifiers { IsStatic = true }) { DeclaringType = counter };
        counter.FieldsList.Add(count);
        counter.FieldsList.Add(total);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ACompoundOrIncrementWriteOfItsOwnFieldIsAWrite_ButOfALocalIsNot(bool increment, bool toLocal)
    {
        var tally = new LocalVariable("tally", integer);
        Expression target = toLocal ? new LocalReferenceExpression(HandBuiltModel.Node(), tally) : new FieldReferenceExpression(HandBuiltModel.Node(), count);
        var bump = Method("Bump",
            new LocalDeclarationStatement(tally, HandBuiltModel.Literal(integer, 0L)),
            Run(new CompoundAssignmentExpression(HandBuiltModel.Node(), target, BinaryOperatorKind.Add, HandBuiltModel.Literal(integer, 1L), integer, null, increment ? IncrementForm.Postfix : IncrementForm.None)));

        Assert.Equal(toLocal, new ReadonlyInference().IsReadonly(bump));
    }

    [Theory]
    [InlineData("user-operator-writing-a-static", false)]
    [InlineData("user-operator-writing-nothing", true)]
    [InlineData("bodiless-intrinsic", true)]
    [InlineData("bodiless-clr-projected", false)]
    public void ACompoundOfALocalIsJudgedByTheOperatorItCalls(string shape, bool expected)
    {
        var plus = new Operator(OperatorKind.Plus, counter, [new Parameter("a", counter), new Parameter("b", counter)], shape switch
        {
            "bodiless-intrinsic" => new MemberModifiers { IsStatic = true, IsIntrinsic = true },
            "bodiless-clr-projected" => new MemberModifiers { IsStatic = true, IsClrProjected = true },
            _ => new MemberModifiers { IsStatic = true },
        })
        {
            Body = shape switch
            {
                "user-operator-writing-a-static" => HandBuiltModel.Block(Run(Write(total)), Returning(Handed())),
                "user-operator-writing-nothing" => HandBuiltModel.Block(Returning(Handed())),
                _ => null,
            },
            DeclaringType = counter,
        };
        var sum = new LocalVariable("sum", counter);
        var combine = Method("Combine",
            new LocalDeclarationStatement(sum, Handed()),
            Run(new CompoundAssignmentExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), sum),
                BinaryOperatorKind.Add, Handed(), counter, plus, IncrementForm.None)));

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(combine));
    }

    [Theory]
    [InlineData("external", "writes", false)]
    [InlineData("external", "quiet", true)]
    [InlineData("fresh", "writes-local", true)]
    [InlineData("external", "writes-local", false)]
    [InlineData("fresh", "writes", false)]
    public void AnInsertedCloneIsJudgedByTheCloneItCalls_UnlessALocalOneOnWhatTheMemberOwns(string source, string clone, bool expected)
    {
        var copy = new Method("Clone", counter, [], new MemberModifiers { IsOwned = true, IsLocal = clone == "writes-local" })
        {
            Body = clone == "quiet"
                ? HandBuiltModel.Block(Returning(Fresh()))
                : HandBuiltModel.Block(Run(Write(count)), Returning(Fresh())),
            DeclaringType = counter,
        };
        counter.MethodsList.Add(copy);
        var made = new LocalVariable("made", counter);
        Expression copied = source == "fresh" ? new LocalReferenceExpression(HandBuiltModel.Node(), made) : Handed();
        var keep = Method("Keep",
            new LocalDeclarationStatement(made, Fresh()),
            new LocalDeclarationStatement(new LocalVariable("kept", counter) { Ownership = OwnershipKind.Owned }, new CloneExpression(HandBuiltModel.Node(), copied)));

        Assert.Equal(expected, new ReadonlyInference().IsReadonly(keep));
    }

    private Method Method(string name, params Statement[] statements)
    {
        return new Method(name, integer, [], default) { Body = HandBuiltModel.Block(statements), DeclaringType = counter };
    }

    private LocalReferenceExpression Handed()
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("c", counter) { Parameter = new Parameter("c", counter) });
    }

    private NewExpression Fresh()
    {
        return new NewExpression(HandBuiltModel.Node(), counter, [], null, [], []);
    }

    private AssignmentExpression Write(Field field)
    {
        return new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), field), HandBuiltModel.Literal(integer, 1L), integer);
    }

    private static ReturnStatement Returning(Expression value)
    {
        return new ReturnStatement(value, value.Type);
    }

    private static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }
}