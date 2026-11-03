using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class ResultArithmeticSelectionTests
{
    [Fact]
    public void CompatibleDistinctErrorsCloseTheDeclaredOperator()
    {
        var f = new Fixture();
        var selected = OperatorSelection.Select(OperatorKind.Plus,
            [f.Shapes.Result(f.Integer, f.Error), f.Shapes.Result(f.Integer, f.Division)], null, f.Shapes.Built.Interner).Selected;

        Assert.NotNull(selected);
        Assert.Same(f.Generic, selected.Declaration);
        Assert.Same(f.Error, ResultTypes.Of(selected.ReturnType)!.Error);
        Assert.Same(f.Division, selected.Closing.Apply(f.Generic.TypeParametersList[1], f.Shapes.Built.Interner));
    }

    [Fact]
    public void ACustomErrorComposesWithoutResultVariance()
    {
        var f = new Fixture();
        var custom = HandBuiltModel.Class("CustomError", f.Error);
        var selected = OperatorSelection.Select(OperatorKind.Plus,
            [f.Shapes.Result(f.Integer, custom), f.Shapes.Result(f.Integer, f.Division)], null, f.Shapes.Built.Interner).Selected;

        Assert.NotNull(selected);
        Assert.Same(f.Generic, selected.Declaration);
        Assert.False(ResultTypes.Fits(f.Shapes.Result(f.Integer, custom), f.Shapes.Result(f.Integer, f.Error), f.Shapes.Built.Interner));
    }

    [Fact]
    public void AnErrorUnionClosesAnOrdinaryGenericSignature()
    {
        var f = new Fixture();
        var custom = HandBuiltModel.Class("CustomError", f.Error);
        var errors = f.Shapes.Built.Interner.GetOrCreateUnionType([custom, f.Division]);
        Assert.NotNull(OperatorSelection.Select(OperatorKind.Plus,
            [f.Shapes.Result(f.Integer, errors), f.Shapes.Result(f.Integer, f.Division)], null, f.Shapes.Built.Interner).Selected);
    }

    [Fact]
    public void DistinctErrorDomainsCanBeRetainedByADeclaredGenericContract()
    {
        var f = new Fixture();
        var unrelated = HandBuiltModel.Class("Unrelated");
        var selected = OperatorSelection.Select(OperatorKind.Plus,
            [f.Shapes.Result(f.Integer, unrelated), f.Shapes.Result(f.Integer, f.Division)], null, f.Shapes.Built.Interner).Selected;

        Assert.NotNull(selected);
    }

    [Fact]
    public void BareValuesDoNotSelectCompositionThroughPayloadConstruction()
    {
        var f = new Fixture();
        Assert.Null(OperatorSelection.Select(OperatorKind.Plus, [f.Integer, f.Integer], null, f.Shapes.Built.Interner).Selected);
    }

    [Fact]
    public void AnExactNativeErrorOverloadWinsOverGenericComposition()
    {
        var f = new Fixture();
        var type = f.Shapes.Result(f.Integer, f.Division);
        var precise = new Operator(OperatorKind.Plus, type, [new Parameter("a", type), new Parameter("b", type)],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = f.Integer };
        f.Integer.OperatorsList.Add(precise);
        Assert.Same(precise, OperatorSelection.Select(OperatorKind.Plus, [type, type], null, f.Shapes.Built.Interner).Selected!.Declaration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UserOperatorsOnEitherOperandKeepTheirResultParameters(bool resultOnRight)
    {
        var f = new Fixture();
        var weight = HandBuiltModel.Class("Weight");
        var result = f.Shapes.Result(f.Integer, f.Division);
        var operands = resultOnRight ? new IMistType[] { weight, result } : new IMistType[] { result, weight };
        var declared = new Operator(OperatorKind.Plus, f.Integer,
            [new Parameter("a", operands[0]), new Parameter("b", operands[1])], new MemberModifiers { IsStatic = true })
        { DeclaringType = weight };
        weight.OperatorsList.Add(declared);

        Assert.Same(declared, OperatorSelection.Select(OperatorKind.Plus, operands, null, f.Shapes.Built.Interner).Selected?.Declaration);
    }

    private sealed class Fixture
    {
        public ResultModel Shapes { get; } = new();
        public ClassType Integer => (ClassType)Shapes.Integer;
        public ClassType Error { get; }
        public ClassType Division { get; }
        public Operator Generic { get; }

        public Fixture()
        {
            Error = HandBuiltModel.Class("Error");
            Division = HandBuiltModel.Class("DivisionError", Error);
            Generic = new Operator(OperatorKind.Plus, Shapes.Result(Integer, Error), [], new MemberModifiers { IsStatic = true })
            {
                DeclaringType = Integer,
                DeclarationNode = HandBuiltModel.Node(),
            };
            var left = new TypeParameter("E", 0, Integer.Scope, null) { DeclaringMethod = Generic };
            var right = new TypeParameter("F", 1, Integer.Scope, null) { DeclaringMethod = Generic };
            Generic.TypeParametersList.AddRange([left, right]);
            Generic.ParametersList.AddRange([
                new Parameter("a", Shapes.Result(Integer, left)), new Parameter("b", Shapes.Result(Integer, right)),
            ]);
            Integer.OperatorsList.Add(Generic);
        }
    }
}