using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class DelegateInvocationSignatureTests
{
    [Theory]
    [InlineData(ByRefDirection.Ref)]
    [InlineData(ByRefDirection.Out)]
    public void AByReferenceDelegateArgumentRequiresAStorageLocation(ByRefDirection direction)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var call = Call(integer, [HandBuiltModel.Literal(integer, 1L, 37)], [Parameter(integer, direction)]);

        var diagnostic = Assert.Single(model.Analyze(new PositionAnalyzer(), call));
        Assert.Equal("MIST_PA_REF_NOT_A_LOCATION", diagnostic.Id.Value);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData(ByRefDirection.Ref, true)]
    [InlineData(ByRefDirection.Out, true)]
    [InlineData(ByRefDirection.In, false)]
    [InlineData(ByRefDirection.None, false)]
    public void WritableDelegateArgumentsInvalidateRefinementFacts(ByRefDirection direction, bool invalidated)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var holder = new LocalVariable("value", integer);
        var call = Call(integer, [new LocalReferenceExpression(HandBuiltModel.Node(), holder)], [Parameter(integer, direction)]);

        Assert.Equal(invalidated, UnseenWrites.Of(call).Contains(holder));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADelegateRefResultRemainsAStorageLocationAndKeepsReadonlyRestriction(bool isReadonly)
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        var call = Call(integer, [], [], new MemberModifiers { IsRef = true, ReturnsReadonlyRef = isReadonly });
        var local = new LocalVariable("alias", integer) { IsRef = true };
        var diagnostics = model.Analyze(new PositionAnalyzer(), new LocalDeclarationStatement(local, call));

        if(isReadonly)
        {
            Assert.Equal("MIST_PA_READONLY_REF_WRITTEN", Assert.Single(diagnostics).Id.Value);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    private static Parameter Parameter(IMistType type, ByRefDirection direction)
    {
        return new Parameter("value", type,
            new ParameterModifier(direction == ByRefDirection.In, direction != ByRefDirection.None, OwnershipKind.Unrestricted, direction));
    }

    private static InvocationExpression Call(
        IMistType type, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter> parameters, MemberModifiers modifiers = default)
    {
        return new InvocationExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(type), arguments, type, null, parameters.Select(parameter => parameter.Type).ToArray())
        {
            InvokeMethod = new Method("Invoke", type, parameters.ToList(), modifiers),
        };
    }
}