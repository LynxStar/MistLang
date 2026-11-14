using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// Readonly describes the selected callee, not argument callbacks, ref writes or type initializers;
/// those can invalidate field facts independently. <see cref="ReadonlyInference"/> supplies the promise
/// only for local bodies; abstract instance dispatch and external bodies need declared readonly.
/// Reads judge the getter and writes the setter. A bodyless accessor needs declared readonly, auto storage
/// or intrinsic status: a CLR getter may run supplied code. Delegate calls and creation keep no facts.
/// </summary>
internal sealed class FieldEffects
{
    private readonly ReadonlyInference inference = new();

    public bool MayWrite(object node)
    {
        return node switch
        {
            InvocationExpression call => !Keeps(call, call.Method, call.Arguments, call.Parameters),
            BinaryExpression { Operator: { Modifiers.IsIntrinsic: false } called } binary => !Keeps(binary, called, [binary.Left, binary.Right], called.ParametersList),
            UnaryExpression { Operator: { Modifiers.IsIntrinsic: false } called } unary => !Keeps(unary, called, [unary.Operand], called.ParametersList),
            CastExpression { Operator: { Modifiers.IsIntrinsic: false } called } cast => !Keeps(cast, called, [cast.Operand], called.ParametersList),
            ImplicitConversionExpression { Operator: { Modifiers.IsIntrinsic: false } called } conversion =>
                !Keeps(conversion, called, [conversion.Operand], called.ParametersList),
            NewExpression => true,
            AssignmentExpression { Target: MemberAccessExpression { Member: PropertyResult { Property: var property } } } => !Keeps(property.Setter, property, property.Modifiers, property.DeclaringType),
            AssignmentExpression { Target: PropertyReferenceExpression { Property: var property } } => !Keeps(property.Setter, property, property.Modifiers, property.DeclaringType),
            AssignmentExpression { Target: ExtensionPropertyAccessExpression { Property: var property } } => !Keeps(property.Setter, property, property.Modifiers, property.DeclaringType),
            AssignmentExpression { Target: IndexExpression { Indexer: { } indexer } } => !Keeps(indexer.Setter, indexer, indexer.Modifiers, indexer.DeclaringType),
            AssignmentExpression => false,
            CompoundAssignmentExpression compound => MayWrite(CompoundAssignments.LongForm(compound))
                || MayWrite(CompoundAssignments.LongForm(compound).Value) || MayWrite(compound.Target),
            MemberAccessExpression { Member: PropertyResult { Property: var property } } => !Keeps(property.Getter, property, property.Modifiers, property.DeclaringType),
            PropertyReferenceExpression { Property: var property } => !Keeps(property.Getter, property, property.Modifiers, property.DeclaringType),
            ExtensionPropertyAccessExpression { Property: var property } => !Keeps(property.Getter, property, property.Modifiers, property.DeclaringType),
            IndexExpression { Indexer: { } indexer } => !Keeps(indexer.Getter, indexer, indexer.Modifiers, indexer.DeclaringType),
            _ => false,
        };
    }

    private bool Keeps(Expression node, Method? selected, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        if(selected is null)
        {
            return false;
        }

        for(var i = 0; i < arguments.Count; i++)
        {
            var parameter = parameters is not null && i < parameters.Count ? parameters[i] : null;

            if(parameter is null || parameter.Modifier.IsRef || parameter.Modifier.Direction is not ByRefDirection.None
                || arguments[i].Type is IFunctionType { IsPure: false })
            {
                return false;
            }
        }

        if(TypeInitializer.TriggeredBy(node).Any(trigger => TypeInitializer.Of(trigger.Type).Initializers().Any()))
        {
            return false;
        }

        var method = selected.SourceDeclaration ?? selected;

        return method.Modifiers.IsReadonly || method is CastOperator { CopiesClone: true }
            || (method is not { DeclaringType: AbstractType, Modifiers.IsStatic: false } && method.Body is not null && inference.IsReadonly(method));
    }

    private bool Keeps(PropertyAccessor? accessor, object member, MemberModifiers owner, MemberType? declaring)
    {
        if(accessor is null || ReadonlyInference.Declared(accessor, owner).IsReadonly)
        {
            return true;
        }

        if(declaring is AbstractType)
        {
            return false;
        }

        if(accessor.IsAuto || (accessor.Body is null && accessor.ExpressionBody is null))
        {
            return accessor.IsAuto || owner.IsIntrinsic;
        }

        return (member, ReferenceEquals(accessor, Getter(member))) switch
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
}