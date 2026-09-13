using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// A generic source named with type arguments inside its clone's body names the newtype exactly when some
/// instance of the newtype is a clone of that construction (newtypes spec Requirement 3.2). In a newtype over
/// a Box of int, a Box of T is the newtype and a Box of string stays a Box; in a generic newtype over a Box
/// of its own V, a Box of string is that newtype of string.
/// </summary>
internal static class CloneRenaming
{
    public static IMistType? Rename(Scope scope, IMistType definition, IReadOnlyList<IMistType> arguments, TypeInterner interner)
    {
        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current is CloneScope cloneScope)
            {
                return cloneScope is { Clone: { } clone, Arguments: { } pattern } && ReferenceEquals(cloneScope.Source, definition)
                    ? Match(clone, pattern, arguments, interner)
                    : null;
            }
        }

        return null;
    }

    private static IMistType? Match(
        ConcreteType clone, IReadOnlyList<IMistType> pattern, IReadOnlyList<IMistType> arguments, TypeInterner interner)
    {
        var bindings = new Dictionary<TypeParameter, IMistType>();

        if(!AllMatch(pattern, arguments, clone, bindings))
        {
            return null;
        }

        return clone.TypeParametersList.Count == 0
            ? clone
            : interner.GetOrCreateConstructedType(
                clone, [.. clone.TypeParametersList.Select(parameter => bindings.GetValueOrDefault(parameter) ?? parameter)]);
    }

    private static bool AllMatch(
        IReadOnlyList<IMistType> expected, IReadOnlyList<IMistType> actual, ConcreteType clone, Dictionary<TypeParameter, IMistType> bindings)
    {
        return expected.Count == actual.Count
            && Enumerable.Range(0, expected.Count).All(i => Matches(expected[i], actual[i], clone, bindings));
    }

    private static bool Matches(IMistType expected, IMistType actual, ConcreteType clone, Dictionary<TypeParameter, IMistType> bindings)
    {
        if(expected is TypeParameter variable && clone.TypeParametersList.Contains(variable))
        {
            if(bindings.TryGetValue(variable, out var bound))
            {
                return Same(bound, actual);
            }

            bindings[variable] = actual;
            return true;
        }

        return expected is ConstructedType expectedConstruction && actual is ConstructedType actualConstruction
            ? ReferenceEquals(expectedConstruction.Definition, actualConstruction.Definition)
                && AllMatch(expectedConstruction.TypeArguments, actualConstruction.TypeArguments, clone, bindings)
            : Same(expected, actual);
    }

    private static bool Same(IMistType left, IMistType right)
    {
        if(ReferenceEquals(left, right))
        {
            return true;
        }

        if(left is ConstructedType leftConstruction && right is ConstructedType rightConstruction)
        {
            return ReferenceEquals(leftConstruction.Definition, rightConstruction.Definition)
                && leftConstruction.TypeArguments.Count == rightConstruction.TypeArguments.Count
                && Enumerable.Range(0, leftConstruction.TypeArguments.Count)
                    .All(i => Same(leftConstruction.TypeArguments[i], rightConstruction.TypeArguments[i]));
        }

        return left is not TypeParameter && right is not TypeParameter && left.FullyQualifiedName == right.FullyQualifiedName;
    }
}