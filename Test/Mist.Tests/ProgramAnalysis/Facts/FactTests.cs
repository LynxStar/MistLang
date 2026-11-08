using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class FactTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    [InlineData(3, 2)]
    [InlineData(-3, -1)]
    public void InequalitiesRoundTheConstantUp(int constant, int expected)
    {
        var value = new FactModel().Value("capacity");
        var fact = FactModel.Le(constant, (value, 2));

        Assert.Equal(BigInteger.One, fact.Term.Coefficients[value]);
        Assert.Equal(new BigInteger(expected), fact.Term.Constant);
    }

    [Fact]
    public void CapacityComparisonReducesToItsFailingCase()
    {
        var capacity = new FactModel().Value("capacity");
        var term = new Term(capacity).Scale(2).Add(new Term(1)).Subtract(new Term(capacity));

        Assert.Equal("capacity <= -1", new Comparison(term).ToString());
    }

    [Fact]
    public void EqualitiesNormalizeSignAndDetectIndivisibleConstants()
    {
        var value = new FactModel().Value("x");

        Assert.Equal(FactModel.Eq(-3, (value, 1)), FactModel.Eq(6, (value, -2)));
        Assert.True(FactModel.Eq(1, (value, 2)).IsContradiction);
        Assert.False(FactModel.Le(-1, (value, 2)).IsContradiction);
    }

    [Fact]
    public void TermsUseIdentityAndCancelWithoutMachineOverflow()
    {
        var model = new FactModel();
        var first = model.Value("x");
        var second = model.Value("x");
        var huge = BigInteger.One << 100;
        var term = new Term(first).Scale(huge).Add(new Term(second));

        Assert.Equal(2, term.Coefficients.Count);
        Assert.Equal(new Term(second), term.Subtract(new Term(first).Scale(huge)));
        Assert.Equal(huge, term.Coefficients[first]);
    }

    [Fact]
    public void BindingsRetainOldValuesAndShareMembersThroughReceiverIdentity()
    {
        var model = new FactModel();
        var local = HandBuiltRefinements.Local(model.Expressions.Int).Local;
        var (bindings, old) = Bindings.Empty.Read(local, local.Type, "x@1", HandBuiltModel.Node());
        var (same, read) = bindings.Read(local, local.Type, "x@2", HandBuiltModel.Node());
        var next = model.Value("x@3");
        var written = bindings.Set(local, next);
        var member = new Field("member", local.Type, default, new MemberModifiers());
        var holder = new MemberHolder(old, member);
        var memberBindings = written.Set(holder, next);

        Assert.Same(old, read);
        Assert.Same(bindings, same);
        Assert.Same(old, bindings.Get(local));
        Assert.Same(next, written.Get(local));
        Assert.Same(next, memberBindings.Get(new MemberHolder(old, member)));
        Assert.Null(memberBindings.Get(new MemberHolder(next, member)));
        Assert.Null(written.Remove([local]).Get(local));
    }

    [Fact]
    public void StrictRelationRemainsAvailable()
    {
        var value = new FactModel().Value("x");
        var strict = new Comparison(new Term(value), Relation.LessThan);

        Assert.Equal(Relation.LessThan, strict.Relation);
        Assert.Equal("x < 0", strict.ToString());
    }
}