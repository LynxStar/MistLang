using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal readonly record struct EnumerationMembers(Method? GetEnumerator, Method? MoveNext, Property? Current);

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

    public static EnumerationMembers Members(IMistType collection, TypeInterner interner)
    {
        if(Declaring(collection) is not { } declaring
            || declaring is ConcreteType { IsIntrinsic: true }
            || Find(declaring, "GetEnumerator") is not MethodGroupResult group
            || Parameterless(group) is not { } getEnumerator)
        {
            return default;
        }

        var path = group.Paths is { } paths && paths.TryGetValue(getEnumerator, out var reached) ? reached : null;
        var enumerator = TypeSubstitution.ForPath(collection as ConstructedType, path, interner).Apply(getEnumerator.ReturnType, interner);

        if(Declaring(enumerator) is not { } enumerating)
        {
            return new EnumerationMembers(getEnumerator, null, null);
        }

        var moveNext = Find(enumerating, "MoveNext") is MethodGroupResult moving ? Parameterless(moving) : null;
        var current = Find(enumerating, "Current") as PropertyResult;

        return new EnumerationMembers(getEnumerator, moveNext, current?.Property);
    }

    private static LookupResult Find(MistType declaring, string name)
    {
        return TypeMemberLookup.Find(declaring, LookupRequest.Member(name, MemberVisibility.InstanceOnly, null));
    }

    private static Method? Parameterless(MethodGroupResult group)
    {
        return group.Methods.FirstOrDefault(method => method.ParametersList.Count == 0 && !method.Modifiers.IsStatic);
    }

    private static MistType? Declaring(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => Declaring(aliased),
            RefinedType refined => Declaring(refined.BaseType),
            ConstructedType constructed => Declaring(constructed.Definition),
            MistType declared => declared,
            _ => null,
        };
    }

    /// <summary>
    /// Walks the conformance graph outward, carrying the substitution each step contributes so a
    /// generic sequence closed at its use site yields the closed element -- <c>MyList&lt;int&gt;</c>
    /// declaring <c>: IEnumerable&lt;T&gt;</c> answers int rather than T. Each step's arguments are
    /// closed through the substitution so far, since a projected <c>List&lt;int&gt;</c> reaches
    /// <c>IEnumerable</c> only through <c>IList</c> and <c>ICollection</c>, each naming the previous
    /// one's parameter, and a binding that stops one step short answers with that parameter.
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
                var closed = constructed.TypeArguments
                    .Select(argument => substitution.Apply(argument, interner: null))
                    .ToList();

                substitution = substitution.Extend(TypeSubstitution.ForParameters(generic.TypeParametersList, closed));
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