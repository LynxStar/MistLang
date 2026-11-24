using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
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

[Trait("Tier", "2")]
public sealed class RefAliasRefusalTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("A1", "owned binding 'b'")]
    [InlineData("A2", "owned field 'p.Kept'")]
    [InlineData("A3", "owned field 'p.Kept'")]
    public void AnAliasOfAnOwnedBindingOrOwnedFieldPassedByBareRefIsRefusedAtTheArgument(string program, string passed)
    {
        var diagnostic = Assert.Single(f.Analyze(new TierAnalyzer(), Program(program)));

        Assert.Equal("MIST0509", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains($"Cannot pass {passed} to bare ref parameter 'x'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("A4")]
    [InlineData("ref return stored directly")]
    public void ARefReturnOfAnOwnedFieldStoredInAStaticIsRefusedAtTheStore(string program)
    {
        var diagnostic = Assert.Single(f.Analyze(new OwnershipAnalyzer(), Program(program)));

        Assert.Equal("MIST0486", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains("Cannot store owned value 'p.Kept' in non-owning field 'S'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ref local of an owned int passed by ref")]
    [InlineData("ref return of a plain field passed by ref")]
    public void AnAliasOfStorageThatIsNotOwnedOrCannotChangeIsStillPassed(string program)
    {
        var body = Program(program);

        Assert.Empty(f.Analyze(new TierAnalyzer(), body));
        Assert.Empty(f.Analyze(new OwnershipAnalyzer(), body));
    }

    private Block Program(string program)
    {
        var b = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
        var p = new LocalVariable("p", f.Holder) { Ownership = OwnershipKind.Owned };
        var r = new LocalVariable("r", f.Box) { IsRef = true };
        Statement declareP = Declare(p, Fresh(f.Holder));
        var get = Returning("Get", f.Kept);

        switch(program)
        {
            case "A1":
                return HandBuiltModel.Block(Declare(b, Fresh(f.Box)), Declare(r, Ref(b)), Pass("Keep", Ref(r, 37), Store(Static(f.S), Ref(Callee))));
            case "A2":
                return HandBuiltModel.Block(declareP, Declare(r, Access(Ref(p), f.Kept)), Pass("Keep", Ref(r, 37), Store(Static(f.S), Ref(Callee))));
            case "A3":
                return HandBuiltModel.Block(declareP, Pass("Keep", Call(get, Ref(p), 37), Store(Static(f.S), Ref(Callee))));
            case "A4":
                return HandBuiltModel.Block(declareP, Declare(r, Call(get, Ref(p))), Run(Store(Static(f.S), Ref(r, 37))));
            case "ref return stored directly":
                return HandBuiltModel.Block(declareP, Run(Store(Static(f.S), Call(get, Ref(p), 37))));
            case "ref local of an owned int passed by ref":
                var int32 = new StructType("Int32", "Standard", new Scope(null), null, null);
                var n = new LocalVariable("n", int32) { Ownership = OwnershipKind.Owned };
                var rn = new LocalVariable("rn", int32) { IsRef = true };
                return HandBuiltModel.Block(Declare(n, HandBuiltModel.Literal(int32, 5L)), Declare(rn, Ref(n)), Pass("Bump", Ref(rn)));
            default:
                return HandBuiltModel.Block(declareP, Pass("Renew", Call(Returning("Plain", f.In), Ref(p)), Store(Ref(Callee), Fresh(f.Box))));
        }
    }

    private Method Returning(string name, Field field)
    {
        var returned = Access(new ThisExpression(HandBuiltModel.Node(), f.Holder), field);
        return new Method(name, f.Box, [], new MemberModifiers { IsRef = true })
        {
            DeclaringType = f.Holder,
            Body = HandBuiltModel.Block(new ReturnStatement(returned, f.Box, isRef: true)),
        };
    }

    private LocalVariable Callee { get; } = Parameter("x", HandBuiltModel.Class("Box"), Tier("ref"));

    private ExpressionStatement Pass(string name, Expression argument, params Expression[] body)
    {
        var method = new Method(name, f.Model.Type("Void"), [new Parameter("x", argument.Type, Tier("ref"))], new MemberModifiers { IsStatic = true })
        {
            Body = HandBuiltModel.Block([.. body.Select(Run)]),
        };
        return Run(Call(method, null, 0, argument));
    }
}