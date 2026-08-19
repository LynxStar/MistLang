using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A union type composed of multiple resolved constituent types.
/// Not registered in a namespace — constructed on-the-fly during type resolution.
/// <para>
/// Constituents are canonical by construction: flattened, deduplicated and sorted, so
/// spelling order never reaches identity. <see cref="NormalizeConstituents"/> is the one
/// definition of that form, shared by the interner and by substitution's interner-less
/// path. The qualified name is built from constituent QUALIFIED names because the
/// conversion relation compares types by that name, and two unions over same-named types
/// from different namespaces must not collide.
/// </para>
/// </summary>
internal sealed class UnionType : MistType, IUnionType
{
    public UnionType(IReadOnlyList<IMistType> constituents)
        : base(
            string.Join(" | ", constituents.Select(c => c.Name)),
            string.Join(" | ", constituents.Select(c => c.FullyQualifiedName)),
            new Scope(null),
            declaringType: null)
    {
        Constituents = constituents;
    }

    public IReadOnlyList<IMistType> Constituents { get; }

    IReadOnlyList<IMistType> IUnionType.Constituents => Constituents;

    public static List<IMistType> NormalizeConstituents(IReadOnlyList<IMistType> constituents)
    {
        var flattened = new List<IMistType>(constituents.Count);
        Flatten(constituents, flattened);

        var distinct = new List<IMistType>(flattened.Count);

        foreach(var type in flattened)
        {
            if(!distinct.Exists(seen => ReferenceEquals(seen, type)))
            {
                distinct.Add(type);
            }
        }

        return distinct
            .OrderBy(type => type.FullyQualifiedName, StringComparer.Ordinal)
            .ToList()
            ;
    }

    private static void Flatten(IReadOnlyList<IMistType> constituents, List<IMistType> into)
    {
        foreach(var type in constituents)
        {
            if(type is UnionType union)
            {
                Flatten(union.Constituents, into);
            }
            else
            {
                into.Add(type);
            }
        }
    }
}