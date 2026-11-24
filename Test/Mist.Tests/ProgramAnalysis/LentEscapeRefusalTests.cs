using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LendFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class LentEscapeRefusalTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("S1", "store borrowed 'x' in 'Hook.S'")]
    [InlineData("S2", "store frozen 'x' in 'Hook.S'")]
    [InlineData("S3", "store readonly ref 'x' in 'Hook.S'")]
    [InlineData("S4", "store borrowed 'x' in 'Hook.S'")]
    [InlineData("S5", "pass borrowed 'x' to unrestricted parameter 'p' of 'Store'")]
    [InlineData("S6", "return borrowed 'x' from 'Leak'")]
    [InlineData("S7", "pass borrowed 'x' to unrestricted parameter 'item' of 'Add'")]
    [InlineData("S8", "store borrowed 'x' in an element of 'a'")]
    [InlineData("S9", "store borrowed 'x' in 'X'")]
    [InlineData("S10", "store borrowed 'x' in 'X'")]
    [InlineData("S11", "store borrowed 'x' in 'h.X'")]
    [InlineData("S12", "store borrowed 'x' in 'Hook.S'")]
    [InlineData("S13", "store borrowed 'o' in 'Hook.S'")]
    [InlineData("out parameter", "store borrowed 'x' in parameter 'y', which its caller holds")]
    [InlineData("array literal", "store borrowed 'x' in an array")]
    [InlineData("tuple", "store borrowed 'x' in a tuple")]
    [InlineData("deconstructed", "store borrowed 'x' in 'Hook.S'")]
    [InlineData("reassigned alias", "store borrowed 'x' in 'Hook.S'")]
    [InlineData("pattern", "store borrowed 'x' in 'Hook.S'")]
    [InlineData("function value", "pass borrowed 'x' to a function value")]
    [InlineData("object initializer", "store borrowed 'x' in 'X'")]
    [InlineData("coalescing", "store borrowed 'x' in 'Hook.S'")]
    [InlineData("ref local", "store borrowed 'x' in 'r', which refers to 'Hook.S'")]
    [InlineData("owned field of a lent holder", "store borrowed 'p' in 'Hook.S'")]
    [InlineData("constructor argument", "pass borrowed 'x' to unrestricted parameter 'p' of 'W'")]
    [InlineData("chain argument", "pass borrowed 'x' to unrestricted parameter 'p' of 'W'")]
    [InlineData("frozen chain argument", "pass frozen 'x' to unrestricted parameter 'p' of 'W'")]
    [InlineData("captured outer local", "store borrowed 'x' in 'kept', declared outside this closure")]
    [InlineData("captured outer local through an alias", "store borrowed 'x' in 'kept', declared outside this closure")]
    [InlineData("local outliving the lend's block", "store borrowed 'o' in 'kept', declared outside the block that holds 'o'")]
    [InlineData("local a closure captures", "store borrowed 'x' in 't', which a closure captures")]
    [InlineData("alias captured by a lambda", "capture borrowed 'x' in a lambda")]
    [InlineData("alias captured by a local function", "capture borrowed 'x' in a local function")]
    public void ALentValueCannotGetOut(string program, string message)
    {
        var (root, owner, member) = Program(program);

        var diagnostic = Assert.Single(f.Analyze(new OwnershipAnalyzer(), root, owner, member));

        Assert.Equal("MIST0508", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.StartsWith($"Cannot {message}:", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C1")]
    [InlineData("C2")]
    [InlineData("C3")]
    [InlineData("clone")]
    [InlineData("alias given a fresh value")]
    [InlineData("frozen and readonly ref re-lends")]
    [InlineData("ref parameter")]
    [InlineData("chain argument lent on")]
    [InlineData("local declared inside the closure")]
    [InlineData("alias in the lend's block")]
    [InlineData("lambda capturing an unrelated local")]
    public void ALentValueStaysAdmittedWhereItIsOnlyLentOnOrRead(string program)
    {
        var (root, owner, member) = Program(program);

        Assert.Empty(f.Analyze(new OwnershipAnalyzer(), root, owner, member));
    }

    private (object Root, MistType? Owner, object? Member) Program(string program)
    {
        var tier = program switch { "S2" or "frozen chain argument" => "frozen", "S3" => "readonly ref", _ => "borrowed" };
        var x = Parameter("x", f.Box, Tier(tier));
        var hook = Static(f.S);
        Block keep;
        var extra = new List<LocalVariable>();
        var functions = new List<Statement>();
        switch(program)
        {
            case "S1" or "S2" or "S3" or "S12":
                keep = HandBuiltModel.Block(Run(Store(hook, Ref(x, 37))));
                break;
            case "S4":
                var y = new LocalVariable("y", f.Box) { InfersOwnership = true };
                keep = HandBuiltModel.Block(Declare(y, Ref(x)), Run(Store(hook, Ref(y, 37))));
                break;
            case "S5":
                var p = Parameter("p", f.Box, default);
                var store = Local("Store", f.Model.Type("Void"), HandBuiltModel.Block(Run(Store(Static(f.S), Ref(p)))), p);
                functions.Add(store);
                keep = HandBuiltModel.Block(Run(Call(store.Function, null, 0, Ref(x, 37))));
                break;
            case "S6":
                var leak = Local("Leak", f.Box, HandBuiltModel.Block(new ReturnStatement(Ref(x, 37), f.Box)), x);
                var b6 = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
                return (HandBuiltModel.Block(leak, Declare(b6, Fresh(f.Box)), Run(Store(hook, Call(leak.Function, null, 0, Ref(b6))))), null, null);
            case "S7":
                var list = Member(f.Hook, "L", f.Boxes, isStatic: true);
                keep = HandBuiltModel.Block(Run(Call(f.Add, Static(list), 0, Ref(x, 37))));
                break;
            case "S8":
                var a = Parameter("a", f.BoxArray, default);
                extra.Add(a);
                keep = HandBuiltModel.Block(Run(Store(f.Element(Ref(a)), Ref(x, 37))));
                break;
            case "S9" or "S10":
                var w = HandBuiltModel.Class("W");
                var field = Member(w, "X", f.Box);
                var body = HandBuiltModel.Block(Run(Store(new FieldReferenceExpression(HandBuiltModel.Node(), field), Ref(x, 37))));
                object declared = program == "S9"
                    ? new Constructor("W", w, [x.Parameter!], default) { Body = body, DeclaringType = w }
                    : new Method("Keep", f.Model.Type("Void"), [x.Parameter!], default) { Body = body, DeclaringType = w };
                return (body, w, declared);
            case "S11":
                var h = Parameter("h", f.Holder, default);
                extra.Add(h);
                keep = HandBuiltModel.Block(Run(Store(Access(Ref(h), Member(f.Holder, "X", f.Box)), Ref(x, 37))));
                break;
            case "S13":
                var b13 = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
                var o = new LocalVariable("o", f.Box) { Ownership = OwnershipKind.Borrowed };
                return (HandBuiltModel.Block(Declare(b13, Fresh(f.Box)), Declare(o, Ref(b13)), Run(Store(hook, Ref(o, 37)))), null, null);
            case "out parameter":
                var outward = Parameter("y", f.Box, Tier("out"));
                extra.Add(outward);
                keep = HandBuiltModel.Block(Run(Store(Ref(outward), Ref(x, 37))));
                break;
            case "array literal":
                keep = HandBuiltModel.Block(Run(new ArrayLiteralExpression(HandBuiltModel.Node(), f.Box, [Ref(x, 37)], f.BoxArray)));
                break;
            case "tuple":
                var pair = f.Model.Type("(Box, Int32)");
                keep = HandBuiltModel.Block(Declare(new LocalVariable("t", pair), new TupleLiteralExpression(HandBuiltModel.Node(), [Ref(x, 37), HandBuiltModel.Literal(f.Int32, 1L)], [null, null], pair)));
                break;
            case "deconstructed":
                var tuple = f.Model.Type("(Box, Int32)");
                var first = new LocalVariable("y", f.Box);
                var second = new LocalVariable("n", f.Int32);
                var literal = new TupleLiteralExpression(HandBuiltModel.Node(), [Ref(x), HandBuiltModel.Literal(f.Int32, 1L)], [null, null], tuple);
                keep = HandBuiltModel.Block(new TupleDeconstructionStatement([first, second], literal), Run(Store(hook, Ref(first, 37))));
                break;
            case "reassigned alias" or "alias given a fresh value":
                var alias = new LocalVariable("y", f.Box);
                var given = program == "reassigned alias" ? (Expression)Ref(x) : Fresh(f.Box);
                var initial = program == "reassigned alias" ? (Expression)Fresh(f.Box) : Ref(x);
                keep = HandBuiltModel.Block(Declare(alias, initial), Run(Store(Ref(alias), given)), Run(Store(hook, Ref(alias, 37))));
                break;
            case "pattern":
                var bound = new LocalVariable("y", f.Box);
                var test = new IsPatternExpression(HandBuiltModel.Node(), Ref(x), new TypeTestPattern(HandBuiltModel.Node(), f.Box, bound), f.Model.Type("Boolean"));
                keep = HandBuiltModel.Block(new IfStatement(test, HandBuiltModel.Block(Run(Store(hook, Ref(bound, 37)))), null));
                break;
            case "function value":
                var action = f.Model.Interner.GetOrCreateFunctionType([f.Box], f.Model.Type("Void"));
                var callback = Parameter("g", action, default);
                extra.Add(callback);
                keep = HandBuiltModel.Block(Run(new InvocationExpression(HandBuiltModel.Node(), Ref(callback), [Ref(x, 37)], f.Model.Type("Void"), null, null)));
                break;
            case "object initializer":
                var holder = HandBuiltModel.Class("H");
                var kept = Member(holder, "X", f.Box);
                var entry = new ObjectInitializerAssignment(HandBuiltModel.Node(), "X", Ref(x, 37), f.Box, member: new FieldResult(kept, holder, []));
                keep = HandBuiltModel.Block(Run(new NewExpression(HandBuiltModel.Node(), holder, [], null, [entry], [])));
                break;
            case "coalescing":
                keep = HandBuiltModel.Block(Run(new AssignmentExpression(HandBuiltModel.Node(), hook, Ref(x, 37), f.Box, isCoalescing: true)));
                break;
            case "ref local":
                var r = new LocalVariable("r", f.Box) { IsRef = true };
                keep = HandBuiltModel.Block(Declare(r, Static(f.S)), Run(Store(Ref(r), Ref(x, 37))));
                break;
            case "owned field of a lent holder":
                var lent = Parameter("p", f.Holder, Tier("borrowed"));
                x = lent;
                keep = HandBuiltModel.Block(Run(Store(hook, Access(Ref(lent), f.Kept, 37))));
                break;
            case "constructor argument":
                var made = HandBuiltModel.Class("W");
                var constructor = new Constructor("W", made, [new Parameter("p", f.Box)], default) { DeclaringType = made };
                keep = HandBuiltModel.Block(Run(Fresh(made, constructor, 0, Ref(x, 37))));
                break;
            case "chain argument" or "frozen chain argument" or "chain argument lent on":
                var based = HandBuiltModel.Class("W");
                var lentOn = program == "chain argument lent on";
                var chainTarget = new Constructor("W", based, [new Parameter("p", f.Box, lentOn ? Tier("borrowed") : default)], default) { DeclaringType = based };
                var chainArgument = Ref(x, 37);
                return (chainArgument, based, Chained(HandBuiltModel.Class("W2"), chainTarget, [x], chainArgument));
            case "captured outer local" or "captured outer local through an alias" or "local declared inside the closure":
                var outerKept = new LocalVariable("kept", f.Box);
                var throughLocal = new LocalVariable("y", f.Box);
                var insideLocal = new LocalVariable("t", f.Box);
                Statement[] stores = program switch
                {
                    "captured outer local" => [Run(Store(Ref(outerKept), Ref(x, 37)))],
                    "captured outer local through an alias" => [Declare(throughLocal, Ref(x)), Run(Store(Ref(outerKept), Ref(throughLocal, 37)))],
                    _ => [Declare(insideLocal, Fresh(f.Box)), Run(Store(Ref(insideLocal), Ref(x)))],
                };
                var capturing = Local("Keep", f.Model.Type("Void"), HandBuiltModel.Block(stores), isStatic: false, x);
                return (HandBuiltModel.Block(Declare(outerKept, Fresh(f.Box)), capturing), null, null);
            case "local outliving the lend's block" or "alias in the lend's block":
                var lends = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
                var borrowed = new LocalVariable("o", f.Box) { Ownership = OwnershipKind.Borrowed };
                var outerLocal = new LocalVariable("kept", f.Box);
                var innerLocal = new LocalVariable("t", f.Box);
                var lentBlock = program == "local outliving the lend's block"
                    ? HandBuiltModel.Block(Declare(borrowed, Ref(lends)), Run(Store(Ref(outerLocal), Ref(borrowed, 37))))
                    : HandBuiltModel.Block(Declare(borrowed, Ref(lends)), Declare(innerLocal, Fresh(f.Box)), Run(Store(Ref(innerLocal), Ref(borrowed))));
                return (HandBuiltModel.Block(Declare(lends, Fresh(f.Box)), Declare(outerLocal, Fresh(f.Box)), lentBlock), null, null);
            case "local a closure captures":
                var seen = new LocalVariable("t", f.Box);
                keep = HandBuiltModel.Block(Declare(seen, Fresh(f.Box)), Run(f.Lambda(Access(Ref(seen), f.V))), Run(Store(Ref(seen), Ref(x, 37))));
                break;
            case "alias captured by a lambda" or "alias captured by a local function" or "lambda capturing an unrelated local":
                var viewer = new LocalVariable("y", f.Box);
                var viewRead = Access(Ref(viewer, 37), f.V);
                Statement closureStatement = program == "alias captured by a local function"
                    ? Local("Look", f.Int32, HandBuiltModel.Block(new ReturnStatement(viewRead, f.Int32)), isStatic: false)
                    : Run(f.Lambda(viewRead));
                keep = HandBuiltModel.Block(Declare(viewer, program == "lambda capturing an unrelated local" ? Fresh(f.Box) : Ref(x)), closureStatement);
                break;
            case "C1":
                keep = HandBuiltModel.Block(Run(Store(Access(Ref(x), f.V), HandBuiltModel.Literal(f.Int32, 5L))), Declare(new LocalVariable("n", f.Int32) { InfersOwnership = true }, Access(Ref(x), f.V)));
                break;
            case "C2":
                var inner = Parameter("y", f.Box, Tier("borrowed"));
                var lend = Local("Inner", f.Model.Type("Void"), HandBuiltModel.Block(Run(Store(Access(Ref(inner), f.V), HandBuiltModel.Literal(f.Int32, 4L)))), inner);
                functions.Add(lend);
                keep = HandBuiltModel.Block(Run(Call(lend.Function, null, 0, Ref(x))));
                break;
            case "C3":
                keep = HandBuiltModel.Block(Run(Store(Access(Static(f.S), f.V), Access(Ref(x), f.V))));
                break;
            case "clone":
                keep = HandBuiltModel.Block(Run(Store(hook, new CloneExpression(HandBuiltModel.Node(), Ref(x)))));
                break;
            case "frozen and readonly ref re-lends":
                var frozen = Parameter("y", f.Box, Tier("frozen"));
                var view = Parameter("z", f.Box, Tier("readonly ref"));
                var freeze = Local("Freeze", f.Model.Type("Void"), HandBuiltModel.Block(), frozen);
                var look = Local("Look", f.Model.Type("Void"), HandBuiltModel.Block(), view);
                functions.AddRange([freeze, look]);
                keep = HandBuiltModel.Block(Run(Call(freeze.Function, null, 0, Ref(x))), Run(Call(look.Function, null, 0, Ref(x))));
                break;
            default:
                var byRef = Parameter("y", f.Box, Tier("ref"));
                var swap = Local("Swap", f.Model.Type("Void"), HandBuiltModel.Block(), byRef);
                functions.Add(swap);
                keep = HandBuiltModel.Block(Run(Call(swap.Function, null, 0, Ref(x))));
                break;
        }

        var keeper = Local("Keep", f.Model.Type("Void"), keep, [x, .. extra]);
        var lender = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
        if(program == "S12")
        {
            var owned = Parameter("b", f.Box, Tier("owned"));
            var outer = Local("Outer", f.Model.Type("Void"), HandBuiltModel.Block(Run(Call(keeper.Function, null, 0, Ref(owned)))), owned);
            return (HandBuiltModel.Block([.. functions, keeper, outer, Run(Call(outer.Function, null, 0, Fresh(f.Box)))]), null, null);
        }

        return (HandBuiltModel.Block([.. functions, keeper, Declare(lender, Fresh(f.Box))]), null, null);
    }
}