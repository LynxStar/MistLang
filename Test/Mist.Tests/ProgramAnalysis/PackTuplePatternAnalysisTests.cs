using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: PatternAnalyzer walking an is-test whose tuple pattern reads a pack, built as the binder builds it.
[Trait("Tier", "2")]
public sealed class PackTuplePatternAnalysisTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly TypeParameter open = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };
    private readonly TypeParameter pair = new("TPair", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed, PackLength = 2 };

    [Fact]
    public void AnElementTypeOneInstantiationCannotHold_IsNoNeverMatch()
    {
        var findings = Analyze(Spread(open), OverOpen(open, Test(h.Bool), Test(h.String)));

        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("fixed")]
    public void AnElementTestOfAPackPromisingPurity_IsJudgedAtThatElement(string pack)
    {
        var promised = new FunctionType([], h.Int, isPure: true);
        var pattern = pack == "open"
            ? OverOpen(open, Test(promised, 20), Discard(open.ElementAt(1)))
            : new TuplePattern(HandBuiltModel.Node(), [Test(promised, 20), Discard(pair.ElementAt(1))], Spread(pair), testedType: null, binding: null);

        var finding = Assert.Single(Analyze(Spread(pack == "open" ? open : pair), pattern));

        Assert.Equal("MIST0145", finding.Number.Value);
        Assert.Equal(21, finding.Position().Start.Column);
    }

    [Fact]
    public void AClosedTuple_KeepsItsNeverMatch()
    {
        var closed = h.Built.Interner.GetOrCreateTupleType([h.Int, h.String]);
        var pattern = new TuplePattern(HandBuiltModel.Node(), [Test(h.Bool, 20), Test(h.String)], closed, testedType: null, binding: null);

        var finding = Assert.Single(Analyze(closed, pattern));

        Assert.Equal("MIST0193", finding.Number.Value);
        Assert.Equal(21, finding.Position().Start.Column);
    }

    private IReadOnlyList<Mist.Common.Diagnostics.Diagnostic> Analyze(IMistType scrutinee, Pattern pattern)
    {
        return h.Built.Analyze(new PatternAnalyzer(), new IsPatternExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(scrutinee), pattern, h.Bool));
    }

    private TuplePattern OverOpen(TypeParameter pack, params Pattern[] elements)
    {
        var tested = h.Built.Interner.GetOrCreateTupleType([.. elements.Select((_, i) => (IMistType)pack.ElementAt(i))]);
        return new TuplePattern(HandBuiltModel.Node(), elements, tested, tested, binding: null) { OpenPack = pack };
    }

    private IMistType Spread(TypeParameter pack)
    {
        return h.Built.Interner.GetOrCreateTupleType([new SpreadType(pack)]);
    }

    private static TypeTestPattern Test(IMistType tested, int offset = 0)
    {
        return new TypeTestPattern(HandBuiltModel.Node(offset), tested, new LocalVariable("bound" + offset, tested));
    }

    private static CatchAllPattern Discard(IMistType input)
    {
        return new CatchAllPattern(HandBuiltModel.Node(), input, binding: null);
    }
}