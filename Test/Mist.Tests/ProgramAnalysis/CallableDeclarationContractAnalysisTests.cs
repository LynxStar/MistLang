using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class CallableDeclarationContractAnalysisTests
{
    [Theory]
    [InlineData("operator", "MIST_PA_DUPLICATE_OPERATOR_SIGNATURE")]
    [InlineData("operator-return", "MIST_PA_DUPLICATE_OPERATOR_SIGNATURE")]
    [InlineData("implicit", "MIST_PA_DUPLICATE_CAST_OPERATOR")]
    [InlineData("explicit", "MIST_PA_DUPLICATE_CAST_OPERATOR")]
    public void ASourceDuplicateIsReportedOnceAtTheLaterDeclarationAndIdentifiesBoth(string kind, string expected)
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Money");
        var integer = fragment.Built.Type("Int32");
        if(kind.StartsWith("operator", StringComparison.Ordinal))
        {
            owner.OperatorsList.Add(Operator(owner, owner, 20));
            owner.OperatorsList.Add(Operator(owner, kind == "operator-return" ? integer : owner, 30));
        }
        else
        {
            owner.CastOperatorsList.Add(Cast(owner, owner, integer, kind == "implicit", 20));
            owner.CastOperatorsList.Add(Cast(owner, owner, integer, kind == "implicit", 30));
        }

        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));
        Assert.Equal(expected, finding.Id.Value);
        Assert.Equal(PipelineStage.Analysis, finding.Stage);
        Assert.Equal("declaration-shape-checks", finding.AnalyzerName);
        Assert.Equal("hand-built.mist", finding.CompilationUnitPath);
        Assert.Equal(31, finding.Location.Start.Column);
        Assert.Contains("Money", finding.Message, StringComparison.Ordinal);
        Assert.Contains("hand-built.mist(1,21)", finding.Message, StringComparison.Ordinal);
        Assert.Contains(kind.StartsWith("operator", StringComparison.Ordinal) ? "'+'" : $"an {kind} cast operator from 'Money' to 'int'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnaryBinaryDifferentParametersAndDifferentSymbolsRemainOverloads()
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Money");
        owner.OperatorsList.Add(Operator(owner, owner, 20));
        var differingParameter = Operator(owner, owner, 30);
        differingParameter.ParametersList[1].Type = fragment.Built.Type("Int32");
        owner.OperatorsList.Add(differingParameter);
        owner.OperatorsList.Add(new Operator(OperatorKind.Minus, owner, [new Parameter("a", owner)], DeclarationFragments.Public with { IsStatic = true })
        {
            DeclarationNode = HandBuiltModel.Node(40),
        });
        owner.OperatorsList.Add(new Operator(OperatorKind.Minus, owner, [new Parameter("a", owner), new Parameter("b", owner)], DeclarationFragments.Public with { IsStatic = true })
        {
            DeclarationNode = HandBuiltModel.Node(50),
        });

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    [Fact]
    public void DifferentSourceTargetPairsAndAnImplicitExplicitPairAreNotDuplicates()
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Money");
        var integer = fragment.Built.Type("Int32");
        owner.CastOperatorsList.Add(Cast(owner, owner, integer, true, 20));
        owner.CastOperatorsList.Add(Cast(owner, owner, integer, false, 30));
        owner.CastOperatorsList.Add(Cast(owner, owner, fragment.Built.Type("Int64"), true, 40));
        owner.CastOperatorsList.Add(Cast(owner, integer, owner, true, 50));

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    [Fact]
    public void FailedParameterTypesDoNotProduceDuplicateFindings()
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Money");
        foreach(var offset in new[] { 20, 30 })
        {
            var op = Operator(owner, owner, offset);
            op.ParametersList[1].Type = ErrorType.Instance;
            owner.OperatorsList.Add(op);
            owner.CastOperatorsList.Add(Cast(owner, ErrorType.Instance, owner, true, offset));
        }

        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    private static Operator Operator(ConcreteType owner, IMistType returns, int offset)
    {
        return new Operator(OperatorKind.Plus, returns, [new Parameter("a", owner), new Parameter("b", owner)], DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(offset),
        };
    }

    private static CastOperator Cast(ConcreteType owner, IMistType source, IMistType target, bool isImplicit, int offset)
    {
        return new CastOperator(target, new Parameter("value", source), isImplicit, DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = owner,
            DeclarationNode = HandBuiltModel.Node(offset),
        };
    }
}