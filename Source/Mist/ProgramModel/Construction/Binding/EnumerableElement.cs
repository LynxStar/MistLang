using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a <c>foreach</c> hands out one of, resolved from the collection's type.
/// <para>
/// Read from the ENUMERABLE CONFORMANCE rather than from a conventionally named <c>GetEnumerator</c>,
/// so the element type is whatever <c>Standard.IEnumerable</c> was closed with and a type that merely
/// happens to have a method of that name is not mistaken for a sequence. An array answers separately,
/// being a construction of <c>Standard.Array</c> rather than a declared conformance.
/// </para>
/// </summary>
internal static class EnumerableElement
{
    /// <summary>
    /// The element type <paramref name="collection"/> yields, or null when it is not a sequence.
    /// </summary>
    public static IMistType? Of(IMistType collection)
    {
        if(Argument(collection, WellKnownTypes.Array) is { } element)
        {
            return element;
        }

        return Search(collection, TypeSubstitution.Empty, depth: 0);
    }

    /// <summary>
    /// Walks the conformance graph outward, carrying the substitution each step contributes so a
    /// generic sequence closed at its use site yields the closed element -- <c>MyList&lt;int&gt;</c>
    /// declaring <c>: IEnumerable&lt;T&gt;</c> answers int rather than T.
    /// </summary>
    private static IMistType? Search(IMistType type, TypeSubstitution substitution, int depth)
    {
        // A conformance graph is acyclic by construction, but a malformed model must not hang the
        // compiler, and no legitimate hierarchy is anywhere near this deep.
        if(depth > 32)
        {
            return null;
        }

        if(Argument(type, WellKnownTypes.IEnumerable) is { } element)
        {
            return substitution.Apply(element, interner: null);
        }

        if(type is ConstructedType constructed)
        {
            var definition = constructed.Definition;

            if(definition is MemberType generic)
            {
                substitution = substitution.Extend(
                    TypeSubstitution.ForParameters(generic.TypeParametersList, constructed.TypeArguments));
            }

            return Search(definition, substitution, depth + 1);
        }

        if(type is not MemberType member)
        {
            return null;
        }

        foreach(var reached in Reached(member))
        {
            if(Search(reached, substitution, depth + 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<IMistType> Reached(MemberType member)
    {
        foreach(var abstractBase in member.BaseAbstractTypes)
        {
            yield return abstractBase;
        }

        if(member is ConcreteType concrete)
        {
            foreach(var conformance in concrete.ConformancesList)
            {
                yield return conformance.AbstractType;
            }

            if(concrete.BaseType is { } baseType)
            {
                yield return baseType;
            }
        }
    }

    /// <summary>
    /// The first type argument of <paramref name="type"/> when it is a construction of
    /// <paramref name="definition"/>, which both well-known sequence types put the element in.
    /// </summary>
    private static IMistType? Argument(IMistType type, WellKnownType definition)
    {
        return type is ConstructedType { TypeArguments.Count: > 0 } constructed
            && constructed.Definition.FullyQualifiedName == definition.FullyQualifiedName
                ? constructed.TypeArguments[0]
                : null
                ;
    }
}