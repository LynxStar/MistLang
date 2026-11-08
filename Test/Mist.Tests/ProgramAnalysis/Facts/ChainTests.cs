using Mist.ProgramAnalysis.Facts;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class ChainTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReserveChainsThroughUsedAndDoubled(bool nonnegative)
    {
        var model = new FactModel();
        var capacity = model.Value("capacity");
        var used = model.Value("used");
        var doubled = model.Value("doubled");
        var bag = MakeBag(Le(-1_000_000, (capacity, 1)), Le(0, (used, 1), (capacity, -1)), Eq(0, (doubled, 1), (capacity, -2)));
        if(nonnegative)
        {
            bag = bag.Add(Le(0, (capacity, -1)));
        }

        var proof = Prove(bag, Le(1, (doubled, 1), (used, -1)), capacity);

        if(nonnegative)
        {
            _ = Assert.IsType<Proven>(proof);
        }
        else
        {
            var refused = Assert.IsType<Refuted>(proof);
            Assert.Contains("capacity <= -1", refused.FailingCase.ToString());
            Assert.False(refused.AfterMerge);
        }
    }

    [Fact]
    public void ShowCombinesAThreeValueWhereClauseWithThePathCondition()
    {
        var model = new FactModel();
        var length = model.Value("items.Length");
        var start = model.Value("start");
        var count = model.Value("count");
        var bag = MakeBag(Le(0, (start, -1)), Le(1, (count, -1)), Le(0, (count, 1), (length, -1), (start, 1)));

        _ = Assert.IsType<Proven>(Prove(bag, Le(0, (length, 1), (start, -1)), start, length));
    }

    [Fact]
    public void ReserveFailureStaysWithinTheRequestedDiagnosticValues()
    {
        var model = new FactModel();
        var capacity = model.Value("capacity");
        var used = model.Value("used");
        var doubled = model.Value("doubled");
        var bag = MakeBag(Le(0, (used, 1), (capacity, -1)), Eq(0, (doubled, 1), (capacity, -2)));

        var refused = Assert.IsType<Refuted>(Prove(bag, Le(1, (doubled, 1), (used, -1)), used, doubled));

        Assert.NotEmpty(refused.FailingCase.Facts);
        Assert.All(refused.FailingCase.Facts, fact => Assert.DoesNotContain(capacity, fact.Term.Coefficients.Keys));
        Assert.All(refused.FailingCase.Facts, fact =>
        {
            var total = fact.Term.Coefficients.Aggregate(fact.Term.Constant,
                (sum, pair) => sum + pair.Value * (pair.Key == used ? -1 : -2));
            Assert.True(fact.Relation == Relation.Equal ? total.IsZero : total.Sign <= 0);
        });
    }

    [Fact]
    public void NeedsTenUsesTheOldValueAfterAWrite()
    {
        var model = new FactModel();
        var old = model.Value("x@1");
        var y = model.Value("y@2");
        var current = model.Value("x@3");
        var bag = MakeBag(Eq(-5, (old, 1)), Eq(0, (y, 1), (old, -2)), Eq(-42, (current, 1)));

        _ = Assert.IsType<Proven>(Chain.Prove(bag, Eq(-10, (y, 1)).Denial(), new HashSet<Value> { y }, Limit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NeedsRequiresThePairingBetweenVariables(bool merge)
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var paths = new[] { new Alternative([Eq(-1, (a, 1)), Eq(-2, (b, 1))]), new Alternative([Eq(-100, (a, 1)), Eq(-200, (b, 1))]) };
        var bag = new Bag([], merge ? [Bag.MergePerVariable(paths, Limit)] : paths);

        var proof = Prove(bag, Le(0, (b, 1), (a, -1)), a, b);

        if(merge)
        {
            Assert.True(Assert.IsType<Refuted>(proof).AfterMerge);
        }
        else
        {
            _ = Assert.IsType<Proven>(proof);
        }
    }

    [Fact]
    public void EqualityDenialMustRuleOutBothAlternatives()
    {
        var x = new FactModel().Value("x");
        var denial = Eq(-10, (x, 1)).Denial();

        Assert.Equal(2, denial.Length);
        _ = Assert.IsType<Proven>(Chain.Prove(MakeBag(Eq(-10, (x, 1))), denial, new HashSet<Value> { x }, Limit));
        var refused = Assert.IsType<Refuted>(Chain.Prove(MakeBag(Le(10, (x, -1))), denial, new HashSet<Value> { x }, Limit));
        Assert.Equal(2, refused.Runs);
        Assert.Contains("x >= 11", refused.FailingCase.ToString());
    }

    [Fact]
    public void ProjectionRunsIdenticalReachableAlternativesOnce()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var unreachable = model.Value("unrelated");
        var bag = new Bag([Eq(-5, (x, 1))], [new Alternative([Eq(-1, (unreachable, 1))]), new Alternative([Eq(-2, (unreachable, 1))])]);

        var result = Assert.IsType<Proven>(Prove(bag, Le(6, (x, -1)), x));

        Assert.Equal(1, result.Runs);
    }

    [Fact]
    public void ProjectionDropsUnreachableMembershipWithoutExpandingIt()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var unused = model.Value("unused");
        var values = new IntegralSet([new(1, 1), new(100, 100)]);
        var bag = MakeBag(Eq(-5, (x, 1)), Fact.In(unused, values));

        _ = Assert.IsType<Proven>(Chain.Prove(bag, [new Alternative([Le(6, (x, -1))])], new HashSet<Value> { x }, new Budget(TimeSpan.FromMinutes(1), 1)));
    }

    [Fact]
    public void DerivedFactBudgetStopsAChainWithoutClaimingARefutation()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var c = model.Value("c");
        var bag = MakeBag(Eq(-1, (a, 1)), Eq(0, (b, 1), (a, -1)), Eq(0, (c, 1), (b, -1)));

        _ = Assert.IsType<Exhausted>(Chain.Prove(bag, [new Alternative([Le(2, (c, -1))])], new HashSet<Value> { c }, new Budget(TimeSpan.FromMinutes(1), 1)));
    }

    [Fact]
    public void ExpiredBudgetDoesNotClaimProof()
    {
        var x = new FactModel().Value("x");

        _ = Assert.IsType<Exhausted>(Chain.Prove(MakeBag(Eq(0, (x, 1))), Eq(0, (x, 1)).Denial(), new HashSet<Value> { x }, new Budget(TimeSpan.Zero, 100)));
    }

    [Fact]
    public void OppositeBoundsCancelWithPositiveScaling()
    {
        var model = new FactModel();
        var x = model.Value("x");
        var y = model.Value("y");
        var bag = MakeBag(Le(0, (x, 2), (y, -1)), Le(0, (x, -3), (y, 1)));

        _ = Assert.IsType<Proven>(Prove(bag, Le(1, (x, 1)), x));
    }

    [Fact]
    public void EqualitySubstitutionKeepsAnInequalityDirectionWithANegativePivot()
    {
        var model = new FactModel();
        var y = model.Value("y");
        var x = model.Value("x");
        var bag = MakeBag(Eq(0, (x, -2), (y, 1)), Le(0, (x, 1)), Le(-100, (y, 1)));

        _ = Assert.IsType<Proven>(Prove(bag, Le(1, (y, -1)), y));
    }

    [Fact]
    public void MergingUnboundedProjectionsStillMarksTheLostRelationship()
    {
        var model = new FactModel();
        var a = model.Value("a");
        var b = model.Value("b");
        var paths = new[] { new Alternative([Eq(1, (a, 1), (b, -1))]), new Alternative([Eq(2, (a, 1), (b, -1))]) };
        var denial = Le(0, (b, 1), (a, -1));

        _ = Assert.IsType<Proven>(Prove(new Bag([], paths), denial, a, b));
        var merged = Bag.MergePerVariable(paths, Limit);
        Assert.True(Assert.IsType<Refuted>(Prove(new Bag([], [merged]), denial, a, b)).AfterMerge);
    }
}