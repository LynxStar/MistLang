using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ReferenceLocationSiteTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("ref-local", "Cannot take a reference to the result of 'Abs': it is not a storage location, so it cannot be bound to ref local 'slot'.")]
    [InlineData("readonly-ref-local", "Cannot take a reference to property 'Count': it is not a storage location, so it cannot be bound to ref local 'view'.")]
    [InlineData("ref-return", "Cannot take a reference to a literal: it is not a storage location, so it cannot be returned by ref.")]
    [InlineData("ref-argument", "Cannot take a reference to the result of 'Abs': it is not a storage location, so it cannot be passed as a ref argument.")]
    [InlineData("out-argument", "Cannot take a reference to a literal: it is not a storage location, so it cannot be passed as an out argument.")]
    [InlineData("in-argument", "Cannot take a reference to the result of 'Abs': it is not a storage location, so it cannot be passed by reference to an in parameter.")]
    public void AReferenceToAValueIsRefusedWhereItIsTaken(string shape, string message)
    {
        var integer = Integer;
        object root = shape switch
        {
            "ref-local" => new LocalDeclarationStatement(new LocalVariable("slot", integer) { IsRef = true }, Abs(37)),
            "readonly-ref-local" => new LocalDeclarationStatement(
                new LocalVariable("view", integer) { IsRef = true, IsReadonly = true },
                new PropertyReferenceExpression(HandBuiltModel.Node(37), new Property("Count", integer, default))),
            "ref-return" => new ReturnStatement(HandBuiltModel.Literal(integer, 5L, 37), integer, isRef: true),
            "ref-argument" => Passing(Abs(37), ByRefDirection.Ref),
            "out-argument" => Passing(HandBuiltModel.Literal(integer, 5L, 37), ByRefDirection.Out),
            _ => Passing(Abs(37), ByRefDirection.In),
        };

        var diagnostic = Assert.Single(model.Analyze(new PositionAnalyzer(), root));
        Assert.Equal("MIST_PA_REF_NOT_A_LOCATION", diagnostic.Number.Value);
        Assert.StartsWith(message, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("ref-local-of-a-local")]
    [InlineData("ref-local-of-an-array-element")]
    [InlineData("ref-argument-of-a-local")]
    [InlineData("value-local-of-a-call")]
    public void AReferenceToStorageOrAPlainValueLocalIsAdmitted(string shape)
    {
        var integer = Integer;
        var array = model.Interner.GetOrCreateConstructedType(model.Type(WellKnownTypes.Array.Name), [integer, UnknownType.Instance]);
        object root = shape switch
        {
            "ref-local-of-a-local" => new LocalDeclarationStatement(new LocalVariable("alias", integer) { IsRef = true }, Local("n")),
            "ref-local-of-an-array-element" => new LocalDeclarationStatement(
                new LocalVariable("alias", integer) { IsRef = true },
                new IndexExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(array), HandBuiltModel.Literal(integer, 1L), integer)),
            "ref-argument-of-a-local" => Passing(Local("n"), ByRefDirection.Ref),
            _ => new LocalDeclarationStatement(new LocalVariable("copy", integer), Abs()),
        };

        Assert.Empty(model.Analyze(new PositionAnalyzer(), root));
    }

    private IMistType Integer => model.Type("Int32");

    private InvocationExpression Abs(int offset = 0)
    {
        var method = new Method("Abs", Integer, [], default);
        return new InvocationExpression(HandBuiltModel.Node(offset), HandBuiltModel.Literal(Integer), [], Integer, method, null);
    }

    private LocalReferenceExpression Local(string name)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, Integer));
    }

    private InvocationExpression Passing(Expression argument, ByRefDirection direction)
    {
        var location = new Parameter("location", Integer, new ParameterModifier(direction == ByRefDirection.In, true, OwnershipKind.Unrestricted, direction));
        var method = new Method("Exchange", Integer, [location], default);
        return new InvocationExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(Integer), [argument], Integer, method, null);
    }
}