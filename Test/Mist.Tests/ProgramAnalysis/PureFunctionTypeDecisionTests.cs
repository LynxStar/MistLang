// Tier 1 (Unit): the pure function type at the decisions that read it -- the interner keeps it apart from the plain type, and assignability admits a pure value where a plain one is expected but not the reverse.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class PureFunctionTypeDecisionTests
{
    private readonly HandBuiltModel model = new();
    private readonly FunctionType plain;
    private readonly FunctionType promised;

    public PureFunctionTypeDecisionTests()
    {
        var integer = model.Type("Int32");
        plain = model.Interner.GetOrCreateFunctionType([integer], integer);
        promised = model.Interner.GetOrCreateFunctionType([integer], integer, isPure: true);
    }

    [Fact]
    public void ProgramModel_ThePureFunctionTypeIsInternedApartFromThePlainOne()
    {
        var integer = model.Type("Int32");

        Assert.Same(promised, model.Interner.GetOrCreateFunctionType([integer], integer, isPure: true));
        Assert.NotSame(promised, plain);
        Assert.True(promised.IsPure);
        Assert.False(plain.IsPure);
        Assert.Equal("pure " + plain.Name, promised.Name);
    }

    [Fact]
    public void ProgramModelToAnalysis_APureFunctionConvertsToThePlainFunctionType()
    {
        Assert.Null(Assignability.Judge(promised, plain, model.Interner));
    }

    [Fact]
    public void ProgramModelToAnalysis_APlainFunctionDoesNotConvertToAPureFunctionType()
    {
        var refused = Assignability.Judge(plain, promised, model.Interner);

        Assert.Equal("MIST0157", refused?.Number.Value);
    }
}