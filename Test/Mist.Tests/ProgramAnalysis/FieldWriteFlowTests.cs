using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FieldWriteFlowTests
{
    private const int Last = FactHolderFlowTests.Last;

    [Theory]
    [InlineData("ref-local-bound-to-a-method-of-this", true)]
    [InlineData("ref-local-bound-to-a-method-of-another-object", true)]
    [InlineData("ref-local-bound-to-a-method-returning-a-static", true)]
    [InlineData("ref-local-bound-to-a-method-returning-what-another-returns", true)]
    [InlineData("ref-local-of-that-ref-local", true)]
    [InlineData("ref-local-written-in-a-loop", true)]
    [InlineData("call-assigned", true)]
    [InlineData("call-compounded", true)]
    [InlineData("ref-local-bound-to-a-method-returning-another-field", false)]
    [InlineData("ref-local-written-before-the-field", false)]
    public void AWriteThroughAReferenceACallReturnsEndsTheFieldsItMayName(string form, bool refused)
    {
        var f = new FactHolderFragments();
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var s = FactHolderFragments.Int(f.H, "s", isRef: true);
        var getA = f.Returning("GetA", FactHolderFragments.Member(f.This, f.A));
        var getB = f.Returning("GetB", FactHolderFragments.Member(f.This, f.B));
        var getS = f.Returning("GetS", FactHolderFragments.Own(f.S));
        var outer = f.Returning("Outer", FactHolderFragments.Call(f.This, getA));
        var other = new LocalVariable("other", f.Pair) { Parameter = FactHolderFragments.Parameter("other", f.Pair) };
        var field = form == "ref-local-bound-to-a-method-returning-a-static" ? f.S : f.A;
        Statement five()
        {
            return FactHolderFragments.Set(FactHolderFragments.Own(field), f.Number(5L));
        }

        Statement bind(Method method, Expression? receiver = null)
        {
            return FactHolderFragments.Declare(r, FactHolderFragments.Call(receiver ?? f.This, method));
        }

        var write = FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(1L));
        List<Statement> body = form switch
        {
            "ref-local-bound-to-a-method-of-this" => [five(), bind(getA), write],
            "ref-local-bound-to-a-method-of-another-object" => [five(), bind(getA, FactHolderFragments.Read(other)), write],
            "ref-local-bound-to-a-method-returning-a-static" => [five(), bind(getS), write],
            "ref-local-bound-to-a-method-returning-what-another-returns" => [five(), bind(outer), write],
            "ref-local-of-that-ref-local" => [five(), bind(getA), FactHolderFragments.Declare(s, FactHolderFragments.Read(r)),
                FactHolderFragments.Set(FactHolderFragments.Read(s), f.Number(1L))],
            "ref-local-written-in-a-loop" => [five(), bind(getA), new WhileStatement(HandBuiltRefinements.Local(f.H.Bool, "flag"), HandBuiltModel.Block(write))],
            "call-assigned" => [five(), FactHolderFragments.Set(FactHolderFragments.Call(f.This, getA), f.Number(1L))],
            "call-compounded" => [five(), HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), FactHolderFragments.Call(f.This, getA),
                BinaryOperatorKind.Add, f.Number(1L), f.H.Int, null, IncrementForm.None))],
            "ref-local-bound-to-a-method-returning-another-field" => [five(), bind(getB), write],
            "ref-local-written-before-the-field" => [bind(getA), write, five()],
            _ => throw new InvalidOperationException(form),
        };
        body.Add(f.Need(FactHolderFragments.Own(field, Last)));

        FactHolderFlowTests.AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("ref-parameter", true)]
    [InlineData("out-parameter", true)]
    [InlineData("ref-parameter-compounded", true)]
    [InlineData("ref-parameter-written-in-a-loop", true)]
    [InlineData("ref-parameter-writing-a-static", true)]
    [InlineData("ref-local-of-a-ref-parameter", true)]
    [InlineData("ref-parameter-of-another-type", false)]
    [InlineData("ref-parameter-written-before-the-field", false)]
    public void AWriteThroughAByRefParameterEndsEveryFieldOfItsType(string form, bool refused)
    {
        var f = new FactHolderFragments();
        var x = f.ByRef("x", direction: form == "out-parameter" ? ByRefDirection.Out : ByRefDirection.Ref);
        var wide = f.ByRef("w", f.H.Built.Type("Int64"));
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var field = form == "ref-parameter-writing-a-static" ? f.S : f.A;
        var five = FactHolderFragments.Set(FactHolderFragments.Own(field), f.Number(5L));
        var write = FactHolderFragments.Set(FactHolderFragments.Read(x), f.Number(1L));
        List<Statement> body = form switch
        {
            "ref-parameter" or "out-parameter" or "ref-parameter-writing-a-static" => [five, write],
            "ref-parameter-compounded" => [five, HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), FactHolderFragments.Read(x),
                BinaryOperatorKind.Add, f.Number(1L), f.H.Int, null, IncrementForm.None))],
            "ref-parameter-written-in-a-loop" => [five, new WhileStatement(HandBuiltRefinements.Local(f.H.Bool, "flag"), HandBuiltModel.Block(write))],
            "ref-local-of-a-ref-parameter" => [five, FactHolderFragments.Declare(r, FactHolderFragments.Read(x)), FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(1L))],
            "ref-parameter-of-another-type" => [five, FactHolderFragments.Set(FactHolderFragments.Read(wide), f.H.Literal(1L, f.H.Built.Type("Int64")))],
            "ref-parameter-written-before-the-field" => [write, five],
            _ => throw new InvalidOperationException(form),
        };
        body.Add(f.Need(FactHolderFragments.Own(field, Last)));

        FactHolderFlowTests.AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("local-copied-from-this", true)]
    [InlineData("local-a-call-returns", true)]
    [InlineData("call-result", true)]
    [InlineData("field", true)]
    [InlineData("parameter", true)]
    [InlineData("local-copied-from-this-compounded", true)]
    [InlineData("local-copied-from-this-writing-another-field", false)]
    [InlineData("local-only-ever-new", false)]
    public void AWriteToAMemberOfAnObjectThatMayBeThisEndsThatFieldOfThis(string receiver, bool refused)
    {
        var f = new FactHolderFragments();
        var o = new LocalVariable("o", f.Pair);
        var self = new Method("Self", f.Pair, [], new MemberModifiers()) { Body = HandBuiltModel.Block(new ReturnStatement(f.This, f.Pair)), DeclaringType = f.Pair };
        var other = f.AddField(f.Pair, "Other", type: f.Pair);
        var q = new LocalVariable("q", f.Pair) { Parameter = FactHolderFragments.Parameter("q", f.Pair) };
        List<Statement> body = [];
        Expression written = receiver switch
        {
            "local-copied-from-this" or "local-copied-from-this-compounded" or "local-copied-from-this-writing-another-field" => FactHolderFragments.Read(o),
            "local-a-call-returns" => FactHolderFragments.Read(o),
            "call-result" => FactHolderFragments.Call(f.This, self),
            "field" => FactHolderFragments.Own(other),
            "parameter" => FactHolderFragments.Read(q),
            "local-only-ever-new" => FactHolderFragments.Read(o),
            _ => throw new InvalidOperationException(receiver),
        };
        if(written is LocalReferenceExpression { Local: var local } && ReferenceEquals(local, o))
        {
            body.Add(FactHolderFragments.Declare(o, receiver switch
            {
                "local-a-call-returns" => FactHolderFragments.Call(f.This, self),
                "local-only-ever-new" => f.Create(),
                _ => f.This,
            }));
        }

        body.Add(FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(5L)));
        var member = FactHolderFragments.Member(written, receiver == "local-copied-from-this-writing-another-field" ? f.B : f.A);
        body.Add(receiver == "local-copied-from-this-compounded"
            ? HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(), member, BinaryOperatorKind.Add, f.Number(1L), f.H.Int, null, IncrementForm.None))
            : FactHolderFragments.Set(member, f.Number(1L)));
        body.Add(f.Need(FactHolderFragments.Own(f.A, Last)));

        FactHolderFlowTests.AssertRefusedOnlyAfter(f, body, refused);
    }

    [Theory]
    [InlineData("field-written", true)]
    [InlineData("static-written", true)]
    [InlineData("member-of-a-parameter-written", true)]
    [InlineData("another-ref-parameter-written", true)]
    [InlineData("array-element-written", true)]
    [InlineData("call-that-may-write", true)]
    [InlineData("readonly-ref-parameter-after-a-field-write", true)]
    [InlineData("local-written", false)]
    [InlineData("readonly-call", false)]
    [InlineData("member-of-a-local-only-ever-new-written", false)]
    [InlineData("member-of-a-struct-copy-written", false)]
    [InlineData("member-of-a-by-value-struct-parameter-written", false)]
    [InlineData("element-of-an-array-created-here-written", false)]
    [InlineData("written-through-a-ref-a-call-returns-for-a-new-object", false)]
    [InlineData("written-through-a-ref-a-call-returns-for-a-struct-handed-by-ref", false)]
    [InlineData("written-through-a-ref-a-call-returns-for-a-struct-field-of-a-new-object", false)]
    [InlineData("written-through-a-ref-two-calls-return-for-a-new-object", false)]
    [InlineData("written-through-a-ref-a-call-returns-for-a-parameter-it-sets-to-this", true)]
    [InlineData("written-through-a-ref-an-extension-call-returns-for-a-new-object", false)]
    [InlineData("written-through-a-ref-an-extension-call-returns-for-this", true)]
    public void AByRefParameterLosesItsFactsAtEveryWriteToStorageItMayName(string form, bool refused)
    {
        var f = new FactHolderFragments();
        var x = f.ByRef("x", direction: form == "readonly-ref-parameter-after-a-field-write" ? ByRefDirection.In : ByRefDirection.Ref);
        var y = f.ByRef("y");
        var box = new LocalVariable("b", f.Box) { Parameter = FactHolderFragments.Parameter("b", f.Box) };
        var array = new LocalVariable("arr", f.H.Sequence(length: 3L));
        var setA = new Method("SetA", f.H.Int, [], new MemberModifiers())
        {
            Body = HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(1L))),
            DeclaringType = f.Pair,
        };
        var getA = new Method("GetA", f.H.Int, [], new MemberModifiers { IsReadonly = true }) { Body = HandBuiltModel.Block(), DeclaringType = f.Pair };
        var refA = f.Returning("RefA", FactHolderFragments.Member(f.This, f.A));
        var o = new LocalVariable("o", f.Pair);
        var point = HandBuiltModel.Struct("Point");
        var copy = new LocalVariable("c", point);
        var value = new LocalVariable("v", point) { Parameter = FactHolderFragments.Parameter("v", point) };
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var inner = new Field("X", f.H.Int, default, new MemberModifiers()) { DeclaringType = point };
        var holder = f.ByRef("s", point);
        var getX = f.Returning("GetX", FactHolderFragments.Member(FactHolderFragments.Read(holder), inner), holder.Parameter!);
        var owner = new LocalVariable("owner", f.Pair) { Parameter = FactHolderFragments.Parameter("owner", f.Pair) };
        var deep = f.Returning("Deep", FactHolderFragments.Member(FactHolderFragments.Member(FactHolderFragments.Read(owner), f.AddField(f.Pair, "T", type: point)), inner),
            owner.Parameter!);
        var outer = f.Returning("Outer", FactHolderFragments.Implicit(refA));
        var reset = f.Returning("Reset", [FactHolderFragments.Set(FactHolderFragments.Read(owner), f.This)], FactHolderFragments.Member(FactHolderFragments.Read(owner), f.A),
            owner.Parameter!);
        var extension = FactHolderFragments.Extension(f.Pair);
        var extA = FactHolderFragments.Extending(extension, f.Returning("ExtA", FactHolderFragments.Member(FactHolderFragments.Read(extension.Receiver!), f.A)));
        List<Statement> setup = form switch
        {
            "written-through-a-ref-an-extension-call-returns-for-a-new-object" => [FactHolderFragments.Declare(r, FactHolderFragments.Call(f.Create(), extA))],
            "written-through-a-ref-an-extension-call-returns-for-this" => [FactHolderFragments.Declare(r, FactHolderFragments.Call(f.This, extA))],
            "written-through-a-ref-a-call-returns-for-a-struct-handed-by-ref" =>
                [FactHolderFragments.Declare(copy, FactHolderFragments.Own(f.AddField(f.Pair, "P", type: point))), FactHolderFragments.Declare(r, f.Free(getX, FactHolderFragments.Read(copy)))],
            "written-through-a-ref-a-call-returns-for-a-struct-field-of-a-new-object" => [FactHolderFragments.Declare(r, FactHolderFragments.Call(f.This, deep, f.Create()))],
            "written-through-a-ref-two-calls-return-for-a-new-object" => [FactHolderFragments.Declare(r, FactHolderFragments.Call(f.Create(), outer))],
            "written-through-a-ref-a-call-returns-for-a-parameter-it-sets-to-this" => [FactHolderFragments.Declare(r, FactHolderFragments.Call(f.This, reset, f.Create()))],
            "member-of-a-local-only-ever-new-written" => [FactHolderFragments.Declare(o, f.Create())],
            "member-of-a-struct-copy-written" => [FactHolderFragments.Declare(copy, FactHolderFragments.Own(f.AddField(f.Pair, "P", type: point)))],
            "element-of-an-array-created-here-written" => [FactHolderFragments.Declare(array, f.H.Array(3L))],
            "written-through-a-ref-a-call-returns-for-a-new-object" => [FactHolderFragments.Declare(r, FactHolderFragments.Call(f.Create(), refA))],
            _ => [],
        };
        Statement write = form switch
        {
            "field-written" or "readonly-ref-parameter-after-a-field-write" => FactHolderFragments.Set(FactHolderFragments.Own(f.A), f.Number(1L)),
            "static-written" => FactHolderFragments.Set(FactHolderFragments.Own(f.S), f.Number(1L)),
            "member-of-a-parameter-written" => FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(box), f.V), f.Number(1L)),
            "another-ref-parameter-written" => FactHolderFragments.Set(FactHolderFragments.Read(y), f.Number(1L)),
            "array-element-written" => FactHolderFragments.Set(f.H.At(FactHolderFragments.Read(array), f.Number(0L)), f.Number(1L)),
            "call-that-may-write" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, setA)),
            "local-written" => FactHolderFragments.Set(FactHolderFragments.Read(FactHolderFragments.Int(f.H, "a")), f.Number(1L)),
            "readonly-call" => HandBuiltRefinements.Evaluate(FactHolderFragments.Call(f.This, getA)),
            "member-of-a-local-only-ever-new-written" => FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(o), f.A), f.Number(1L)),
            "member-of-a-struct-copy-written" => FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(copy), f.A), f.Number(1L)),
            "member-of-a-by-value-struct-parameter-written" => FactHolderFragments.Set(FactHolderFragments.Member(FactHolderFragments.Read(value), f.A), f.Number(1L)),
            "element-of-an-array-created-here-written" => FactHolderFragments.Set(f.H.At(FactHolderFragments.Read(array), f.Number(0L)), f.Number(1L)),
            "written-through-a-ref-a-call-returns-for-a-new-object" or "written-through-a-ref-a-call-returns-for-a-struct-handed-by-ref"
                or "written-through-a-ref-a-call-returns-for-a-struct-field-of-a-new-object" or "written-through-a-ref-two-calls-return-for-a-new-object"
                or "written-through-a-ref-a-call-returns-for-a-parameter-it-sets-to-this" or "written-through-a-ref-an-extension-call-returns-for-a-new-object"
                or "written-through-a-ref-an-extension-call-returns-for-this" => FactHolderFragments.Set(FactHolderFragments.Read(r), f.Number(1L)),
            _ => throw new InvalidOperationException(form),
        };
        var five = form == "readonly-ref-parameter-after-a-field-write"
            ? f.H.Binary(FactHolderFragments.Read(x), BinaryOperatorKind.Equal, f.Number(5L)) : HandBuiltRefinements.Assign(FactHolderFragments.Read(x), f.Number(5L));
        var need = f.Need(FactHolderFragments.Read(x, Last));
        List<Statement> body = form == "readonly-ref-parameter-after-a-field-write"
            ? [HandBuiltRefinements.If(five, write, need)]
            : [.. setup, HandBuiltRefinements.Evaluate(five), write, need];

        FactHolderFlowTests.AssertRefusedOnlyAfter(f, body, refused);
    }
}