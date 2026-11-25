using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// What <see cref="UnseenWrites"/> puts outside a body's flow: storage a closure writes that is not its own, and
/// storage a call, construction or ref local reaches that the flow cannot name. <see cref="FactHolderDecisionTests"/>
/// holds the named-argument, ref-local alias and returned-ref cases; these are the remaining arms.
/// </summary>
[Trait("Tier", "1")]
public sealed class UnseenWriteDecisionTests
{
    [Theory]
    [InlineData("compound-assigns-a-captured-local", "d")]
    [InlineData("assigns-its-own-local", "")]
    [InlineData("assigns-its-own-parameter", "")]
    [InlineData("local-function-assigns-its-own-parameter", "")]
    [InlineData("assigns-a-field", "A")]
    [InlineData("assigns-a-field-of-this", "A")]
    [InlineData("assigns-a-field-of-another-object", "V")]
    [InlineData("assigns-an-element-of-a-captured-array", "")]
    [InlineData("hands-a-captured-local-out", "d")]
    [InlineData("hands-a-captured-local-readonly", "")]
    [InlineData("hands-a-captured-local-readonly-to-a-ref-returning-call", "d")]
    public void AClosureLeavesOutsideTheFlowOnlyStorageItWritesThatIsNotItsOwn(string form, string unseen)
    {
        var f = new FactHolderFragments();
        var d = FactHolderFragments.Int(f.H, "d");
        var t = FactHolderFragments.Int(f.H, "t");
        var p = new LocalVariable("p", f.H.Int) { Parameter = FactHolderFragments.Parameter("p", f.H.Int) };
        var array = new LocalVariable("array", f.H.Sequence());
        var other = new LocalVariable("other", f.Box);
        LambdaExpression lambda(params Statement[] statements)
        {
            return new LambdaExpression(HandBuiltModel.Node(), [p], null, HandBuiltModel.Block(statements), false, f.H.Int);
        }

        Expression closure = form switch
        {
            "compound-assigns-a-captured-local" => lambda(HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(),
                FactHolderFragments.Read(d), BinaryOperatorKind.Add, f.Number(1L), f.H.Int, null, IncrementForm.None))),
            "assigns-its-own-local" => lambda(FactHolderFragments.Declare(t, f.Number(0L)), FactHolderFragments.Set(FactHolderFragments.Read(t), f.Number(6L))),
            "assigns-its-own-parameter" => lambda(FactHolderFragments.Set(FactHolderFragments.Read(p), f.Number(6L))),
            "local-function-assigns-its-own-parameter" => f.Number(0L),
            "assigns-a-field" => lambda(FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(6L))),
            "assigns-a-field-of-this" => lambda(FactHolderFragments.Set(FactHolderFragments.Member(f.This, f.A), f.Number(6L))),
            "assigns-a-field-of-another-object" => lambda(FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(other), f.V), f.Number(6L))),
            "assigns-an-element-of-a-captured-array" => lambda(FactHolderFragments.Set(f.H.At(FactHolderFragments.Read(array), f.Number(0L)), f.Number(6L))),
            "hands-a-captured-local-out" => lambda(HandBuiltRefinements.Evaluate(f.Free(f.Viewing(f.H.Int, ByRefDirection.Out), FactHolderFragments.Read(d)))),
            "hands-a-captured-local-readonly" => lambda(HandBuiltRefinements.Evaluate(f.Free(f.Viewing(f.H.Int, ByRefDirection.In), FactHolderFragments.Read(d)))),
            "hands-a-captured-local-readonly-to-a-ref-returning-call" => lambda(HandBuiltRefinements.Evaluate(f.Free(f.Returning("Pick", null,
                f.ByRef("y", direction: ByRefDirection.In).Parameter!), FactHolderFragments.Read(d)))),
            _ => throw new InvalidOperationException(form),
        };

        Statement nested = form == "local-function-assigns-its-own-parameter"
            ? new LocalFunctionStatement(f.Method("W", f.Pair, HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Read(p), f.Number(6L))),
                f.H.Int, p.Parameter!), [p])
            : HandBuiltRefinements.Evaluate(closure);

        var root = HandBuiltModel.Block(FactHolderFragments.Declare(d, f.Number(5L)), FactHolderFragments.Declare(array, f.H.Array(3L)),
            FactHolderFragments.Declare(other, null), nested);

        Assert.Equal(unseen, Names(UnseenWrites.Of(root)));
    }

    [Theory]
    [InlineData("member-of-another-object-handed-out", "V")]
    [InlineData("member-of-another-object-handed-readonly", "")]
    [InlineData("member-of-another-object-handed-by-value", "")]
    [InlineData("member-of-another-object-handed-to-a-constructor-by-ref", "V")]
    [InlineData("member-of-another-object-handed-to-a-constructor-readonly", "")]
    [InlineData("local-handed-by-ref-to-a-ref-like-construction", "a")]
    [InlineData("local-handed-by-ref-to-a-plain-construction", "")]
    [InlineData("ref-local-of-a-member-of-another-object", "V,r")]
    public void StorageACallConstructionOrRefLocalReachesUnnamedIsOutsideTheFlow(string form, string unseen)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var member = FactHolderFragments.Member(FactHolderFragments.Read(new LocalVariable("other", f.Box)), f.V);
        NewExpression construct(IMistType type, ByRefDirection direction, Expression argument)
        {
            var parameter = FactHolderFragments.Parameter("value", f.H.Int, direction: direction);
            var constructor = new Constructor(type.Name, type, [parameter], new MemberModifiers());
            return new NewExpression(HandBuiltModel.Node(), type, [argument], constructor, [], [f.H.Int]);
        }

        Statement statement = form switch
        {
            "member-of-another-object-handed-out" => HandBuiltRefinements.Evaluate(f.Free(f.Viewing(f.H.Int, ByRefDirection.Out), member)),
            "member-of-another-object-handed-readonly" => HandBuiltRefinements.Evaluate(f.Free(f.Viewing(f.H.Int, ByRefDirection.In), member)),
            "member-of-another-object-handed-by-value" => HandBuiltRefinements.Evaluate(f.Free(f.Method("Take", f.Pair, null, f.H.Int,
                FactHolderFragments.Parameter("value", f.H.Int)), member)),
            "member-of-another-object-handed-to-a-constructor-by-ref" => HandBuiltRefinements.Evaluate(construct(f.Box, ByRefDirection.Ref, member)),
            "member-of-another-object-handed-to-a-constructor-readonly" => HandBuiltRefinements.Evaluate(construct(f.Box, ByRefDirection.In, member)),
            "local-handed-by-ref-to-a-ref-like-construction" => HandBuiltRefinements.Evaluate(construct(f.Span, ByRefDirection.Ref, FactHolderFragments.Read(a))),
            "local-handed-by-ref-to-a-plain-construction" => HandBuiltRefinements.Evaluate(construct(HandBuiltModel.Struct("Copy"), ByRefDirection.Ref,
                FactHolderFragments.Read(a))),
            "ref-local-of-a-member-of-another-object" => FactHolderFragments.Declare(r, member),
            _ => throw new InvalidOperationException(form),
        };

        Assert.Equal(unseen, Names(UnseenWrites.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(0L)), statement))));
    }

    private static string Names(IEnumerable<object> holders)
    {
        return string.Join(",", holders.Select(holder => holder switch
        {
            LocalVariable local => local.Name,
            Field field => field.Name,
            _ => throw new InvalidOperationException(holder.GetType().Name),
        }).Order(StringComparer.Ordinal));
    }
}