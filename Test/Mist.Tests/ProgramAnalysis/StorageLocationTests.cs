using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class StorageLocationTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [InlineData("local")]
    [InlineData("field")]
    [InlineData("static-field-access")]
    [InlineData("class-receiver-field")]
    [InlineData("struct-local-field")]
    [InlineData("array-element")]
    [InlineData("refined-array-element")]
    [InlineData("span-element")]
    [InlineData("readonly-span-element")]
    [InlineData("ref-call")]
    [InlineData("ref-property")]
    [InlineData("ref-indexer")]
    [InlineData("converted-local")]
    [InlineData("this")]
    public void AStorageLocationOrAnUndecidedShapeIsAdmitted(string shape)
    {
        Assert.Null(StorageLocations.NotALocation(Shaped(shape)));
    }

    [Theory]
    [InlineData("value-call", "the result of 'Abs'")]
    [InlineData("function-value-call", "the result of a call")]
    [InlineData("property", "property 'Count'")]
    [InlineData("indexer", "the indexer of 'Bag'")]
    [InlineData("string-element", "an element of 'string'")]
    [InlineData("slice", "a slice")]
    [InlineData("literal", "a literal")]
    [InlineData("sum", "a computed value")]
    [InlineData("struct-result-field", "a field of the result of 'Origin'")]
    public void AValueIsNamedForTheDiagnostic(string shape, string expected)
    {
        Assert.Equal(expected, StorageLocations.NotALocation(Shaped(shape)));
    }

    [Theory]
    [InlineData(ByRefDirection.Ref, "passed as a ref argument")]
    [InlineData(ByRefDirection.Out, "passed as an out argument")]
    [InlineData(ByRefDirection.In, "passed by reference to an in parameter")]
    [InlineData(ByRefDirection.None, null)]
    public void EveryByReferenceParameterTakesAReference(ByRefDirection direction, string? expected)
    {
        var modifier = new ParameterModifier(direction == ByRefDirection.In, direction != ByRefDirection.None, OwnershipKind.Unrestricted, direction);

        Assert.Equal(expected, PositionDecision.ByReferenceArgument(modifier));
    }

    private IMistType Integer => model.Type("Int32");

    private Expression Shaped(string shape)
    {
        var integer = Integer;
        var holder = HandBuiltModel.Class("Holder");
        var point = HandBuiltModel.Struct("Point");
        var x = new Field("X", integer, default, default);
        var origin = new Method("Origin", point, [], default);

        return shape switch
        {
            "local" => Local(integer),
            "field" => new FieldReferenceExpression(HandBuiltModel.Node(), x),
            "static-field-access" => FieldOf(HandBuiltModel.Literal(point), new Field("Zero", integer, default, new MemberModifiers { IsStatic = true })),
            "class-receiver-field" => FieldOf(Call(new Method("Make", holder, [], default)), x),
            "struct-local-field" => FieldOf(Local(point), x),
            "array-element" => Element(Constructed(WellKnownTypes.Array.Name), HandBuiltModel.Literal(integer, 1L)),
            "refined-array-element" => Element(HandBuiltModel.Refined(Constructed(WellKnownTypes.Array.Name)), HandBuiltModel.Literal(integer, 1L)),
            "span-element" => Element(Constructed(WellKnownTypes.Span.Name), HandBuiltModel.Literal(integer, 1L)),
            "readonly-span-element" => Element(Constructed(WellKnownTypes.ReadOnlySpan.Name), HandBuiltModel.Literal(integer, 1L)),
            "ref-call" => Call(new Method("Pick", integer, [], new MemberModifiers { IsRef = true })),
            "ref-property" => new PropertyReferenceExpression(HandBuiltModel.Node(), new Property("Slot", integer, new MemberModifiers { IsRef = true })),
            "ref-indexer" => IndexedBy(new MemberModifiers { IsRef = true }),
            "converted-local" => new ImplicitConversionExpression(HandBuiltModel.Node(), Local(integer), integer, []),
            "this" => new ThisExpression(HandBuiltModel.Node(), holder),
            "value-call" => Call(new Method("Abs", integer, [], default)),
            "function-value-call" => new InvocationExpression(HandBuiltModel.Node(), Local(integer), [], integer, null, null),
            "property" => new PropertyReferenceExpression(HandBuiltModel.Node(), new Property("Count", integer, default)),
            "indexer" => IndexedBy(default),
            "string-element" => Element(model.Type("String"), HandBuiltModel.Literal(integer, 1L)),
            "slice" => Element(Constructed(WellKnownTypes.Array.Name), HandBuiltModel.Literal(model.Type("Range"))),
            "literal" => HandBuiltModel.Literal(integer, 5L),
            "sum" => new BinaryExpression(HandBuiltModel.Node(), Local(integer), BinaryOperatorKind.Add, Local(integer), integer),
            _ => FieldOf(Call(origin), x),
        };
    }

    private static LocalReferenceExpression Local(IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("n", type));
    }

    private static MemberAccessExpression FieldOf(Expression receiver, Field field)
    {
        return new MemberAccessExpression(
            HandBuiltModel.Node(), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private static InvocationExpression Call(Method method)
    {
        return new InvocationExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(method.ReturnType), [], method.ReturnType, method, null);
    }

    private IMistType Constructed(string definition)
    {
        return model.Interner.GetOrCreateConstructedType(model.Type(definition), [Integer, UnknownType.Instance]);
    }

    private IndexExpression Element(IMistType receiver, Expression index)
    {
        return new IndexExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(receiver), index, Integer);
    }

    private IndexExpression IndexedBy(MemberModifiers modifiers)
    {
        return new IndexExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(HandBuiltModel.Class("Bag")), HandBuiltModel.Literal(Integer, 1L), Integer)
        {
            Indexer = new Indexer(Integer, [new Parameter("index", Integer)], modifiers),
        };
    }
}