using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: RefinementAnalyzer over a hand-built call to `Take(<slot>, Adult tail)`, where Adult is
// `int >= 18` and the slot is a `params` parameter. An argument is written as the integer it is, or
// `...` for a spread the binder kept; each sits at column 10 times its position plus one.
[Trait("Tier", "2")]
public sealed class FlattenedArgumentRefinementTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly TypeParameter pack = new("TArgs", 0, new Scope(null), null) { PackKind = TypePackKind.Unbracketed };

    [Theory]
    [InlineData("array-of-adults", "20, 5, 30", 11)]
    [InlineData("array-of-adults", "20, 30, 5", 21)]
    [InlineData("pack-closed-over-an-adult", "5, 7, 30", 1)]
    [InlineData("pack-closed-over-an-adult", "20, 7, 5", 21)]
    [InlineData("open-pack", "..., 5", 11)]
    public void AnArgumentThatViolatesTheRefinementItLandsIn_IsRefusedWhereItIsWritten(string slot, string arguments, int column)
    {
        var finding = Assert.Single(Analyze(slot, arguments));

        Assert.Equal(("MIST_PA_REFINEMENT_VIOLATED", column), (finding.Number.Value, finding.Position().Start.Column));
    }

    [Theory]
    [InlineData("array-of-adults", "20, 30, 40")]
    [InlineData("array-of-adults", "18")]
    [InlineData("pack-closed-over-an-adult", "20, 7, 30")]
    [InlineData("open-pack", "..., 30")]
    public void ACallWhoseArgumentsMeetTheirRefinements_IsAdmitted(string slot, string arguments)
    {
        Assert.Empty(Analyze(slot, arguments));
    }

    [Fact]
    public void AFixedParameterAfterTheSlot_IsNamedByItsOwnConstraint()
    {
        var finding = Assert.Single(Analyze("array-of-adults", "20, 30, 5", tailIsNamed: false));

        Assert.Equal("MIST_PA_CONSTRAINT_VIOLATED", finding.Number.Value);
        Assert.Contains("on parameter 'tail'", finding.Message);
    }

    private IReadOnlyList<Mist.Common.Diagnostics.Diagnostic> Analyze(string slot, string arguments, bool tailIsNamed = true)
    {
        var adult = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, "Adult");
        var tail = tailIsNamed ? adult : h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, name: null);
        var open = new TupleType([new SpreadType(pack)]);

        var (written, closed) = slot switch
        {
            "array-of-adults" => (Array(adult), Array(adult)),
            "pack-closed-over-an-adult" => (open, h.Built.Interner.GetOrCreateTupleType([adult, h.Int])),
            "open-pack" => (open, open),
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

        var callee = new Method(
            "Take", h.Int, [new Parameter("slot", written) { IsFlattened = true }, new Parameter("tail", tail)], new MemberModifiers());
        var passed = arguments.Split(", ").Select((each, i) => Argument(each, open, offset: i * 10)).ToList();
        var call = new InvocationExpression(HandBuiltModel.Node(90), h.Literal(0L, offset: 95), passed, h.Int, callee, [closed, tail]);

        return h.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(HandBuiltRefinements.Evaluate(call)));
    }

    private Expression Argument(string written, IMistType open, int offset)
    {
        return written == "..."
            ? new SpreadExpression(HandBuiltModel.Node(offset), HandBuiltModel.Literal(open, offset: offset), open)
            : h.Literal(long.Parse(written, System.Globalization.CultureInfo.InvariantCulture), offset: offset);
    }

    private IMistType Array(IMistType element)
    {
        return h.Built.Interner.GetOrCreateConstructedType(
            h.Built.Type("Array<,>"), [element, h.Built.Interner.GetOrCreateUnknownValue(h.Int)]);
    }
}