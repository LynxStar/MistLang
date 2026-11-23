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
    [InlineData("its-ref-parameter", "p,y")]
    [InlineData("array-element", "p,y")]
    [InlineData("another-call-returning-a-field", "A,p,y")]
    [InlineData("a-field-handed-to-another-call", "B,p,y")]
    [InlineData("no-body", "A,B,S,V,p,y")]
    [InlineData("an-abstract-method", "A,B,S,V,p,y")]
    [InlineData("field-of-a-receiver-that-is-another-object", "p,y")]
    [InlineData("field-of-a-parameter-given-another-object", "p,y")]
    [InlineData("field-of-a-parameter-given-this", "A,p,y")]
    public void AReferenceACallReturnsReachesWhatItsBodyReturnsAndEveryByRefParameterOfItsType(string returned, string reached)
    {
        var f = new FactHolderFragments();
        var x = f.ByRef("x");
        var getA = f.Returning("GetA", FactHolderFragments.Member(f.This, f.A));
        var pass = f.Returning("Pass", FactHolderFragments.Read(x), x.Parameter!);
        var owner = new LocalVariable("owner", f.Pair) { Parameter = FactHolderFragments.Parameter("owner", f.Pair) };
        var through = f.Returning("Through", FactHolderFragments.Member(FactHolderFragments.Read(owner), f.A), owner.Parameter!);
        var callee = returned switch
        {
            "field-of-this" => getA,
            "field-of-another-object" => f.Returning("GetV", FactHolderFragments.Member(FactHolderFragments.Read(new LocalVariable("other", f.Box)), f.V)),
            "static" => f.Returning("GetS", FactHolderFragments.Own(f.S)),
            "its-ref-parameter" => pass,
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
            "its-ref-parameter" => [FactHolderFragments.Read(FactHolderFragments.Int(f.H, "a"))],
            "field-of-a-parameter-given-another-object" => [f.Create()],
            "field-of-a-parameter-given-this" => [f.This],
            _ => [],
        };
        Expression receiver = returned == "field-of-a-receiver-that-is-another-object" ? f.Create() : f.This;

        var reach = ReturnedStorage.Of(FactHolderFragments.Call(receiver, callee, arguments), candidate => candidate is NewExpression);

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
    [InlineData("member-of-a-new-object", "p,y")]
    [InlineData("member-of-a-local-only-ever-new", "p,y")]
    [InlineData("member-of-a-local-new-then-reassigned", "A,p,y")]
    [InlineData("member-of-a-struct-copy", "p,y")]
    [InlineData("by-ref-parameter", "A,B,S,V,p,y")]
    [InlineData("out-parameter", "A,B,S,V,p,y")]
    [InlineData("ref-local-of-a-by-ref-parameter", "A,B,S,V,p,y")]
    [InlineData("ref-local-of-a-field", "A,p,y")]
    [InlineData("ref-local-bound-to-a-call-returning-a-field", "A,p,y")]
    [InlineData("ref-local-of-an-array-element", "p,y")]
    [InlineData("array-element", "p,y")]
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
        var copy = new LocalVariable("c", HandBuiltModel.Struct("Point"));
        var array = new LocalVariable("arr", f.H.Sequence());
        var creation = f.Create();
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
            case "member-of-a-local-new-then-reassigned":
                before.Add(FactHolderFragments.Declare(o, creation));
                before.Add(FactHolderFragments.Set(FactHolderFragments.Read(o), f.This));
                written = FactHolderFragments.Member(FactHolderFragments.Read(o), f.A);
                break;
            case "member-of-a-struct-copy":
                written = FactHolderFragments.Member(FactHolderFragments.Read(copy), f.A);
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
            case "ref-local-of-an-array-element":
                before.Add(FactHolderFragments.Declare(r, f.H.At(FactHolderFragments.Read(array), f.Number(0L))));
                written = FactHolderFragments.Read(r);
                break;
            case "array-element":
                written = f.H.At(FactHolderFragments.Read(array), f.Number(0L));
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