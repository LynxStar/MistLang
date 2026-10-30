using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class RefinementSiteTests
{
    [Theory]
    [InlineData("declaration")]
    [InlineData("assignment")]
    [InlineData("return")]
    [InlineData("call")]
    [InlineData("construction")]
    [InlineData("collection-element")]
    [InlineData("initializer-member")]
    [InlineData("cast")]
    [InlineData("index")]
    [InlineData("list-pattern")]
    public void FindingBelongsToTheValueOrPositionThatNeedsTheProof(string site)
    {
        var h = new HandBuiltRefinements();
        var adult = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18L, "AdultAge");
        var value = h.Literal(7L, offset: 24);
        var expected = "MIST0271";
        object root;
        switch(site)
        {
            case "declaration": root = HandBuiltRefinements.Need(value, adult); break;
            case "assignment": root = HandBuiltRefinements.Assign(HandBuiltRefinements.Local(adult), value); break;
            case "return": root = new ReturnStatement(value, adult); break;
            case "call": root = h.Call(value, adult); break;
            case "construction":
                var person = HandBuiltModel.Class("Person");
                var constructor = new Constructor("Person", person, [new Parameter("age", adult)], new MemberModifiers());
                root = new NewExpression(HandBuiltModel.Node(3), person, [value], constructor, [], [adult]); break;
            case "collection-element":
                var add = new Method("Add", h.Built.Type("Void"), [new Parameter("age", adult)], new MemberModifiers());
                root = new NewExpression(HandBuiltModel.Node(3), HandBuiltModel.Class("Ages"), [], null,
                    [new ObjectInitializerAssignment(HandBuiltModel.Node(3), null, value, null, add) { AddParameterTypes = [adult] }], []); break;
            case "initializer-member":
                root = new NewExpression(HandBuiltModel.Node(3), HandBuiltModel.Class("Person"), [], null,
                    [new ObjectInitializerAssignment(HandBuiltModel.Node(3), "Age", value, adult)], []); break;
            case "cast": root = new CastExpression(HandBuiltModel.Node(24), h.Literal(7L, offset: 3), adult, [], adult); break;
            case "index":
                root = h.At(h.Array(3L), h.Literal(3L, offset: 24), offset: 3);
                expected = "MIST0274"; break;
            case "list-pattern":
                var ledger = HandBuiltModel.Class("Ledger");
                var constraint = h.Between(h.Int, 0L, 2L, null);
                var indexer = new Indexer(h.Int, [new Parameter("entry", constraint)], new MemberModifiers());
                var count = new Property("Count", h.Int, new MemberModifiers());
                var elements = Enumerable.Range(0, 8).Select(_ => new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(3), h.Int, null), false, h.Int)).ToList();
                elements.Add(new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(24), h.Int, new LocalVariable("last", h.Int)), false, h.Int));
                var list = new ListPattern(HandBuiltModel.Node(3), elements, ledger) { Reads = new CountedIndexer(indexer, constraint, count) };
                root = new IsPatternExpression(HandBuiltModel.Node(3), HandBuiltRefinements.Local(ledger), list, h.Bool);
                expected = "MIST0276"; break;
            default: throw new InvalidOperationException(site);
        }

        var finding = Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), root));
        Assert.Equal(expected, finding.Number.Value);
        Assert.Equal("hand-built.mist", finding.Location.Path);
        Assert.Equal(25, finding.Position().Start.Column);
    }

    [Theory]
    [InlineData("method")]
    [InlineData("property")]
    [InlineData("parameter")]
    [InlineData("setter")]
    [InlineData("mapping")]
    public void ContractFindingBelongsToTheImplementationOrMapping(string site)
    {
        var fragments = new DeclarationFragments();
        var h = new HandBuiltRefinements();
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, "Positive");
        var contract = fragments.Abstract("IFactory");
        var concrete = fragments.Class("Factory");
        var expected = "MIST0284";
        if(site is "property" or "setter")
        {
            contract.PropertiesList.Add(new Property("Value", positive, DeclarationFragments.Public)
            {
                Getter = site == "property" ? new PropertyAccessor { IsAuto = true } : null,
                Setter = site == "setter" ? new PropertyAccessor { IsAuto = true } : null,
            });
            concrete.PropertiesList.Add(new Property("Value", site == "setter" ? h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 2L, "Strong") : h.Int, DeclarationFragments.Public)
            {
                DeclaringType = concrete,
                DeclarationNode = HandBuiltModel.Node(24),
                Getter = site == "property" ? new PropertyAccessor { IsAuto = true } : null,
                Setter = site == "setter" ? new PropertyAccessor { IsAuto = true } : null,
            });
            if(site == "setter") { expected = "MIST0283"; }

            concrete.BaseAbstractTypes.Add(contract);
        }
        else
        {
            var slot = new Method("Make", site == "parameter" ? h.Int : positive,
                site == "parameter" ? [new Parameter("value", h.Int)] : [], DeclarationFragments.Public);
            contract.MethodsList.Add(slot);
            var filler = new Method(site == "mapping" ? "Text" : "Make", h.Int,
                site == "parameter" ? [new Parameter("value", positive)] : [], DeclarationFragments.Public)
            {
                DeclaringType = concrete,
                DeclarationNode = HandBuiltModel.Node(24),
            };
            concrete.MethodsList.Add(filler);
            if(site == "mapping")
            {
                concrete.ConformancesList.Add(new ConformanceDeclaration(concrete, contract, "Refusal",
                    [new MethodMapping(filler, slot, contract)], [], HandBuiltModel.Node(24)));
            }
            else { concrete.BaseAbstractTypes.Add(contract); }

            if(site == "parameter") { expected = "MIST0283"; }
        }

        var finding = Assert.Single(fragments.Run(new RefinementAnalyzer()));
        Assert.Equal(expected, finding.Number.Value);
        Assert.Equal("hand-built.mist", finding.Location.Path);
        Assert.Equal(25, finding.Position().Start.Column);
    }

    [Fact]
    public void ConstructorChainFindingBelongsToItsArgument()
    {
        var fragments = new DeclarationFragments();
        var h = new HandBuiltRefinements();
        var concrete = fragments.Class("Empty");
        var positive = h.Refine(h.Int, ComparisonKind.GreaterThan, 0L, null);
        var chained = new Constructor("Bounded", concrete, [new Parameter("size", positive)], DeclarationFragments.Public);
        var constructor = new Constructor("Empty", concrete, [], DeclarationFragments.Public)
        {
            DeclaringType = concrete,
            DeclarationNode = HandBuiltModel.Node(3),
            ChainedConstructor = chained,
            ChainKind = ConstructorChainKind.Base,
            ChainParameterTypes = [positive],
        };
        constructor.ChainArgumentsList.Add(h.Literal(0L, offset: 24));
        concrete.ConstructorsList.Add(constructor);
        var finding = Assert.Single(fragments.Run(new RefinementAnalyzer()));
        Assert.Equal("MIST0276", finding.Number.Value);
        Assert.Equal(25, finding.Position().Start.Column);
    }

    [Theory]
    [InlineData("Single")]
    [InlineData("Double")]
    [InlineData("Decimal")]
    public void ANegatedRealBoundInAFinding_ReadsAsTheNegatedLiteral(string real)
    {
        var h = new HandBuiltRefinements();
        var type = h.Built.Type(real);
        var bound = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, h.Literal(Real(real, 0.5), type), type);
        var above = h.Refine(type, ComparisonKind.GreaterThan, bound, "Above");
        var value = new UnaryExpression(HandBuiltModel.Node(24), UnaryOperatorKind.Negate, h.Literal(Real(real, 1.25), type, offset: 24), type);

        var finding = Assert.Single(h.Built.Analyze(new RefinementAnalyzer(), HandBuiltRefinements.Need(value, above)));

        Assert.Equal("MIST0271", finding.Number.Value);
        Assert.Contains("'> -0.5'", finding.Message, StringComparison.Ordinal);
    }

    private static object Real(string type, double value)
    {
        return type switch
        {
            "Single" => (float)value,
            "Double" => value,
            _ => (decimal)value,
        };
    }

}