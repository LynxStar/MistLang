using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class WriteReachDecisionTests
{
    [Theory]
    [InlineData("field-of-this", "A,p,y")]
    [InlineData("field-of-another-object", "V,p,y")]
    [InlineData("static", "S,p,y")]
    [InlineData("its-ref-parameter-given-a-local", "")]
    [InlineData("its-ref-parameter-given-a-field", "A,p,y")]
    [InlineData("its-ref-parameter-given-a-member-of-a-new-object", "")]
    [InlineData("array-element", "p,y")]
    [InlineData("a-ref-local-bound-to-a-field-of-this", "A,B,S,V,p,y")]
    [InlineData("another-call-returning-a-field", "A,p,y")]
    [InlineData("a-field-handed-to-another-call", "B,p,y")]
    [InlineData("no-body", "A,B,S,V,p,y")]
    [InlineData("an-abstract-method", "A,B,S,V,p,y")]
    [InlineData("field-of-a-receiver-that-is-another-object", "")]
    [InlineData("field-of-a-parameter-given-another-object", "")]
    [InlineData("field-of-a-parameter-given-this", "A,p,y")]
    [InlineData("field-of-a-parameter-reassigned-to-this", "A,p,y")]
    [InlineData("field-of-a-parameter-handed-by-ref", "A,p,y")]
    [InlineData("field-of-a-parameter-compound-assigned-from-this", "A,p,y")]
    [InlineData("field-of-a-parameter-bound-to-a-ref-local", "A,p,y")]
    [InlineData("field-of-a-parameter-copied-to-a-local", "")]
    [InlineData("field-of-a-struct-field-of-a-parameter-given-another-object", "")]
    [InlineData("field-of-a-struct-field-of-a-parameter-given-this", "p,y")]
    [InlineData("field-of-a-struct-field-of-a-parameter-reassigned-to-this", "p,y")]
    [InlineData("field-of-a-ref-struct-parameter-given-a-member-of-a-new-object", "")]
    [InlineData("field-of-a-ref-struct-parameter-given-a-field", "p,y")]
    [InlineData("another-call-on-a-receiver-that-is-another-object", "")]
    [InlineData("another-call-handed-a-parameter-given-another-object", "")]
    [InlineData("another-call-handed-a-parameter-reassigned-to-this", "A,p,y")]
    [InlineData("another-call-handed-a-ref-struct-parameter-given-a-member-of-a-new-object", "")]
    [InlineData("another-call-handed-a-struct-field-of-a-receiver-that-is-another-object", "")]
    [InlineData("another-call-handed-a-struct-field-of-this", "p,y")]
    [InlineData("field-of-an-extension-receiver-that-is-another-object", "")]
    [InlineData("field-of-an-extension-receiver-that-is-this", "A,p,y")]
    [InlineData("field-of-an-extension-receiver-reassigned-to-a-parameter-given-this", "A,p,y")]
    [InlineData("field-of-an-extension-receiver-handed-by-ref", "A,p,y")]
    [InlineData("field-of-a-parameter-of-an-extension-method-given-another-object", "")]
    [InlineData("field-of-a-parameter-of-an-extension-method-given-this", "A,p,y")]
    [InlineData("another-call-on-an-extension-receiver-that-is-another-object", "")]
    [InlineData("another-call-on-an-extension-receiver-that-is-this", "A,p,y")]
    [InlineData("a-recursive-call-on-a-receiver-that-is-another-object", "")]
    [InlineData("a-recursive-call-on-this", "A,p,y")]
    [InlineData("a-mutually-recursive-call-on-a-receiver-that-is-another-object", "")]
    [InlineData("a-mutually-recursive-call-on-this", "A,B,p,y")]
    [InlineData("a-recursive-call-handing-this-on-given-another-object", "")]
    [InlineData("a-recursive-call-handing-its-parameter-on-given-this", "A,p,y")]
    [InlineData("a-recursive-call-handing-its-ref-parameter-on-given-a-local", "")]
    [InlineData("a-recursive-call-handing-its-ref-parameter-on-given-a-field", "A,p,y")]
    [InlineData("a-cycle-that-never-returns-known-storage", "")]
    public void AReferenceACallReturnsReachesTheStorageItsBodyReturnsReadAsTheCallers(string returned, string reached)
    {
        var f = new FactHolderFragments();
        var x = f.ByRef("x");
        var getA = f.Returning("GetA", FactHolderFragments.Member(f.This, f.A));
        var pass = f.Returning("Pass", FactHolderFragments.Read(x), x.Parameter!);
        var owner = new LocalVariable("owner", f.Pair) { Parameter = FactHolderFragments.Parameter("owner", f.Pair) };
        var through = f.Returning("Through", FactHolderFragments.Member(FactHolderFragments.Read(owner), f.A), owner.Parameter!);
        var point = HandBuiltModel.Struct("Point");
        var inner = new Field("X", f.H.Int, default, new MemberModifiers()) { DeclaringType = point };
        var stored = f.AddField(f.Pair, "P", type: point);
        var holder = f.ByRef("s", point);
        var getX = f.Returning("GetX", FactHolderFragments.Member(FactHolderFragments.Read(holder), inner), holder.Parameter!);
        var swap = new Method("Swap", f.H.Int, [f.ByRef("q", f.Pair).Parameter!], new MemberModifiers()) { DeclaringType = f.Pair };
        Statement[] toThis = [FactHolderFragments.Set(FactHolderFragments.Read(owner), f.This)];
        Expression ownerX = FactHolderFragments.Member(FactHolderFragments.Member(FactHolderFragments.Read(owner), stored), inner);
        var extension = FactHolderFragments.Extension(f.Pair);
        var extended = FactHolderFragments.Read(extension.Receiver!);
        var rec = f.RefMethod("Rec");
        var (ev, od) = (f.RefMethod("Ev"), f.RefMethod("Od"));
        _ = FactHolderFragments.ReturningEach(ev, FactHolderFragments.Implicit(od), FactHolderFragments.Member(f.This, f.A));
        _ = FactHolderFragments.ReturningEach(od, FactHolderFragments.Implicit(ev), FactHolderFragments.Member(f.This, f.B));
        var recursing = new LocalVariable("owner", f.Pair) { Parameter = FactHolderFragments.Parameter("owner", f.Pair) };
        var recP = f.RefMethod("RecP", recursing.Parameter!);
        var bounce = f.ByRef("z");
        var passOn = f.RefMethod("PassOn", bounce.Parameter!);
        var alias = FactHolderFragments.Int(f.H, "r", isRef: true);
        var callee = returned switch
        {
            "field-of-an-extension-receiver-that-is-another-object" or "field-of-an-extension-receiver-that-is-this" =>
                FactHolderFragments.Extending(extension, f.Returning("ExtA", FactHolderFragments.Member(extended, f.A))),
            "field-of-an-extension-receiver-reassigned-to-a-parameter-given-this" => FactHolderFragments.Extending(extension, f.Returning("ExtA",
                [FactHolderFragments.Set(FactHolderFragments.Read(extension.Receiver!), FactHolderFragments.Read(owner))], FactHolderFragments.Member(extended, f.A), owner.Parameter!)),
            "field-of-an-extension-receiver-handed-by-ref" => FactHolderFragments.Extending(extension, f.Returning("ExtA",
                [HandBuiltRefinements.Evaluate(f.Free(swap, FactHolderFragments.Read(extension.Receiver!)))], FactHolderFragments.Member(extended, f.A))),
            "field-of-a-parameter-of-an-extension-method-given-another-object" or "field-of-a-parameter-of-an-extension-method-given-this" =>
                FactHolderFragments.Extending(extension, through),
            "another-call-on-an-extension-receiver-that-is-another-object" or "another-call-on-an-extension-receiver-that-is-this" =>
                FactHolderFragments.Extending(extension, f.Returning("ExtA", FactHolderFragments.Call(extended, getA))),
            "field-of-a-parameter-reassigned-to-this" => f.Returning("Through", toThis, FactHolderFragments.Member(FactHolderFragments.Read(owner), f.A), owner.Parameter!),
            "field-of-a-parameter-handed-by-ref" => f.Returning("Through", [HandBuiltRefinements.Evaluate(f.Free(swap, FactHolderFragments.Read(owner)))],
                FactHolderFragments.Member(FactHolderFragments.Read(owner), f.A), owner.Parameter!),
            "field-of-a-parameter-compound-assigned-from-this" => f.Returning("Through", [HandBuiltRefinements.Evaluate(new CompoundAssignmentExpression(HandBuiltModel.Node(),
                FactHolderFragments.Read(owner), BinaryOperatorKind.Add, f.This, f.Pair, null, IncrementForm.None))], FactHolderFragments.Member(FactHolderFragments.Read(owner), f.A), owner.Parameter!),
            "field-of-a-parameter-bound-to-a-ref-local" or "field-of-a-parameter-copied-to-a-local" => f.Returning("Through", [FactHolderFragments.Declare(new LocalVariable("r", f.Pair) { IsRef = returned == "field-of-a-parameter-bound-to-a-ref-local" },
                FactHolderFragments.Read(owner))], FactHolderFragments.Member(FactHolderFragments.Read(owner), f.A), owner.Parameter!),
            "a-ref-local-bound-to-a-field-of-this" => f.Returning("Alias", [FactHolderFragments.Declare(alias, FactHolderFragments.Member(f.This, f.A))], FactHolderFragments.Read(alias)),
            "field-of-a-struct-field-of-a-parameter-given-another-object" or "field-of-a-struct-field-of-a-parameter-given-this" =>
                f.Returning("Deep", ownerX, owner.Parameter!),
            "field-of-a-struct-field-of-a-parameter-reassigned-to-this" => f.Returning("Deep", toThis, ownerX, owner.Parameter!),
            "field-of-a-ref-struct-parameter-given-a-member-of-a-new-object" or "field-of-a-ref-struct-parameter-given-a-field" => getX,
            "another-call-on-a-receiver-that-is-another-object" => f.Returning("Outer", FactHolderFragments.Implicit(getA)),
            "another-call-handed-a-parameter-given-another-object" => f.Returning("Outer", f.Free(through, FactHolderFragments.Read(owner)), owner.Parameter!),
            "another-call-handed-a-parameter-reassigned-to-this" => f.Returning("Outer", toThis, f.Free(through, FactHolderFragments.Read(owner)), owner.Parameter!),
            "another-call-handed-a-ref-struct-parameter-given-a-member-of-a-new-object" => f.Returning("Outer", f.Free(getX, FactHolderFragments.Read(holder)), holder.Parameter!),
            "another-call-handed-a-struct-field-of-a-receiver-that-is-another-object" or "another-call-handed-a-struct-field-of-this" =>
                f.Returning("Outer", f.Free(getX, FactHolderFragments.Own(stored))),
            "a-recursive-call-on-a-receiver-that-is-another-object" or "a-recursive-call-on-this" =>
                FactHolderFragments.ReturningEach(rec, FactHolderFragments.Implicit(rec), FactHolderFragments.Member(f.This, f.A)),
            "a-mutually-recursive-call-on-a-receiver-that-is-another-object" or "a-mutually-recursive-call-on-this" => ev,
            "a-recursive-call-handing-this-on-given-another-object" => FactHolderFragments.ReturningEach(recP, FactHolderFragments.Implicit(recP, f.This),
                FactHolderFragments.Member(FactHolderFragments.Read(recursing), f.A)),
            "a-recursive-call-handing-its-parameter-on-given-this" => FactHolderFragments.ReturningEach(recP,
                FactHolderFragments.Implicit(recP, FactHolderFragments.Read(recursing)), FactHolderFragments.Member(FactHolderFragments.Read(recursing), f.A)),
            "a-recursive-call-handing-its-ref-parameter-on-given-a-local" or "a-recursive-call-handing-its-ref-parameter-on-given-a-field" =>
                FactHolderFragments.ReturningEach(passOn, FactHolderFragments.Implicit(passOn, FactHolderFragments.Read(bounce)), FactHolderFragments.Read(bounce)),
            "a-cycle-that-never-returns-known-storage" => FactHolderFragments.ReturningEach(rec, FactHolderFragments.Implicit(rec)),
            "field-of-this" => getA,
            "field-of-another-object" => f.Returning("GetV", FactHolderFragments.Member(FactHolderFragments.Read(new LocalVariable("other", f.Box)), f.V)),
            "static" => f.Returning("GetS", FactHolderFragments.Own(f.S)),
            "its-ref-parameter-given-a-local" or "its-ref-parameter-given-a-field" or "its-ref-parameter-given-a-member-of-a-new-object" => pass,
            "array-element" => f.Returning("Element", f.H.At(f.H.Array(3L), f.Number(0L))),
            "another-call-returning-a-field" => f.Returning("Outer", FactHolderFragments.Call(f.This, getA)),
            "a-field-handed-to-another-call" => f.Returning("Outer", f.Free(pass, FactHolderFragments.Own(f.B))),
            "no-body" => f.Returning("Extern", null),
            "an-abstract-method" => AbstractCopy(f.Returning("GetA", FactHolderFragments.Member(f.This, f.A))),
            "field-of-a-receiver-that-is-another-object" => getA,
            "field-of-a-parameter-given-another-object" or "field-of-a-parameter-given-this" => through,
            _ => throw new InvalidOperationException(returned),
        };
        Expression[] arguments = returned switch
        {
            "its-ref-parameter-given-a-local" or "a-recursive-call-handing-its-ref-parameter-on-given-a-local" => [FactHolderFragments.Read(FactHolderFragments.Int(f.H, "a"))],
            "its-ref-parameter-given-a-field" or "a-recursive-call-handing-its-ref-parameter-on-given-a-field" => [FactHolderFragments.Own(f.A)],
            "a-recursive-call-handing-this-on-given-another-object" => [f.Create()],
            "a-recursive-call-handing-its-parameter-on-given-this" => [f.This],
            "its-ref-parameter-given-a-member-of-a-new-object" => [FactHolderFragments.Member(f.Create(), f.A)],
            "field-of-a-parameter-given-another-object" or "field-of-a-parameter-reassigned-to-this" or "field-of-a-parameter-handed-by-ref"
                or "field-of-a-parameter-compound-assigned-from-this" or "field-of-a-parameter-bound-to-a-ref-local" or "field-of-a-parameter-copied-to-a-local"
                or "field-of-a-struct-field-of-a-parameter-given-another-object" or "field-of-a-struct-field-of-a-parameter-reassigned-to-this"
                or "another-call-handed-a-parameter-given-another-object" or "another-call-handed-a-parameter-reassigned-to-this" => [f.Create()],
            "field-of-a-parameter-given-this" or "field-of-a-struct-field-of-a-parameter-given-this"
                or "field-of-an-extension-receiver-reassigned-to-a-parameter-given-this" or "field-of-a-parameter-of-an-extension-method-given-this" => [f.This],
            "field-of-a-parameter-of-an-extension-method-given-another-object" => [f.Create()],
            "field-of-a-ref-struct-parameter-given-a-member-of-a-new-object" or "another-call-handed-a-ref-struct-parameter-given-a-member-of-a-new-object" =>
                [FactHolderFragments.Member(f.Create(), stored)],
            "field-of-a-ref-struct-parameter-given-a-field" => [FactHolderFragments.Own(stored)],
            _ => [],
        };
        Expression receiver = returned is "field-of-a-receiver-that-is-another-object" or "another-call-on-a-receiver-that-is-another-object"
            or "another-call-handed-a-struct-field-of-a-receiver-that-is-another-object" or "field-of-an-extension-receiver-that-is-another-object"
            or "field-of-an-extension-receiver-reassigned-to-a-parameter-given-this" or "field-of-an-extension-receiver-handed-by-ref"
            or "field-of-a-parameter-of-an-extension-method-given-this" or "another-call-on-an-extension-receiver-that-is-another-object"
            or "a-recursive-call-on-a-receiver-that-is-another-object" or "a-mutually-recursive-call-on-a-receiver-that-is-another-object"
            or "a-recursive-call-handing-this-on-given-another-object" or "a-recursive-call-handing-its-parameter-on-given-this" ? f.Create() : f.This;

        var reach = ReturnedStorage.Of(FactHolderFragments.Call(receiver, callee, arguments), BodyHolders.None);

        Assert.Equal(reached, Covered(f, reach));
    }

    [Theory]
    [InlineData("field-of-this", "A,p,y")]
    [InlineData("field-named-through-this", "A,p,y")]
    [InlineData("static", "S,p,y")]
    [InlineData("member-of-a-parameter", "A,p,y")]
    [InlineData("member-of-a-call-result", "A,p,y")]
    [InlineData("member-of-a-field", "A,p,y")]
    [InlineData("member-of-a-local-copied-from-this", "A,p,y")]
    [InlineData("member-of-a-new-object", "")]
    [InlineData("member-of-a-local-only-ever-new", "")]
    [InlineData("member-of-a-local-only-ever-new-a-nested-function-reads", "p,y")]
    [InlineData("member-of-a-local-new-then-reassigned", "A,p,y")]
    [InlineData("member-of-a-struct-copy", "")]
    [InlineData("member-of-a-struct-copy-a-nested-function-reads", "p,y")]
    [InlineData("member-of-a-struct-held-by-a-struct-copy", "")]
    [InlineData("member-of-a-struct-held-by-a-field", "A,p,y")]
    [InlineData("member-of-an-object-held-by-a-struct-copy", "A,p,y")]
    [InlineData("member-of-a-by-value-struct-parameter", "")]
    [InlineData("member-of-a-by-value-struct-parameter-a-nested-function-reads", "p,y")]
    [InlineData("by-ref-parameter", "A,B,S,V,p,y")]
    [InlineData("out-parameter", "A,B,S,V,p,y")]
    [InlineData("ref-local-of-a-by-ref-parameter", "A,B,S,V,p,y")]
    [InlineData("ref-local-of-a-field", "A,p,y")]
    [InlineData("ref-local-bound-to-a-call-returning-a-field", "A,p,y")]
    [InlineData("ref-local-bound-to-a-call-returning-a-field-of-a-new-object", "")]
    [InlineData("ref-local-of-a-member-of-a-new-object", "A")]
    [InlineData("ref-local-of-a-member-of-another-object", "V,p,y")]
    [InlineData("ref-local-of-an-array-element", "p,y")]
    [InlineData("ref-local-of-an-element-of-an-array-created-here", "")]
    [InlineData("ref-local-of-an-element-a-ref-indexer-returns", "A,B,S,V,p,y")]
    [InlineData("array-element", "p,y")]
    [InlineData("element-a-ref-indexer-returns", "A,B,S,V,p,y")]
    [InlineData("element-of-an-array-created-here", "")]
    [InlineData("element-of-an-array-created-here-a-nested-function-reads", "p,y")]
    [InlineData("element-of-an-array-created-here-an-async-block-reads", "p,y")]
    [InlineData("element-of-a-new-array", "")]
    [InlineData("call-returning-a-field", "A,p,y")]
    [InlineData("local", "")]
    [InlineData("by-value-parameter", "")]
    public void AWriteReachesEveryHolderThatMayShareItsStorage(string target, string reached)
    {
        var f = new FactHolderFragments();
        var o = new LocalVariable("o", f.Pair);
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var x = f.ByRef("x");
        var getA = f.Returning("GetA", FactHolderFragments.Member(f.This, f.A));
        var self = new Method("Self", f.Pair, [], new MemberModifiers()) { DeclaringType = f.Pair };
        var other = f.AddField(f.Pair, "Other", type: f.Pair);
        var point = HandBuiltModel.Struct("Point");
        var copy = new LocalVariable("c", point);
        var held = new Field("In", point, default, new MemberModifiers()) { DeclaringType = point };
        var stored = f.AddField(f.Pair, "P", type: point);
        var value = new LocalVariable("v", point) { Parameter = FactHolderFragments.Parameter("v", point) };
        var array = new LocalVariable("arr", f.H.Sequence());
        var creation = f.Create();
        var linked = new Field("Owner", f.Pair, default, new MemberModifiers()) { DeclaringType = point };
        var viewed = new IndexExpression(HandBuiltModel.Node(), FactHolderFragments.Read(new LocalVariable("view", f.Span)), f.Number(0L), f.H.Int)
        {
            Indexer = new Indexer(f.H.Int, [FactHolderFragments.Parameter("index", f.H.Int)], new MemberModifiers { IsRef = true }),
        };
        List<Statement> before = [];
        Expression written;
        switch(target)
        {
            case "field-of-this":
                written = FactHolderFragments.Own(f.A);
                break;
            case "field-named-through-this":
                written = FactHolderFragments.Member(f.This, f.A);
                break;
            case "static":
                written = FactHolderFragments.Own(f.S);
                break;
            case "member-of-a-parameter":
                written = FactHolderFragments.Member(FactHolderFragments.Read(new LocalVariable("q", f.Pair) { Parameter = FactHolderFragments.Parameter("q", f.Pair) }), f.A);
                break;
            case "member-of-a-call-result":
                written = FactHolderFragments.Member(FactHolderFragments.Call(f.This, self), f.A);
                break;
            case "member-of-a-field":
                written = FactHolderFragments.Member(FactHolderFragments.Own(other), f.A);
                break;
            case "member-of-a-local-copied-from-this":
                before.Add(FactHolderFragments.Declare(o, f.This));
                written = FactHolderFragments.Member(FactHolderFragments.Read(o), f.A);
                break;
            case "member-of-a-new-object":
                written = FactHolderFragments.Member(creation, f.A);
                break;
            case "member-of-a-local-only-ever-new":
                before.Add(FactHolderFragments.Declare(o, creation));
                before.Add(FactHolderFragments.Set(FactHolderFragments.Read(o), f.Create()));
                written = FactHolderFragments.Member(FactHolderFragments.Read(o), f.A);
                break;
            case "member-of-a-local-only-ever-new-a-nested-function-reads":
                before.Add(FactHolderFragments.Declare(o, creation));
                before.Add(Reads(o));
                written = FactHolderFragments.Member(FactHolderFragments.Read(o), f.A);
                break;
            case "member-of-a-local-new-then-reassigned":
                before.Add(FactHolderFragments.Declare(o, creation));
                before.Add(FactHolderFragments.Set(FactHolderFragments.Read(o), f.This));
                written = FactHolderFragments.Member(FactHolderFragments.Read(o), f.A);
                break;
            case "member-of-a-struct-copy":
                before.Add(FactHolderFragments.Declare(copy, FactHolderFragments.Own(stored)));
                written = FactHolderFragments.Member(FactHolderFragments.Read(copy), f.A);
                break;
            case "member-of-a-struct-copy-a-nested-function-reads":
                before.Add(FactHolderFragments.Declare(copy, FactHolderFragments.Own(stored)));
                before.Add(Reads(copy));
                written = FactHolderFragments.Member(FactHolderFragments.Read(copy), f.A);
                break;
            case "member-of-a-struct-held-by-a-struct-copy":
                before.Add(FactHolderFragments.Declare(copy, FactHolderFragments.Own(stored)));
                written = FactHolderFragments.Member(FactHolderFragments.Member(FactHolderFragments.Read(copy), held), f.A);
                break;
            case "member-of-a-struct-held-by-a-field":
                written = FactHolderFragments.Member(FactHolderFragments.Member(FactHolderFragments.Own(stored), held), f.A);
                break;
            case "member-of-an-object-held-by-a-struct-copy":
                before.Add(FactHolderFragments.Declare(copy, FactHolderFragments.Own(stored)));
                written = FactHolderFragments.Member(FactHolderFragments.Member(FactHolderFragments.Read(copy), linked), f.A);
                break;
            case "member-of-a-by-value-struct-parameter":
                written = FactHolderFragments.Member(FactHolderFragments.Read(value), f.A);
                break;
            case "member-of-a-by-value-struct-parameter-a-nested-function-reads":
                before.Add(Reads(value));
                written = FactHolderFragments.Member(FactHolderFragments.Read(value), f.A);
                break;
            case "by-ref-parameter":
                written = FactHolderFragments.Read(x);
                break;
            case "out-parameter":
                written = FactHolderFragments.Read(f.ByRef("x", direction: ByRefDirection.Out));
                break;
            case "ref-local-of-a-by-ref-parameter":
                before.Add(FactHolderFragments.Declare(r, FactHolderFragments.Read(x)));
                written = FactHolderFragments.Read(r);
                break;
            case "ref-local-of-a-field":
                before.Add(FactHolderFragments.Declare(r, FactHolderFragments.Own(f.A)));
                written = FactHolderFragments.Read(r);
                break;
            case "ref-local-bound-to-a-call-returning-a-field":
                before.Add(FactHolderFragments.Declare(r, FactHolderFragments.Call(f.This, getA)));
                written = FactHolderFragments.Read(r);
                break;
            case "ref-local-bound-to-a-call-returning-a-field-of-a-new-object":
                before.Add(FactHolderFragments.Declare(r, FactHolderFragments.Call(creation, getA)));
                written = FactHolderFragments.Read(r);
                break;
            case "ref-local-of-a-member-of-a-new-object":
                before.Add(FactHolderFragments.Declare(r, FactHolderFragments.Member(creation, f.A)));
                written = FactHolderFragments.Read(r);
                break;
            case "ref-local-of-a-member-of-another-object":
                before.Add(FactHolderFragments.Declare(r, FactHolderFragments.Member(FactHolderFragments.Read(new LocalVariable("other", f.Box)), f.V)));
                written = FactHolderFragments.Read(r);
                break;
            case "ref-local-of-an-element-a-ref-indexer-returns":
                before.Add(FactHolderFragments.Declare(r, viewed));
                written = FactHolderFragments.Read(r);
                break;
            case "element-a-ref-indexer-returns":
                written = viewed;
                break;
            case "element-of-an-array-created-here-an-async-block-reads":
                before.Add(FactHolderFragments.Declare(array, f.H.Array(3L)));
                before.Add(HandBuiltRefinements.Evaluate(new AsyncBlockExpression(HandBuiltModel.Node(), HandBuiltModel.Block(HandBuiltRefinements.Evaluate(FactHolderFragments.Read(array))),
                    f.H.Int, false)));
                written = f.H.At(FactHolderFragments.Read(array), f.Number(0L));
                break;
            case "ref-local-of-an-element-of-an-array-created-here":
                before.Add(FactHolderFragments.Declare(array, f.H.Array(3L)));
                before.Add(FactHolderFragments.Declare(r, f.H.At(FactHolderFragments.Read(array), f.Number(0L))));
                written = FactHolderFragments.Read(r);
                break;
            case "ref-local-of-an-array-element":
                before.Add(FactHolderFragments.Declare(r, f.H.At(FactHolderFragments.Read(array), f.Number(0L))));
                written = FactHolderFragments.Read(r);
                break;
            case "array-element":
                written = f.H.At(FactHolderFragments.Read(array), f.Number(0L));
                break;
            case "element-of-an-array-created-here":
                before.Add(FactHolderFragments.Declare(array, f.H.Array(3L)));
                written = f.H.At(FactHolderFragments.Read(array), f.Number(0L));
                break;
            case "element-of-an-array-created-here-a-nested-function-reads":
                before.Add(FactHolderFragments.Declare(array, f.H.Array(3L)));
                before.Add(Reads(array));
                written = f.H.At(FactHolderFragments.Read(array), f.Number(0L));
                break;
            case "element-of-a-new-array":
                written = f.H.At(f.H.Array(3L), f.Number(0L));
                break;
            case "call-returning-a-field":
                written = FactHolderFragments.Call(f.This, getA);
                break;
            case "local":
                written = FactHolderFragments.Read(FactHolderFragments.Int(f.H, "a"));
                break;
            case "by-value-parameter":
                written = FactHolderFragments.Read(new LocalVariable("v", f.H.Int) { Parameter = FactHolderFragments.Parameter("v", f.H.Int) });
                break;
            default:
                throw new InvalidOperationException(target);
        }

        var holders = BodyHolders.Of(HandBuiltModel.Block([.. before, FactHolderFragments.Set(written, f.Number(1L))]));

        Assert.Equal(reached, Covered(f, holders.Reach(written)));
    }

    [Theory]
    [InlineData("a-local-handed-to-it-by-ref", true)]
    [InlineData("a-local-handed-through-a-ref-local", true)]
    [InlineData("a-local-never-handed-by-ref", false)]
    [InlineData("a-local-handed-only-readonly", false)]
    [InlineData("a-local-of-another-closed-type", false)]
    [InlineData("a-local-the-function-declares", false)]
    [InlineData("a-ref-parameter-of-the-body-itself", false)]
    public void AWriteThroughANestedFunctionsRefParameterReachesALocalOutsideItThatMayBeItsStorage(string form, bool reached)
    {
        var f = new FactHolderFragments();
        var a = form == "a-local-of-another-closed-type" ? new LocalVariable("a", f.H.Built.Type("Int64")) : FactHolderFragments.Int(f.H, "a");
        var b = FactHolderFragments.Int(f.H, "b");
        var r = FactHolderFragments.Int(f.H, "r", isRef: true);
        var p = f.ByRef("p");
        var own = form == "a-local-the-function-declares";
        List<Statement> inner = own ? [FactHolderFragments.Declare(a, f.Number(5L)), Reads(a), HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(a)))]
            : [HandBuiltRefinements.Evaluate(FactHolderFragments.Read(a))];
        var w = f.Method("W", f.Pair, HandBuiltModel.Block([.. inner, FactHolderFragments.Set(FactHolderFragments.Read(p), f.Number(1L))]), f.H.Int, p.Parameter!);
        List<Statement> body = [FactHolderFragments.Declare(b, f.Number(0L)), new LocalFunctionStatement(w, [p])];
        if(!own)
        {
            body.Insert(0, FactHolderFragments.Declare(a, f.Number(5L)));
        }

        body.AddRange(form switch
        {
            "a-local-handed-to-it-by-ref" or "a-local-of-another-closed-type" => [HandBuiltRefinements.Evaluate(f.Free(w, FactHolderFragments.Read(a)))],
            "a-local-handed-through-a-ref-local" => [FactHolderFragments.Declare(r, FactHolderFragments.Read(a)), HandBuiltRefinements.Evaluate(f.Free(w, FactHolderFragments.Read(r)))],
            "a-local-handed-only-readonly" => [HandBuiltRefinements.Evaluate(f.Free(f.Viewing(f.H.Int, ByRefDirection.In), FactHolderFragments.Read(a))),
                HandBuiltRefinements.Evaluate(f.Free(w, FactHolderFragments.Read(b)))],
            "a-ref-parameter-of-the-body-itself" => [HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(a)))],
            _ => [HandBuiltRefinements.Evaluate(f.Free(w, FactHolderFragments.Read(b)))],
        });
        var written = form == "a-ref-parameter-of-the-body-itself" ? f.ByRef("q") : p;

        var holders = BodyHolders.Of(HandBuiltModel.Block([.. body]));

        Assert.Equal(reached, holders.Reach(FactHolderFragments.Read(written)).Covers(a));
    }

    [Fact]
    public void AWriteThroughANestedFunctionsRefParameterReachesAStructLocalHandedToItThroughAReferenceItsOwnMethodReturns()
    {
        var f = new FactHolderFragments();
        var point = HandBuiltModel.Struct("Point");
        var s = new LocalVariable("s", point);
        var r = new LocalVariable("r", point) { IsRef = true };
        var p = f.ByRef("p", point);
        var own = new Method("Own", point, [], new MemberModifiers { IsRef = true }) { DeclaringType = point };
        var w = f.Method("W", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(FactHolderFragments.Read(s))), f.H.Int, p.Parameter!);

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(s, null), new LocalFunctionStatement(w, [p]),
            FactHolderFragments.Declare(r, FactHolderFragments.Call(FactHolderFragments.Read(s), own)), HandBuiltRefinements.Evaluate(f.Free(w, FactHolderFragments.Read(r)))));

        Assert.True(holders.Reach(FactHolderFragments.Read(p)).Covers(s));
    }

    [Theory]
    [InlineData(ByRefDirection.Ref, true, true)]
    [InlineData(ByRefDirection.In, true, true)]
    [InlineData(ByRefDirection.Ref, false, false)]
    public void AWriteToALocalANestedFunctionReadsReachesThatFunctionsRefParametersItMayBeHandedTo(ByRefDirection direction, bool handed, bool reached)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var b = FactHolderFragments.Int(f.H, "b");
        var p = f.ByRef("p", direction: direction);
        var w = f.Method("W", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(FactHolderFragments.Read(a))), f.H.Int, p.Parameter!);

        var holders = BodyHolders.Of(HandBuiltModel.Block(FactHolderFragments.Declare(a, f.Number(5L)), FactHolderFragments.Declare(b, f.Number(0L)),
            new LocalFunctionStatement(w, [p]), HandBuiltRefinements.Evaluate(f.Free(w, FactHolderFragments.Read(handed ? a : b)))));

        Assert.Equal(reached, holders.Reach(FactHolderFragments.Read(a)).Covers(p));
    }

    [Theory]
    [InlineData("writes-the-local", true)]
    [InlineData("hands-the-local-by-ref", true)]
    [InlineData("calls-a-local-function-that-writes-the-local", true)]
    [InlineData("is-declared-before-a-local-function-it-calls-that-calls-one-that-writes-the-local", true)]
    [InlineData("writes-another-local", false)]
    [InlineData("is-a-method", false)]
    public void ACallToALocalFunctionReachesTheRefParametersThatMayNameALocalItWrites(string form, bool reached)
    {
        var f = new FactHolderFragments();
        var a = FactHolderFragments.Int(f.H, "a");
        var b = FactHolderFragments.Int(f.H, "b");
        var p = f.ByRef("p");
        var m = f.Method("M", f.Pair, HandBuiltModel.Block(FactHolderFragments.Set(FactHolderFragments.Read(p), f.Number(5L))), f.H.Int, p.Parameter!);
        var write = f.Method("Inc", f.Pair, HandBuiltModel.Block(form switch
        {
            "hands-the-local-by-ref" => HandBuiltRefinements.Evaluate(f.Free(f.Bump(), FactHolderFragments.Read(a))),
            "writes-another-local" => FactHolderFragments.Set(FactHolderFragments.Read(b), f.Number(1L)),
            _ => FactHolderFragments.Set(FactHolderFragments.Read(a), f.Number(1L)),
        }));
        var outer = f.Method("Outer", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(f.Free(write))));
        var top = f.Method("Top", f.Pair, HandBuiltModel.Block(HandBuiltRefinements.Evaluate(f.Free(outer))));
        var first = form == "is-declared-before-a-local-function-it-calls-that-calls-one-that-writes-the-local";
        List<Statement> body = [FactHolderFragments.Declare(a, f.Number(0L)), FactHolderFragments.Declare(b, f.Number(0L)), new LocalFunctionStatement(m, [p]),
            HandBuiltRefinements.Evaluate(f.Free(m, FactHolderFragments.Read(a)))];
        if(first)
        {
            body.Add(new LocalFunctionStatement(top, []));
        }

        if(form != "is-a-method")
        {
            body.Add(new LocalFunctionStatement(write, []));
            body.Add(new LocalFunctionStatement(outer, []));
        }

        var holders = BodyHolders.Of(HandBuiltModel.Block([.. body]));

        Assert.Equal(reached, holders.Called(f.Free(first ? top : form == "calls-a-local-function-that-writes-the-local" ? outer : write)).Covers(p));
    }

    [Theory]
    [InlineData("Int32", "Int32", true)]
    [InlineData("Int32", "Int64", false)]
    [InlineData("refined-Int32", "Int32", true)]
    [InlineData("type-parameter", "Int64", true)]
    public void StorageOfOneClosedTypeIsNeverAnothers(string first, string second, bool shared)
    {
        var f = new FactHolderFragments();
        IMistType type(string name)
        {
            return name switch
            {
                "refined-Int32" => f.Five,
                "type-parameter" => new TypeParameter("T", 0, new ProgramModel.Model.Scopes.Scope(null), null),
                _ => f.H.Built.Type(name),
            };
        }

        Assert.Equal(shared, WriteReach.MayShare(type(first), type(second)));
    }

    private static ExpressionStatement Reads(LocalVariable local)
    {
        var read = FactHolderFragments.Read(local);
        return HandBuiltRefinements.Evaluate(new LambdaExpression(HandBuiltModel.Node(), [], read, null, false, new FunctionType([], read.Type)));
    }

    private static Method AbstractCopy(Method method)
    {
        var contract = new AbstractType("IPair", "Probe", new ProgramModel.Model.Scopes.Scope(null), null, null);
        return new Method(method.Name, method.ReturnType, [], method.Modifiers) { Body = method.Body, DeclaringType = contract };
    }

    private static string Covered(FactHolderFragments f, WriteReach reach)
    {
        var y = f.ByRef("y");
        var p = f.ByRef("p");
        var wide = f.ByRef("q", f.H.Built.Type("Int64"));
        var l = f.AddField(f.Pair, "L", type: f.H.Built.Type("Int64"));
        object[] holders = [f.A, f.B, f.S, l, f.V, p, y, wide, FactHolderFragments.Int(f.H, "a")];
        return string.Join(",", holders.Where(reach.Covers).Select(holder => holder switch
        {
            LocalVariable local => local.Name,
            Field field => field.Name,
            _ => throw new InvalidOperationException(holder.GetType().Name),
        }).Order(StringComparer.Ordinal));
    }
}