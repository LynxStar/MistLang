using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// Whether evaluating one node may write a field whose fact the refinement flow holds. A call keeps the
/// facts only when the method it selected is readonly -- declared, or inferred by
/// <see cref="ReadonlyInference"/> from a body this program owns -- and nothing else the call runs could
/// write: no argument is passed by reference, no argument is a function value that is not pure, and no
/// type initializer with work to do is triggered. A read asks only the getter it runs and a write only
/// the setter. Readonly is a promise about the callee, so a slot of an abstract type, whose body is its
/// implementers', counts only what it declares, and a method with no body here counts only what it
/// declares; a delegate call and an object creation never keep anything.
/// </summary>
internal sealed class FieldEffects
{
    private readonly ReadonlyInference inference = new();

    public bool MayWrite(object node)
    {
        return node switch
        {
            InvocationExpression call => !Keeps(call),
            NewExpression => true,
            AssignmentExpression { Target: MemberAccessExpression { Member: PropertyResult { Property: var property } } } => !Keeps(property.Setter, property, property.Modifiers, property.DeclaringType),
            AssignmentExpression { Target: PropertyReferenceExpression { Property: var property } } => !Keeps(property.Setter, property, property.Modifiers, property.DeclaringType),
            AssignmentExpression { Target: IndexExpression { Indexer: { } indexer } } => !Keeps(indexer.Setter, indexer, indexer.Modifiers, indexer.DeclaringType),
            AssignmentExpression => false,
            MemberAccessExpression { Member: PropertyResult { Property: var property } } => !Keeps(property.Getter, property, property.Modifiers, property.DeclaringType),
            PropertyReferenceExpression { Property: var property } => !Keeps(property.Getter, property, property.Modifiers, property.DeclaringType),
            IndexExpression { Indexer: { } indexer } => !Keeps(indexer.Getter, indexer, indexer.Modifiers, indexer.DeclaringType),
            _ => false,
        };
    }

    private bool Keeps(InvocationExpression call)
    {
        if(call.Method is not { } selected)
        {
            return false;
        }

        for(var i = 0; i < call.Arguments.Count; i++)
        {
            var parameter = call.Parameters is { } parameters && i < parameters.Count ? parameters[i] : null;

            if(parameter is null || parameter.Modifier.IsRef || parameter.Modifier.Direction is not ByRefDirection.None
                || call.Arguments[i].Type is IFunctionType { IsPure: false })
            {
                return false;
            }
        }

        if(TypeInitializer.TriggeredBy(call).Any(trigger => TypeInitializer.Of(trigger.Type).Initializers().Any()))
        {
            return false;
        }

        var method = selected.SourceDeclaration ?? selected;

        return method.Modifiers.IsReadonly
            || (method is not { DeclaringType: AbstractType, Modifiers.IsStatic: false } && method.Body is not null && inference.IsReadonly(method));
    }

    private bool Keeps(PropertyAccessor? accessor, object member, MemberModifiers owner, MemberType? declaring)
    {
        if(accessor is null || !HasBody(accessor) || ReadonlyInference.Declared(accessor, owner).IsReadonly)
        {
            return true;
        }

        return declaring is not AbstractType && (member, ReferenceEquals(accessor, Getter(member))) switch
        {
            (Property property, true) => inference.IsReadonlyGetter(property),
            (Indexer indexer, true) => inference.IsReadonlyGetter(indexer),
            _ => inference.IsReadonlySetter(accessor),
        };
    }

    private static PropertyAccessor? Getter(object member)
    {
        return member switch
        {
            Property property => property.Getter,
            Indexer indexer => indexer.Getter,
            _ => null,
        };
    }

    private static bool HasBody(PropertyAccessor accessor)
    {
        return accessor is { IsAuto: false } && (accessor.Body is not null || accessor.ExpressionBody is not null);
    }
}