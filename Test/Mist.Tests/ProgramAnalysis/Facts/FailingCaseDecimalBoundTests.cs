using System.Numerics;
using Mist.ProgramAnalysis.Facts;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class FailingCaseDecimalBoundTests
{
    [Fact]
    public void SpelledPreservesATerminatingDecimalBoundBeyondBinaryPrecision()
    {
        var amount = new Value(new HandBuiltModel().Type("Decimal"), "amount", HandBuiltModel.Node());
        var scale = BigInteger.Pow(10, 28);
        var term = new Term([KeyValuePair.Create(amount, scale)], -(scale / 10 + 1));
        var denial = new Fact(term, Relation.LessThan);

        Assert.Equal(["amount < 0.1000000000000000000000000001"], FailingCase.Spelled([denial]));
    }
}