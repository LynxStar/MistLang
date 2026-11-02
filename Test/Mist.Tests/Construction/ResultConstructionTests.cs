using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "1")]
public sealed class ResultConstructionTests
{
    private readonly ResultModel shapes = new();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AUniquePayloadSelectsItsCase(bool success)
    {
        var result = shapes.Result(shapes.Integer, shapes.Text);
        var payload = Literal(success ? shapes.Integer : shapes.Text);
        var constructed = Assert.IsType<ResultConstructionExpression>(ImplicitConversions.At(payload, result, shapes.Built.Interner));
        Assert.Same(payload, constructed.Payload);
        Assert.Equal(success ? ResultCase.Success : ResultCase.Failure, constructed.Case);
    }

    [Fact]
    public void OverlapAndNeitherPayloadHaveNoImplicitConstruction()
    {
        var overlap = shapes.Result(shapes.Text, shapes.Text);
        Assert.Null(ResultTypes.Of(overlap)!.Select(shapes.Text, shapes.Built.Interner));
        Assert.Null(ResultTypes.Of(shapes.Result(shapes.Integer, shapes.Text))!.Select(shapes.Boolean, shapes.Built.Interner));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProjectionsSelectOverlappingCasesWithoutTheOtherArgument(bool success)
    {
        var result = shapes.Result(shapes.Text, shapes.Text);
        var projected = Literal(shapes.Projection(shapes.Text, success));
        var constructed = Assert.IsType<ResultConstructionExpression>(ImplicitConversions.At(projected, result, shapes.Built.Interner));
        Assert.Equal(success ? ResultCase.Success : ResultCase.Failure, constructed.Case);
        Assert.Same(shapes.Text, constructed.Payload.Type);
    }

    [Fact]
    public void ExistingResultsPreserveIdentityAndCannotFlattenOrWiden()
    {
        var result = shapes.Result(shapes.Integer, shapes.Text);
        var value = Literal(result);
        Assert.Same(value, ImplicitConversions.At(value, result, shapes.Built.Interner));
        Assert.False(ConversionClassifier.IsImplicit(result, shapes.Result(shapes.Built.Type("Int64"), shapes.Text), shapes.Built.Interner));
        Assert.False(ConversionClassifier.IsImplicit(result, shapes.Integer, shapes.Built.Interner));
        var erased = shapes.Result(ProgramModel.Model.Types.UnknownType.Instance, shapes.Text);
        Assert.False(ConversionClassifier.IsImplicit(result, erased, shapes.Built.Interner));
        Assert.False(ConversionRelation.IsWithin(result, erased));
        Assert.Null(ResultTypes.Of(shapes.Result(result, shapes.Text))!.Select(result, shapes.Built.Interner));
        Assert.Null(ResultTypes.Of(shapes.Result(result, shapes.Text))!.Select(shapes.Integer, shapes.Built.Interner));
    }

    [Fact]
    public void OptionalVoidAndNestedPayloadsKeepDistinctTaggedTuples()
    {
        var optional = shapes.Built.Interner.GetOrCreateUnionType([shapes.Integer, shapes.Void]);
        foreach(var payload in new[] { shapes.Text, optional, shapes.Void, shapes.Result(shapes.Integer, shapes.Text) })
        {
            var inspection = shapes.Inspection(payload, payload);
            Assert.Equal(2, inspection.Constituents.Count);
            Assert.NotSame(inspection.Constituents[0], inspection.Constituents[1]);
        }
    }

    [Fact]
    public void AResultCannotBeDefaultConstructed()
    {
        var result = shapes.Result(shapes.Integer, shapes.Text);
        Assert.False(Defaultability.HasDefault(result));
        Assert.False(Defaultability.HasImplicitConstructor(result));
    }

    [Fact]
    public void AnUninferredPayloadStaysPlain()
    {
        var payload = Literal(shapes.Text, "data");
        Assert.Same(payload, ImplicitConversions.At(payload, null, shapes.Built.Interner));
    }

    [Fact]
    public void AnUnrelatedMethodNamedAsFailureDoesNotTransferItsReceiver()
    {
        var receiver = Literal(shapes.Text);
        var projection = shapes.Projection(shapes.Integer, false);
        var method = new Method("AsFailure", projection, [], default) { DeclaringBlock = new ExtensionBlock("self", "Probe", new Scope(null), null) };
        var call = new InvocationExpression(Node(), new MemberAccessExpression(Node(), receiver, "AsFailure", projection), [], projection, method, []);
        Assert.Null(ResultTransfers.Source(call));
    }

    [Fact]
    public void AProjectionConstructorRetainsItsPayloadAsTheTransferredSource()
    {
        var payload = Literal(shapes.Text);
        var projection = shapes.Projection(shapes.Text, true);
        var created = new NewExpression(Node(), projection, [payload], null, [], [shapes.Text]);
        Assert.Same(payload, ResultTransfers.Source(created));
    }

}