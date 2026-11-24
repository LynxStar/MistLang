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
public sealed class BareRefRefusalTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("B1", "owned binding 'b'", "x")]
    [InlineData("B2", "owned binding 'b'", "x")]
    [InlineData("B3", "owned binding 'b'", "x")]
    [InlineData("B4", "owned binding 'b'", "x")]
    [InlineData("B5", "owned binding 'b'", "value")]
    [InlineData("B6", "owned field 'p.Kept'", "x")]
    [InlineData("B7", "owned field 'p.Kept'", "x")]
    [InlineData("B8", "owned field 'q.In'", "x")]
    [InlineData("B9", "owned field 'Kept'", "x")]
    public void AnOwnedBindingOrOwnedFieldPassedByBareRefIsRefusedAtTheArgument(string program, string passed, string parameter)
    {
        var (body, owner, member) = Program(program);

        var diagnostic = Assert.Single(f.Analyze(new TierAnalyzer(), body, owner, member));

        Assert.Equal("MIST0509", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains($"Cannot pass {passed} to bare ref parameter '{parameter}': a bare 'ref' carries no ownership", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("Lend it as 'borrowed' to change it in place, or pass it to an 'owned' parameter and take the replacement from an 'owned' return", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOwnedParameterPassedByBareRefToAChainedConstructorIsRefused()
    {
        var w = HandBuiltModel.Class("W");
        var target = new Constructor("W", w, [new Parameter("x", f.Box, Tier("ref"))], default) { DeclaringType = w };
        var y = Parameter("y", f.Box, Tier("owned"));
        var argument = Ref(y, 37);

        var diagnostic = Assert.Single(f.Analyze(new TierAnalyzer(), argument, w, Chained(HandBuiltModel.Class("W2"), target, [y], argument)));

        Assert.Equal("MIST0509", diagnostic.Number.Value);
        Assert.Contains("Cannot pass owned binding 'y' to bare ref parameter 'x'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("owned value that cannot change")]
    [InlineData("owned binding lent borrowed")]
    [InlineData("plain field of an owned parent")]
    [InlineData("unrestricted local")]
    [InlineData("inferred local")]
    [InlineData("owned binding by readonly ref")]
    public void WhatIsNeitherAnOwnedBindingNorAnOwnedFieldThatCanChangeIsStillPassed(string program)
    {
        var (body, owner, member) = Program(program);

        Assert.Empty(f.Analyze(new TierAnalyzer(), body, owner, member));
    }

    [Fact]
    public void AnOwnedFieldThroughAnUnrestrictedParentStaysASharedExtraction()
    {
        var p = new LocalVariable("p", f.Holder);
        var body = HandBuiltModel.Block(Declare(p, Fresh(f.Holder)), Pass("Keep", "ref", Access(Ref(p), f.Kept, 37)));

        var diagnostic = Assert.Single(f.Analyze(new TierAnalyzer(), body));

        Assert.Equal("MIST0233", diagnostic.Number.Value);
    }

    [Fact]
    public void AnInferredLocalPassedByBareRefLendsNothingAfterwards()
    {
        var b = new LocalVariable("b", f.Box) { InfersOwnership = true };
        var body = HandBuiltModel.Block(Declare(b, Fresh(f.Box)), Pass("Replace", "ref", Ref(b)), Pass("Set", "borrowed", Ref(b, 37)));

        var diagnostic = Assert.Single(f.Analyze(new TierAnalyzer(), body));

        Assert.Equal("MIST0230", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
    }

    private (Block Body, MistType? Owner, object? Member) Program(string program)
    {
        var b = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
        var p = new LocalVariable("p", f.Holder) { Ownership = OwnershipKind.Owned };
        Statement[] declareB = [Declare(b, Fresh(f.Box))];
        Statement[] declareP = [Declare(p, Fresh(f.Holder))];
        switch(program)
        {
            case "B1":
                return (HandBuiltModel.Block([.. declareB, Pass("Keep", "ref", Ref(b, 37), Store(Static(f.S), Ref(Callee))), Run(Store(Access(Static(f.S), f.V), HandBuiltModel.Literal(f.Int32, 7L)))]), null, null);
            case "B2":
                return (HandBuiltModel.Block([.. declareB, Pass("Replace", "ref", Ref(b, 37), Store(Ref(Callee), Static(f.S)))]), null, null);
            case "B3":
                return (HandBuiltModel.Block([.. declareB, Pass("Renew", "ref", Ref(b, 37), Store(Ref(Callee), Fresh(f.Box)))]), null, null);
            case "B4":
                return (HandBuiltModel.Block(Pass("Keep", "ref", Ref(Parameter("b", f.Box, Tier("owned")), 37))), null, null);
            case "B5":
                var tryGet = new Method("TryGetValue", f.Model.Type("Boolean"), [new Parameter("key", f.Model.Type("String")), new Parameter("value", f.Box, Tier("out"))], new MemberModifiers { IsStatic = true });
                return (HandBuiltModel.Block([.. declareB, Run(Call(tryGet, null, 0, HandBuiltModel.Literal(f.Model.Type("String"), "k"), Ref(b, 37)))]), null, null);
            case "B6":
                return (HandBuiltModel.Block([.. declareP, Pass("Keep", "ref", Access(Ref(p), f.Kept, 37))]), null, null);
            case "B7":
                return (HandBuiltModel.Block([.. declareP, Pass("Replace", "ref", Access(Ref(p), f.Kept, 37))]), null, null);
            case "B8":
                var type = HandBuiltModel.Class("Q");
                type.Modifiers = DeclarationFragments.Public with { IsOwned = true };
                var q = new LocalVariable("q", type) { Ownership = OwnershipKind.Owned };
                return (HandBuiltModel.Block(Declare(q, Fresh(type)), Pass("Keep", "ref", Access(Ref(q), Member(type, "In", f.Box), 37))), null, null);
            case "B9":
                var leak = new Method("Leak", f.Model.Type("Void"), [], default) { DeclaringType = f.Holder };
                return (HandBuiltModel.Block(Pass("Keep", "ref", new FieldReferenceExpression(HandBuiltModel.Node(37), f.Kept))), f.Holder, leak);
            case "owned value that cannot change":
                var n = new LocalVariable("n", new StructType("Int32", "Standard", new Scope(null), null, null)) { Ownership = OwnershipKind.Owned };
                return (HandBuiltModel.Block(Declare(n, HandBuiltModel.Literal(n.Type)), Pass("Bump", "ref", Ref(n))), null, null);
            case "owned binding lent borrowed":
                return (HandBuiltModel.Block([.. declareB, Pass("Set", "borrowed", Ref(b))]), null, null);
            case "plain field of an owned parent":
                return (HandBuiltModel.Block([.. declareP, Pass("Renew", "ref", Access(Ref(p), f.In))]), null, null);
            case "unrestricted local":
                var u = new LocalVariable("u", f.Box);
                return (HandBuiltModel.Block(Declare(u, Static(f.S)), Pass("Keep", "ref", Ref(u))), null, null);
            case "inferred local":
                var s = new LocalVariable("s", f.Box) { InfersOwnership = true };
                return (HandBuiltModel.Block(Declare(s, Fresh(f.Box)), Pass("Keep", "ref", Ref(s))), null, null);
            default:
                return (HandBuiltModel.Block([.. declareB, Pass("Peek", "readonly ref", Ref(b))]), null, null);
        }
    }

    private LocalVariable Callee { get; } = Parameter("x", HandBuiltModel.Class("Box"), Tier("ref"));

    private ExpressionStatement Pass(string name, string tier, Expression argument, params Expression[] body)
    {
        var parameter = new Parameter("x", argument.Type, Tier(tier));
        var method = new Method(name, f.Model.Type("Void"), [parameter], new MemberModifiers { IsStatic = true })
        {
            Body = HandBuiltModel.Block([.. body.Select(Run)]),
        };
        return Run(Call(method, null, 0, argument));
    }
}