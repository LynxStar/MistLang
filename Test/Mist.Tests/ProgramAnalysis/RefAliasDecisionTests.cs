using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LendFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RefAliasDecisionTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("ref local of an owned local", "owned binding", "b")]
    [InlineData("ref local of a ref local of an owned local", "owned binding", "b")]
    [InlineData("ref local of an owned field", "owned field", "p.Kept")]
    [InlineData("ref return of an owned field", "owned field", "p.Kept")]
    [InlineData("ref return through a nested ref return", "owned field", "p.Kept")]
    [InlineData("ref local of a ref return of an owned field", "owned field", "p.Kept")]
    [InlineData("static ref return of a parameter's owned field", "owned field", "p.Kept")]
    [InlineData("ref return of an owned field of this", "owned field", "Kept")]
    [InlineData("ref return of a ref parameter holding a ref local", "owned binding", "b")]
    public void ARefLocalOrRefReturnIsJudgedAsTheOwnedStorageItStandsFor(string argument, string kind, string name)
    {
        var (passed, root) = Argument(argument);

        Assert.Equal((kind, name), TierAnalyzer.PassedByBareRef(passed, Tier("ref"), new CloneDerivation([]), root));
    }

    [Theory]
    [InlineData("ref local of an owned int")]
    [InlineData("ref local of an unrestricted local")]
    [InlineData("ref return of a plain field")]
    [InlineData("ref return of a method with no body")]
    [InlineData("ref return of a parameter the callee reassigns")]
    [InlineData("recursive ref return")]
    public void ARefLocalOrRefReturnThatReachesNoOwnedStorageThatCanChangeIsPassed(string argument)
    {
        var (passed, root) = Argument(argument);

        Assert.Null(TierAnalyzer.PassedByBareRef(passed, Tier("ref"), new CloneDerivation([]), root));
    }

    private (Expression Argument, Block Root) Argument(string argument)
    {
        var b = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
        var p = new LocalVariable("p", f.Holder) { Ownership = OwnershipKind.Owned };
        var r = new LocalVariable("r", f.Box) { IsRef = true };
        Statement declareB = Declare(b, Fresh(f.Box));
        Statement declareP = Declare(p, Fresh(f.Holder));
        var get = Returning("Get", Access(This(), f.Kept));

        switch(argument)
        {
            case "ref local of an owned local":
                return (Ref(r), HandBuiltModel.Block(declareB, Declare(r, Ref(b))));
            case "ref local of a ref local of an owned local":
                var r2 = new LocalVariable("r2", f.Box) { IsRef = true };
                return (Ref(r2), HandBuiltModel.Block(declareB, Declare(r, Ref(b)), Declare(r2, Ref(r))));
            case "ref local of an owned field":
                return (Ref(r), HandBuiltModel.Block(declareP, Declare(r, Access(Ref(p), f.Kept))));
            case "ref return of an owned field":
                return (Call(get, Ref(p)), HandBuiltModel.Block(declareP));
            case "ref return through a nested ref return":
                return (Call(Returning("Get2", Call(get, This())), Ref(p)), HandBuiltModel.Block(declareP));
            case "ref local of a ref return of an owned field":
                return (Ref(r), HandBuiltModel.Block(declareP, Declare(r, Call(get, Ref(p)))));
            case "static ref return of a parameter's owned field":
                var q = Parameter("q", f.Holder, default);
                return (Call(Returning("Of", Access(Ref(q), f.Kept), isStatic: true, parameters: [q]), null, 0, Ref(p)), HandBuiltModel.Block(declareP));
            case "ref return of an owned field of this":
                return (Call(get), HandBuiltModel.Block());
            case "ref return of a ref parameter holding a ref local":
                var x = Parameter("x", f.Box, Tier("ref"));
                return (Call(Returning("Pass", Ref(x), isStatic: true, parameters: [x]), null, 0, Ref(r)), HandBuiltModel.Block(declareB, Declare(r, Ref(b))));
            case "ref local of an owned int":
                var n = new LocalVariable("n", Int32) { Ownership = OwnershipKind.Owned };
                var rn = new LocalVariable("rn", Int32) { IsRef = true };
                return (Ref(rn), HandBuiltModel.Block(Declare(n, HandBuiltModel.Literal(Int32, 5L)), Declare(rn, Ref(n))));
            case "ref local of an unrestricted local":
                var u = new LocalVariable("u", f.Box);
                return (Ref(r), HandBuiltModel.Block(Declare(u, Fresh(f.Box)), Declare(r, Ref(u))));
            case "ref return of a plain field":
                return (Call(Returning("Plain", Access(This(), f.In)), Ref(p)), HandBuiltModel.Block(declareP));
            case "ref return of a method with no body":
                var bodiless = new Method("Get", f.Box, [], new MemberModifiers { IsRef = true }) { DeclaringType = f.Holder };
                return (Call(bodiless, Ref(p)), HandBuiltModel.Block(declareP));
            case "ref return of a parameter the callee reassigns":
                var reassigned = Parameter("q", f.Holder, default);
                var of = Returning("Of", Access(Ref(reassigned), f.Kept), isStatic: true, parameters: [reassigned], Run(Store(Ref(reassigned), Fresh(f.Holder))));
                return (Call(of, null, 0, Ref(p)), HandBuiltModel.Block(declareP));
            default:
                var loop = new Method("Loop", f.Box, [], new MemberModifiers { IsRef = true }) { DeclaringType = f.Holder, Body = HandBuiltModel.Block() };
                loop.Body.Statements = [new ReturnStatement(Call(loop, This()), f.Box, isRef: true)];
                return (Call(loop, Ref(p)), HandBuiltModel.Block(declareP));
        }
    }

    private Method Returning(string name, Expression place, bool isStatic = false, LocalVariable[]? parameters = null, params Statement[] before)
    {
        return new Method(name, f.Box, [.. (parameters ?? []).Select(parameter => parameter.Parameter!)], new MemberModifiers { IsRef = true, IsStatic = isStatic })
        {
            DeclaringType = f.Holder,
            Body = HandBuiltModel.Block([.. before, new ReturnStatement(place, f.Box, isRef: true)]),
        };
    }

    private ThisExpression This()
    {
        return new ThisExpression(HandBuiltModel.Node(), f.Holder);
    }

    private static StructType Int32 { get; } = new("Int32", "Standard", new Scope(null), null, null);
}