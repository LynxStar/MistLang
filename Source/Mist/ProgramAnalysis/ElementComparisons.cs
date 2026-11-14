using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// CLR default comparers can call Mist IEquatable/IComparable members on behalf of List
/// operations and anonymous equality, regardless of the container's promise. Null means the
/// comparison reaches code outside the model; an empty result means no Mist callback.
/// </summary>
internal static class ElementComparisons
{
    public static IReadOnlyList<Method>? Of(InvocationExpression invocation, out IMistType? element)
    {
        element = null;

        if(invocation.Method is not { DeclaringType: ConcreteType { IsIntrinsic: true } list } method
            || MistType.StripArity(list.FullyQualifiedName) != "Standard.List"
            || ThisTiers.ReceiverOf(invocation)?.Type is not ConstructedType { TypeArguments: [var held] })
        {
            return [];
        }

        element = held;

        return (method.Name, method.ParametersList.Count) switch
        {
            ("Contains" or "IndexOf" or "Remove", 1) => Equality(held, viaObject: false, []),
            ("Sort", 0) => Ordering(held, []),
            _ => [],
        };
    }

    public static IReadOnlyList<Method>? OfAnonymous(BinaryExpression comparison)
    {
        if(comparison is not { Operator: null, OperatorKind: BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual }
            || (comparison.Left.Type is not AnonymousType && comparison.Right.Type is not AnonymousType))
        {
            return [];
        }

        return comparison.Left.Type is AnonymousType anonymous ? Members(anonymous, []) : null;
    }

    private static List<Method>? Members(AnonymousType anonymous, HashSet<IMistType> visiting)
    {
        return Combined(anonymous.Fields.Select(field => Equality(field.Type, viaObject: false, visiting)));
    }

    private static List<Method>? Equality(IMistType type, bool viaObject, HashSet<IMistType> visiting)
    {
        switch(type)
        {
            case LiftedType lifted:
                return Equality(lifted.Type, viaObject, visiting);
            case UnionType union when OptionalUnion.InnerType(union) is { } present:
                return Equality(present, viaObject, visiting);
            case UnionType union:
                return Combined(union.Constituents.Select(constituent => Equality(constituent, viaObject: true, visiting)));
            case INamedTupleType named:
                return Equality(named.Underlying, viaObject, visiting);
            case ITupleType tuple:
                return Combined(tuple.ElementTypes.Select(held => Equality(held, viaObject: false, visiting)));
            case AnonymousType anonymous:
                return visiting.Add(anonymous) ? Members(anonymous, visiting) : [];
            case TypeAlias { AliasedType: { } aliased }:
                return Equality(aliased, viaObject, visiting);
            case RefinedType refined:
                return Equality(refined.BaseType, viaObject, visiting);
            case Newtype { SourceType: { } source }:
                return Equality(source, viaObject, visiting);
            case EnumType or IFunctionType:
                return [];
        }

        if(OptionalUnion.IsVoid(type))
        {
            return [];
        }

        if(PurityJudge.DefinitionOf(type) is not ConcreteType concrete)
        {
            return null;
        }

        if(concrete.IsIntrinsic)
        {
            return [];
        }

        if(concrete.DeclarationNode is null || !visiting.Add(concrete))
        {
            return concrete.DeclarationNode is null ? null : [];
        }

        if(!viaObject && ClrCallbacks.Implements(concrete, "System.IEquatable"))
        {
            return ClrCallbacks.Answering(concrete, "Equals", 1) is { } equals ? [equals] : null;
        }

        return concrete is ClassType
            ? []
            : Combined(concrete.FieldsList.Where(field => !field.Modifiers.IsStatic).Select(field => Equality(field.Type, viaObject: true, visiting)));
    }

    private static List<Method>? Ordering(IMistType type, HashSet<IMistType> visiting)
    {
        switch(type)
        {
            case LiftedType lifted:
                return Ordering(lifted.Type, visiting);
            case INamedTupleType named:
                return Ordering(named.Underlying, visiting);
            case ITupleType tuple:
                return Combined(tuple.ElementTypes.Select(held => Ordering(held, visiting)));
            case TypeAlias { AliasedType: { } aliased }:
                return Ordering(aliased, visiting);
            case RefinedType refined:
                return Ordering(refined.BaseType, visiting);
            case EnumType:
                return [];
        }

        if(PurityJudge.DefinitionOf(type) is not ConcreteType concrete || (!concrete.IsIntrinsic && concrete.DeclarationNode is null))
        {
            return null;
        }

        if(concrete.IsIntrinsic || !visiting.Add(concrete) || !ClrCallbacks.Implements(concrete, "System.IComparable"))
        {
            return [];
        }

        return ClrCallbacks.Answering(concrete, "CompareTo", 1) is { } compare ? [compare] : null;
    }

    private static List<Method>? Combined(IEnumerable<List<Method>?> parts)
    {
        var all = new List<Method>();

        foreach(var part in parts)
        {
            if(part is null)
            {
                return null;
            }

            all.AddRange(part);
        }

        return all;
    }
}