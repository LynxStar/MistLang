using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal enum ResultCase
{
    Success,
    Failure,
}

internal sealed record ResultTypes(ConstructedType Type, IMistType Success, IMistType Error)
{
    public static ResultTypes? Of(IMistType? type)
    {
        return type is ConstructedType { Definition.FullyQualifiedName: "Standard.Result<,>", TypeArguments: [var success, var error] } result
            ? new ResultTypes(result, success, error)
            : null;
    }

    public static (ResultCase Case, IMistType Payload)? Projection(IMistType type)
    {
        return type switch
        {
            ConstructedType { Definition.FullyQualifiedName: "Standard.ResultSuccess<>", TypeArguments: [var payload] } => (ResultCase.Success, payload),
            ConstructedType { Definition.FullyQualifiedName: "Standard.ResultFailure<>", TypeArguments: [var payload] } => (ResultCase.Failure, payload),
            _ => null,
        };
    }

    public ResultCase? Select(IMistType from, TypeInterner? interner, Int128? constant = null)
    {
        if(Projection(from) is { } projection)
        {
            return Fits(projection.Payload, projection.Case == ResultCase.Success ? Success : Error, interner) ? projection.Case : null;
        }

        if(Of(from) is not null)
        {
            return null;
        }

        var success = Fits(from, Success, interner, constant);
        var failure = Fits(from, Error, interner, constant);
        return success == failure ? null : success ? ResultCase.Success : ResultCase.Failure;
    }

    public static bool Fits(IMistType from, IMistType to, TypeInterner? interner, Int128? constant = null)
    {
        if(Of(to) is not null && !ConversionRelation.IsIdentity(from, to))
        {
            return false;
        }

        return ConversionClassifier.Classify(from, to, interner) is ConversionKind.Identity or ConversionKind.Implicit
            || (constant is { } value && ConstantConversion.Fits(value, from, to));
    }

    public Expression? Construct(Expression value, TypeInterner interner)
    {
        if(Select(value.Type, interner, ConstantConversion.IntegerConstant(value)) is not { } selected)
        {
            return null;
        }

        var payload = value;
        if(Projection(value.Type) is { } projection && value.Type is ConstructedType { Definition: ConcreteType definition })
        {
            payload = new MemberAccessExpression(value.Node, value, "Payload", projection.Payload,
                member: new FieldResult(definition.FieldsList.Single(field => field.Name == "Payload"), definition, []));
        }

        return new ResultConstructionExpression(value.Node, ImplicitConversions.At(payload, selected == ResultCase.Success ? Success : Error, interner), Type, selected);
    }

    public UnionType Inspection(TypeInterner? interner)
    {
        var method = ((ConcreteType)Type.Definition).MethodsList.Single(method => method.Name == "Inspect");
        return (UnionType)TypeSubstitution.ForReceiver(Type).Apply(method.ReturnType, interner);
    }
}