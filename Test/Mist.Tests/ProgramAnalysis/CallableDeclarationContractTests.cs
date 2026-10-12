using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class CallableDeclarationContractTests
{
    [Theory]
    [InlineData("identical", true)]
    [InlineData("return-type", true)]
    [InlineData("parameter-name", true)]
    [InlineData("parameter-ownership", true)]
    [InlineData("refinement", true)]
    [InlineData("symbol", false)]
    [InlineData("arity", false)]
    [InlineData("parameter-type", false)]
    [InlineData("ref-parameter", false)]
    [InlineData("failed-parameter", false)]
    [InlineData("synthetic", false)]
    public void OperatorSignatureUsesSymbolAndParameterSignature(string difference, bool duplicates)
    {
        var built = new HandBuiltModel();
        var owner = HandBuiltModel.Struct("Money");
        var earlier = Operator(owner, OperatorKind.Minus, owner, [new Parameter("a", owner), new Parameter("b", owner)],
            difference == "synthetic" ? null : 20);
        var later = Operator(owner,
            difference == "symbol" ? OperatorKind.Plus : OperatorKind.Minus,
            difference == "return-type" ? built.Type("Int32") : owner,
            [new Parameter("a", owner), new Parameter("b", owner)], 30);

        switch(difference)
        {
            case "parameter-name":
                later.ParametersList[1] = new Parameter("other", owner);
                break;
            case "parameter-ownership":
                later.ParametersList[1] = new Parameter("b", owner, new ParameterModifier(false, false, OwnershipKind.Owned));
                break;
            case "refinement":
                later.ParametersList[1].Type = HandBuiltModel.Refined(owner, "Positive");
                break;
            case "arity":
                later.ParametersList.RemoveAt(1);
                break;
            case "parameter-type":
                later.ParametersList[1].Type = built.Type("Int32");
                break;
            case "ref-parameter":
                later.ParametersList[1] = new Parameter("b", owner, new ParameterModifier(false, true, OwnershipKind.Unrestricted));
                break;
            case "failed-parameter":
                earlier.ParametersList[1].Type = later.ParametersList[1].Type = ErrorType.Instance;
                break;
        }

        var repeats = CallableSignatureDecision.Operators([earlier, later]).ToList();
        if(duplicates)
        {
            var repeat = Assert.Single(repeats);
            Assert.Same(earlier, repeat.Earlier);
            Assert.Same(later, repeat.Later);
        }
        else
        {
            Assert.Empty(repeats);
        }
    }

    [Theory]
    [InlineData("implicit", true)]
    [InlineData("explicit", true)]
    [InlineData("different-kind", false)]
    [InlineData("different-source", false)]
    [InlineData("different-target", false)]
    [InlineData("refined-source", true)]
    [InlineData("refined-target", true)]
    [InlineData("failed-source", false)]
    [InlineData("failed-target", false)]
    [InlineData("synthetic", false)]
    public void CastSignatureUsesKindAndSourceTargetPair(string difference, bool duplicates)
    {
        var built = new HandBuiltModel();
        var owner = HandBuiltModel.Struct("Money");
        var earlier = Cast(owner, owner, built.Type("Int32"), difference != "explicit", difference == "synthetic" ? null : 20);
        var later = Cast(owner,
            difference == "different-source" ? built.Type("String") : owner,
            difference == "different-target" ? built.Type("Int64") : built.Type("Int32"),
            difference is not ("different-kind" or "explicit"), 30);

        switch(difference)
        {
            case "refined-source":
                later.SourceParameter.Type = HandBuiltModel.Refined(owner, "Positive");
                break;
            case "refined-target":
                later = Cast(owner, owner, HandBuiltModel.Refined(built.Type("Int32"), "Positive"), true, 30);
                break;
            case "failed-source":
                earlier.SourceParameter.Type = later.SourceParameter.Type = ErrorType.Instance;
                break;
            case "failed-target":
                earlier = Cast(owner, owner, ErrorType.Instance, true, 20);
                later = Cast(owner, owner, ErrorType.Instance, true, 30);
                break;
        }

        var repeats = CallableSignatureDecision.Casts([earlier, later]).ToList();
        if(duplicates)
        {
            var repeat = Assert.Single(repeats);
            Assert.Same(earlier, repeat.Earlier);
            Assert.Same(later, repeat.Later);
        }
        else
        {
            Assert.Empty(repeats);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailedConstructedTypeArgumentCannotMakeADuplicate(bool cast)
    {
        var built = new HandBuiltModel();
        var owner = HandBuiltModel.Struct("Money");
        var parameter = built.Interner.GetOrCreateConstructedType(HandBuiltModel.Class("Box"), [ErrorType.Instance]);

        Assert.Empty(cast
            ? CallableSignatureDecision.Casts([Cast(owner, owner, parameter, true, 20), Cast(owner, owner, parameter, true, 30)])
            : CallableSignatureDecision.Operators([
                Operator(owner, OperatorKind.Plus, owner, [new Parameter("a", owner), new Parameter("b", parameter)], 20),
                Operator(owner, OperatorKind.Plus, owner, [new Parameter("a", owner), new Parameter("b", parameter)], 30)]));
    }

    [Fact]
    public void EveryLaterDuplicateIdentifiesTheFirstSourceDeclaration()
    {
        var owner = HandBuiltModel.Struct("Money");
        var operators = new[] { 20, 30, 40 }.Select(offset => Operator(owner, OperatorKind.Minus, owner, [new Parameter("a", owner)], offset)).ToArray();
        var repeats = CallableSignatureDecision.Operators(operators).ToList();

        Assert.Equal(2, repeats.Count);
        Assert.All(repeats, repeat => Assert.Same(operators[0], repeat.Earlier));
        Assert.Equal(operators.Skip(1), repeats.Select(repeat => repeat.Later));
    }

    private static Operator Operator(ConcreteType owner, OperatorKind kind, IMistType returns, List<Parameter> parameters, int? offset)
    {
        return new Operator(kind, returns, parameters, DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = owner,
            DeclarationNode = offset is { } at ? HandBuiltModel.Node(at) : null,
        };
    }

    private static CastOperator Cast(ConcreteType owner, IMistType source, IMistType target, bool isImplicit, int? offset)
    {
        return new CastOperator(target, new Parameter("value", source), isImplicit, DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = owner,
            DeclarationNode = offset is { } at ? HandBuiltModel.Node(at) : null,
        };
    }
}