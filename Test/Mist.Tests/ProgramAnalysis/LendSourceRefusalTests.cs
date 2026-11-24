using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LendFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class LendSourceRefusalTests
{
    private readonly LendFragments f = new();

    [Theory]
    [InlineData("T1", "borrowed", false, "static field 'Hook.S'")]
    [InlineData("T2", "borrowed", true, "static field 'Hook.S'")]
    [InlineData("T3", "frozen", false, "static field 'Hook.S'")]
    [InlineData("T4", "frozen", true, "static field 'Hook.S'")]
    [InlineData("T5", "borrowed", false, "field 'this.In'")]
    [InlineData("T6", "borrowed", true, "field 'this.In'")]
    [InlineData("T7", "borrowed", false, "field 'p.In'")]
    [InlineData("T8", "borrowed", false, "field 'p.In'")]
    [InlineData("T9", "borrowed", false, "element of 'arr'")]
    [InlineData("T10", "borrowed", false, "result of 'Get'")]
    public void ASourceNothingHoldsExclusivelyCannotBeLent(string program, string tier, bool argument, string source)
    {
        var (statements, owner, member) = Lend(program, tier, argument);

        var diagnostic = Assert.Single(f.Analyze(new TierAnalyzer(), HandBuiltModel.Block([.. statements]), owner, member));

        Assert.Equal("MIST0230", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains($"unrestricted {source} to {tier} parameter", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AConstructionThatKeepsThisCannotBeLent(bool argument)
    {
        var (statements, _, _) = Lend(argument ? "T12" : "T11", "borrowed", argument);

        var diagnostic = Assert.Single(f.Analyze(new TierAnalyzer(), HandBuiltModel.Block([.. statements])));

        Assert.Equal("MIST0507", diagnostic.Number.Value);
        Assert.Equal(37, Assert.IsType<SourceRange>(diagnostic.Location).Offset);
        Assert.Contains("neither fresh, moved from an owned binding, nor of a type that cannot change", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("'Leaky' lets 'this' escape", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("owned local")]
    [InlineData("borrowed parameter re-lent")]
    [InlineData("frozen parameter re-lent")]
    [InlineData("owned field of an owned parent")]
    [InlineData("fresh construction")]
    [InlineData("call returning owned")]
    [InlineData("inferred owned local")]
    public void AnExclusiveSourceIsStillLent(string source)
    {
        var tier = source.StartsWith("frozen", StringComparison.Ordinal) ? "frozen" : "borrowed";
        var statements = new List<Statement>();
        Expression value;
        switch(source)
        {
            case "owned local":
                var b = new LocalVariable("b", f.Box) { Ownership = OwnershipKind.Owned };
                statements.Add(Declare(b, Fresh(f.Box)));
                value = Ref(b);
                break;
            case "borrowed parameter re-lent" or "frozen parameter re-lent":
                value = Ref(Parameter("x", f.Box, Tier(tier)));
                break;
            case "owned field of an owned parent":
                var p = new LocalVariable("p", f.Holder) { Ownership = OwnershipKind.Owned };
                statements.Add(Declare(p, Fresh(f.Holder)));
                value = Access(Ref(p), f.Kept);
                break;
            case "fresh construction":
                value = Fresh(f.Box);
                break;
            case "call returning owned":
                value = Call(new Method("Make", f.Box, [], new MemberModifiers { IsStatic = true }) { ReturnOwnership = OwnershipKind.Owned });
                break;
            default:
                var s = new LocalVariable("s", f.Box) { InfersOwnership = true };
                statements.Add(Declare(s, Fresh(f.Box)));
                value = Ref(s);
                break;
        }

        statements.Add(Declare(new LocalVariable("o", f.Box) { Ownership = tier == "frozen" ? OwnershipKind.Frozen : OwnershipKind.Borrowed }, value));

        Assert.Empty(f.Analyze(new TierAnalyzer(), HandBuiltModel.Block([.. statements])));
    }

    [Theory]
    [InlineData("T2")]
    [InlineData("T6")]
    [InlineData("T7")]
    [InlineData("T9")]
    [InlineData("T10")]
    [InlineData("T12")]
    public void AnySourceCanBeLentReadonlyRef(string program)
    {
        var (statements, owner, member) = Lend(program, "readonly ref", argument: true);

        Assert.Empty(f.Analyze(new TierAnalyzer(), HandBuiltModel.Block([.. statements]), owner, member));
    }

    private (List<Statement> Statements, MistType? Owner, object? Member) Lend(string program, string tier, bool argument)
    {
        var statements = new List<Statement>();
        MistType? owner = null;
        object? member = null;
        Expression source;
        switch(program)
        {
            case "T1" or "T2" or "T3" or "T4":
                source = Static(f.S, 37);
                break;
            case "T5" or "T6":
                owner = f.Holder;
                member = new Method("M", f.Model.Type("Void"), [], default) { DeclaringType = f.Holder };
                source = Access(new ThisExpression(HandBuiltModel.Node(), f.Holder), f.In, 37);
                break;
            case "T7":
                var p = new LocalVariable("p", f.Holder) { Ownership = OwnershipKind.Owned };
                statements.Add(Declare(p, Fresh(f.Holder)));
                source = Access(Ref(p), f.In, 37);
                break;
            case "T8":
                source = Access(Ref(Parameter("p", f.Holder, Tier("borrowed"))), f.In, 37);
                break;
            case "T9":
                var arr = new LocalVariable("arr", f.BoxArray);
                statements.Add(Declare(arr, new ArrayLiteralExpression(HandBuiltModel.Node(), f.Box, [Fresh(f.Box)], f.BoxArray)));
                source = f.Element(Ref(arr), 37);
                break;
            case "T10":
                source = Call(new Method("Get", f.Box, [], new MemberModifiers { IsStatic = true }), null, 37);
                break;
            default:
                source = Leaky(37);
                break;
        }

        statements.Add(argument
            ? Run(Call(Use(tier, source.Type), null, 0, source))
            : Declare(new LocalVariable("o", source.Type) { Ownership = tier == "frozen" ? OwnershipKind.Frozen : OwnershipKind.Borrowed }, source));
        return (statements, owner, member);
    }

    private Method Use(string tier, IMistType? type = null)
    {
        var parameter = new Parameter("x", type ?? f.Box, Tier(tier));
        return new Method("Use", f.Model.Type("Void"), [parameter], new MemberModifiers { IsStatic = true });
    }
}