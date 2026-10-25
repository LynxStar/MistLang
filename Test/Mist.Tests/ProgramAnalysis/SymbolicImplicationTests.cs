using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class SymbolicImplicationTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly Property count;
    private readonly Property limit;
    private readonly Property filled;
    private readonly Property elsewhere;

    public SymbolicImplicationTests()
    {
        count = Member("Count");
        limit = Member("Limit");
        filled = Member("Count");
        elsewhere = Member("Count");
    }

    [Theory]
    [InlineData("same-member-in-distinct-declarations", true)]
    [InlineData("same-spelled-unrelated-member", false)]
    [InlineData("strict-upper-implies-inclusive", true)]
    [InlineData("inclusive-upper-does-not-imply-strict", false)]
    [InlineData("strict-lower-implies-inclusive", true)]
    [InlineData("inclusive-lower-does-not-imply-strict", false)]
    [InlineData("upper-does-not-imply-lower", false)]
    [InlineData("extra-conjunct-strengthens", true)]
    [InlineData("missing-conjunct", false)]
    [InlineData("integral-endpoints-meet", true)]
    [InlineData("integral-endpoint-falls-short", false)]
    [InlineData("empty-domain-implies-a-bound", true)]
    [InlineData("inhabited-domain-lacks-the-bound", false)]
    [InlineData("same-dependent-parameter", true)]
    [InlineData("dependent-parameter-closed-opaquely", false)]
    [InlineData("forwarded-dependent-parameter", true)]
    [InlineData("forwarded-to-another-parameter", false)]
    [InlineData("corresponding-parameters", true)]
    [InlineData("parameters-without-correspondence", false)]
    [InlineData("parameters-in-other-positions", false)]
    [InlineData("out-parameters", false)]
    [InlineData("selected-member-filler", true)]
    [InlineData("member-without-selection", false)]
    [InlineData("slot-answered-by-two-fillers", false)]
    [InlineData("corresponding-receivers", true)]
    [InlineData("distinct-receivers", false)]
    [InlineData("selected-filler-through-a-receiver", false)]
    [InlineData("receiver-against-this", false)]
    [InlineData("disjunction-in-one-declaration", true)]
    [InlineData("disjunction-in-distinct-declarations", false)]
    [InlineData("negation-in-distinct-declarations", false)]
    [InlineData("arithmetic-in-distinct-declarations", false)]
    public void ASymbolicConjunctionImpliesOnlyWhatItsSymbolsProve(string name, bool expected)
    {
        var (source, target, symbols) = Case(name);
        Assert.Equal(expected, ContractImplication.Proves(source, target, symbols: symbols));
    }

    [Theory]
    [InlineData("weaker-demand-is-accepted", null)]
    [InlineData("stronger-demand-is-strengthened", "MIST0283")]
    [InlineData("corresponding-parameter-demand", null)]
    [InlineData("uncorresponded-parameter-demand", "MIST0283")]
    [InlineData("equivalent-by-reference", null)]
    [InlineData("weaker-by-reference", "MIST0283")]
    [InlineData("weaker-setter-demand", null)]
    [InlineData("stronger-setter-demand", "MIST0283")]
    public void AnImplementationAcceptsTheSameOrWeakerParameterPredicate(string name, string? expected)
    {
        var below = Refine(Below(Read(count)));
        var atMost = Refine(AtMost(Read(count)));
        var (slot, filler) = (Parameter("bound"), Parameter("bound"));
        var positional = SymbolCorrespondence.None.WithParameters([slot], [filler]);
        var finding = name switch
        {
            "weaker-demand-is-accepted" => RefinementContracts.ParameterFinding(below, atMost),
            "stronger-demand-is-strengthened" => RefinementContracts.ParameterFinding(atMost, below),
            "corresponding-parameter-demand" => RefinementContracts.ParameterFinding(Refine(Below(Named(slot))), Refine(Below(Named(filler))), symbols: positional),
            "uncorresponded-parameter-demand" => RefinementContracts.ParameterFinding(Refine(Below(Named(slot))), Refine(Below(Named(filler)))),
            "equivalent-by-reference" => RefinementContracts.ParameterFinding(below, Refine(Below(Read(count))), byReference: true),
            "weaker-by-reference" => RefinementContracts.ParameterFinding(below, atMost, byReference: true),
            "weaker-setter-demand" => RefinementContracts.SetterFinding(below, atMost),
            _ => RefinementContracts.SetterFinding(atMost, below),
        };

        Assert.Equal(expected, finding?.Number.Value);
    }

    [Theory]
    [InlineData("stronger-output-is-preserved", null)]
    [InlineData("weaker-output-is-unproven", "MIST0284")]
    [InlineData("equivalent-reference-output", null)]
    [InlineData("stronger-reference-output", "MIST0284")]
    [InlineData("output-over-a-corresponding-parameter", null)]
    [InlineData("output-over-an-uncorresponded-parameter", "MIST0284")]
    public void AnImplementationPreservesTheOutputPromise(string name, string? expected)
    {
        var below = Refine(Below(Read(count)));
        var atMost = Refine(AtMost(Read(count)));
        var (slot, filler) = (Parameter("bound"), Parameter("bound"));
        var positional = SymbolCorrespondence.None.WithParameters([slot], [filler]);
        var finding = name switch
        {
            "stronger-output-is-preserved" => RefinementContracts.ReturnFinding(below, atMost),
            "weaker-output-is-unproven" => RefinementContracts.ReturnFinding(atMost, below),
            "equivalent-reference-output" => RefinementContracts.ReturnFinding(below, Refine(Below(Read(count))), byReference: true),
            "stronger-reference-output" => RefinementContracts.ReturnFinding(below, atMost, byReference: true),
            "output-over-a-corresponding-parameter" => RefinementContracts.ReturnFinding(Refine(Below(Named(filler))), Refine(Below(Named(slot))), symbols: positional),
            _ => RefinementContracts.ReturnFinding(Refine(Below(Named(filler))), Refine(Below(Named(slot)))),
        };

        Assert.Equal(expected, finding?.Number.Value);
    }

    [Fact]
    public void EmbeddedStorageNeedsEquivalenceBothWays()
    {
        var box = HandBuiltModel.Class("Box<>");

        IMistType boxed(IMistType element)
        {
            return h.Built.Interner.GetOrCreateConstructedType(box, [element]);
        }

        Assert.True(ContractImplication.Proves(boxed(Refine(Below(Read(count)))), boxed(Refine(Below(Read(count))))));
        Assert.False(ContractImplication.Proves(boxed(Refine(Below(Read(count)))), boxed(Refine(AtMost(Read(count))))));
        Assert.False(ContractImplication.Proves(boxed(Refine(AtMost(Read(count)))), boxed(Refine(Below(Read(count))))));
    }

    private (IMistType Source, IMistType Target, SymbolCorrespondence Symbols) Case(string name)
    {
        var none = SymbolCorrespondence.None;

        switch(name)
        {
            case "same-member-in-distinct-declarations":
                return (Refine(Below(Read(count))), Refine(Below(Read(count))), none);
            case "same-spelled-unrelated-member":
                return (Refine(Below(Read(count))), Refine(Below(Read(elsewhere))), none);
            case "strict-upper-implies-inclusive":
                return (Refine(Below(Read(count))), Refine(AtMost(Read(count))), none);
            case "inclusive-upper-does-not-imply-strict":
                return (Refine(AtMost(Read(count))), Refine(Below(Read(count))), none);
            case "strict-lower-implies-inclusive":
                return (Refine(Compare(ComparisonKind.GreaterThan, Read(count))), Refine(Compare(ComparisonKind.GreaterThanOrEqual, Read(count))), none);
            case "inclusive-lower-does-not-imply-strict":
                return (Refine(Compare(ComparisonKind.GreaterThanOrEqual, Read(count))), Refine(Compare(ComparisonKind.GreaterThan, Read(count))), none);
            case "upper-does-not-imply-lower":
                return (Refine(AtMost(Read(count))), Refine(Compare(ComparisonKind.GreaterThanOrEqual, Read(count))), none);
            case "extra-conjunct-strengthens":
                return (Refine(Below(Read(count)), Below(Read(limit))), Refine(Below(Read(count))), none);
            case "missing-conjunct":
                return (Refine(Below(Read(count))), Refine(Below(Read(count)), Below(Read(limit))), none);
            case "integral-endpoints-meet":
                return (Refine(Compare(ComparisonKind.GreaterThan, 0L), Below(Read(count))),
                    Refine(Compare(ComparisonKind.GreaterThanOrEqual, 1L), AtMost(Read(count))), none);
            case "integral-endpoint-falls-short":
                return (Refine(Compare(ComparisonKind.GreaterThanOrEqual, 0L), Below(Read(count))),
                    Refine(Compare(ComparisonKind.GreaterThanOrEqual, 1L), AtMost(Read(count))), none);
            case "empty-domain-implies-a-bound":
                return (Refine(Compare(ComparisonKind.GreaterThan, 5L), Below(3L)), Refine(Below(Read(count))), none);
            case "inhabited-domain-lacks-the-bound":
                return (Refine(Compare(ComparisonKind.GreaterThan, 5L), Below(7L)), Refine(Below(Read(count))), none);
            case "disjunction-in-one-declaration":
                var either = Refine(Either());
                return (new RefinedType(either, h.Int), new RefinedType(either, h.Int), none);
            case "disjunction-in-distinct-declarations":
                return (Refine(Either()), Refine(Either()), none);
            case "negation-in-distinct-declarations":
                return (Refine(new NegatedPattern(HandBuiltModel.Node(), h.Int, Below(Read(count)))),
                    Refine(new NegatedPattern(HandBuiltModel.Node(), h.Int, Below(Read(count)))), none);
            case "arithmetic-in-distinct-declarations":
                return (Refine(Below(Plus(Read(count)))), Refine(Below(Plus(Read(count)))), none);
        }

        return name.Contains("dependent", StringComparison.Ordinal) || name.StartsWith("forwarded", StringComparison.Ordinal)
            ? Closings(name)
            : Selected(name);
    }

    private (IMistType Source, IMistType Target, SymbolCorrespondence Symbols) Closings(string name)
    {
        var size = Dependent("N");
        var forwarded = Dependent("M");
        var declared = Refine(Below(new TypeReferenceExpression(HandBuiltModel.Node(), size)));

        RefinedType closed(IMistType argument)
        {
            return new RefinedType(declared, h.Int, new Dictionary<TypeParameter, IMistType> { [size] = argument });
        }

        return name switch
        {
            "same-dependent-parameter" => (declared, Refine(Below(new TypeReferenceExpression(HandBuiltModel.Node(), size))), SymbolCorrespondence.None),
            "dependent-parameter-closed-opaquely" => (closed(new TypeParameter("T", 0, new Scope(null), null)), declared, SymbolCorrespondence.None),
            "forwarded-dependent-parameter" => (closed(forwarded), Refine(Below(new TypeReferenceExpression(HandBuiltModel.Node(), forwarded))), SymbolCorrespondence.None),
            _ => (closed(forwarded), declared, SymbolCorrespondence.None),
        };
    }

    private (IMistType Source, IMistType Target, SymbolCorrespondence Symbols) Selected(string name)
    {
        var (first, second) = (Parameter("first"), Parameter("second"));
        var (filledFirst, filledSecond) = (Parameter("first"), Parameter("second"));
        var positional = SymbolCorrespondence.None.WithParameters([first, second], [filledFirst, filledSecond]);
        var selected = SymbolCorrespondence.OfMembers([(count, filled)]);

        return name switch
        {
            "corresponding-parameters" => (Refine(Below(Named(first))), Refine(Below(Named(filledFirst))), positional),
            "parameters-without-correspondence" => (Refine(Below(Named(first))), Refine(Below(Named(filledFirst))), SymbolCorrespondence.None),
            "parameters-in-other-positions" => (Refine(Below(Named(first))), Refine(Below(Named(filledSecond))), positional),
            "out-parameters" => OutParameters(),
            "selected-member-filler" => (Refine(Below(Read(count))), Refine(Below(Read(filled))), selected),
            "member-without-selection" => (Refine(Below(Read(count))), Refine(Below(Read(filled))), SymbolCorrespondence.None),
            "slot-answered-by-two-fillers" => (Refine(Below(Read(count))), Refine(Below(Read(filled))), SymbolCorrespondence.OfMembers([(count, filled), (count, elsewhere)])),
            "corresponding-receivers" => (Refine(Below(Through(first, count))), Refine(Below(Through(filledFirst, count))), positional),
            "distinct-receivers" => (Refine(Below(Through(first, count))), Refine(Below(Through(filledSecond, count))), positional),
            "selected-filler-through-a-receiver" => (Refine(Below(Through(first, count))), Refine(Below(Through(filledFirst, filled))),
                SymbolCorrespondence.OfMembers([(count, filled)]).WithParameters([first, second], [filledFirst, filledSecond])),
            _ => (Refine(Below(Through(first, count))), Refine(Below(Read(count))), positional),
        };
    }

    private (IMistType Source, IMistType Target, SymbolCorrespondence Symbols) OutParameters()
    {
        var output = new ParameterModifier(false, true, default, ByRefDirection.Out);
        var (slot, filler) = (new Parameter("result", h.Int, output), new Parameter("result", h.Int, output));
        return (Refine(Below(Named(slot))), Refine(Below(Named(filler))), SymbolCorrespondence.None.WithParameters([slot], [filler]));
    }

    private Property Member(string name)
    {
        return new Property(name, h.Int, DeclarationFragments.Public) { Getter = new PropertyAccessor() };
    }

    private Parameter Parameter(string name)
    {
        return new Parameter(name, h.Int);
    }

    private TypeParameter Dependent(string name)
    {
        return new TypeParameter(name, 0, new Scope(null), null) { IsDependent = true, Domain = h.Built.Type("Int32") };
    }

    private RefinedType Refine(params Pattern[] conjuncts)
    {
        return HandBuiltRefinements.RefinePattern(h.Int,
            conjuncts.Length == 1 ? conjuncts[0] : new ConjunctionPattern(HandBuiltModel.Node(), h.Int, conjuncts), null);
    }

    private ComparisonPattern Compare(ComparisonKind kind, object bound)
    {
        return h.Compare(h.Int, kind, bound);
    }

    private ComparisonPattern Below(object bound)
    {
        return Compare(ComparisonKind.LessThan, bound);
    }

    private ComparisonPattern AtMost(object bound)
    {
        return Compare(ComparisonKind.LessThanOrEqual, bound);
    }

    private DisjunctionPattern Either()
    {
        return new DisjunctionPattern(HandBuiltModel.Node(), h.Int, [Below(Read(count)), Compare(ComparisonKind.GreaterThan, 100L)]);
    }

    private Expression Plus(Expression value)
    {
        return h.Binary(value, BinaryOperatorKind.Add, h.Literal(1L), h.Int);
    }

    private static PropertyReferenceExpression Read(Property property)
    {
        return new PropertyReferenceExpression(HandBuiltModel.Node(), property);
    }

    private static LocalReferenceExpression Named(Parameter parameter)
    {
        return HandBuiltRefinements.Read(new LocalVariable(parameter.Name, parameter.Type) { Parameter = parameter });
    }

    private MemberAccessExpression Through(Parameter receiver, Property property)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), Named(receiver), property.Name, h.Int,
            member: new PropertyResult(property, h.Int, []));
    }
}