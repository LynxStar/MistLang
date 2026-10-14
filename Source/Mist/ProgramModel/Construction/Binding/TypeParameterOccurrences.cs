using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Which type parameters a type mentions, and whether a type is closed with <c>unknown</c>, both read
/// structurally. A bare member type is its own instantiation -- <c>this</c> inside <c>Box</c> is a
/// <c>Box</c> of its own parameter -- so it mentions the parameters of its declaration and of every
/// type enclosing it. An array's unknown LENGTH is a value of known domain, never the top type, so a
/// dependent position bound to <c>unknown</c> does not close a type with it; the same distinction keeps
/// every array field out of the storage ban.
/// </summary>
internal static class TypeParameterOccurrences
{
    public static HashSet<TypeParameter> In(IMistType type)
    {
        var found = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);
        Collect(type, found);
        return found;
    }

    public static bool Mentions(IMistType type, TypeParameter parameter)
    {
        return In(type).Contains(parameter);
    }

    public static void Collect(IMistType type, ISet<TypeParameter> into)
    {
        switch(type)
        {
            case PackElementType element:
                _ = into.Add(element.Pack);
                break;

            case TypeParameter parameter:
                _ = into.Add(parameter);
                break;

            case ConstructedType constructed:
                CollectAll(constructed.TypeArguments, into);

                if(constructed.Enclosing is { } enclosing)
                {
                    Collect(enclosing, into);
                }

                break;

            case UnionType union:
                CollectAll(union.Constituents, into);
                break;

            case NamedTupleType named:
                CollectAll(named.ElementTypes, into);
                break;

            case TupleType tuple:
                CollectAll(tuple.ElementTypes, into);
                break;

            case FunctionType function:
                CollectAll(function.ParameterTypes, into);
                Collect(function.ReturnType, into);
                break;

            case RefinedType refined:
                Collect(refined.BaseType, into);
                break;

            case LiftedType lifted:
                Collect(lifted.Type, into);
                break;

            case SpreadType spread:
                Collect(spread.Operand, into);
                break;

            case TypeAlias { AliasedType: { } aliased }:
                Collect(aliased, into);
                break;

            case MemberType or EnumType:
                for(var declaration = (MistType)type; declaration is not null; declaration = declaration.DeclaringType)
                {
                    if(declaration is IHasTypeParameters generic)
                    {
                        into.UnionWith(generic.TypeParametersList);
                    }
                }

                break;
        }
    }

    private static void CollectAll(IReadOnlyList<IMistType> types, ISet<TypeParameter> into)
    {
        for(var i = 0; i < types.Count; i++)
        {
            Collect(types[i], into);
        }
    }

    public static bool BearsUnknown(IMistType type)
    {
        return type switch
        {
            UnknownType => true,
            ConstructedType constructed => BearsUnknownArgument(constructed)
                || (constructed.Enclosing is { } enclosing && BearsUnknown(enclosing)),
            UnionType union => union.Constituents.Any(BearsUnknown),
            NamedTupleType named => named.ElementTypes.Any(BearsUnknown),
            TupleType tuple => tuple.ElementTypes.Any(BearsUnknown),
            FunctionType function => function.ParameterTypes.Any(BearsUnknown) || BearsUnknown(function.ReturnType),
            RefinedType refined => BearsUnknown(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => BearsUnknown(aliased),
            _ => false,
        };
    }

    private static bool BearsUnknownArgument(ConstructedType constructed)
    {
        var parameters = (constructed.Definition as IHasTypeParameters)?.TypeParametersList;

        for(var i = 0; i < constructed.TypeArguments.Count; i++)
        {
            if(parameters?.Find(parameter => parameter.Ordinal == i) is { IsDependent: true })
            {
                continue;
            }

            if(BearsUnknown(constructed.TypeArguments[i]))
            {
                return true;
            }
        }

        return false;
    }
}