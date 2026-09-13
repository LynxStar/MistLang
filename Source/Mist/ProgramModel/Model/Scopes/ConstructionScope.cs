using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A generic declaration's body bound for one construction of it, as a conversion to a constructed clonable
/// source runs that source's Clone (newtypes spec Requirement 10.1): the declaration's type parameters answer
/// with the construction's arguments, and a member the declaration answers is reached through the
/// construction, so its type is the construction's.
/// </summary>
internal sealed class ConstructionScope(Scope parent, ConstructedType construction) : Scope(parent)
{
    internal override LookupResult Lookup(LookupRequest request)
    {
        var self = LookupSelf(request);

        return self is not NotFoundResult ? self : Through(Parent?.Lookup(request) ?? new NotFoundResult(), construction);
    }

    public static LookupResult Through(LookupResult found, ConstructedType construction)
    {
        return found switch
        {
            FieldResult field => field with { BaseConstructions = [construction, .. field.BaseConstructions] },
            PropertyResult property => property with { BaseConstructions = [construction, .. property.BaseConstructions] },
            MethodGroupResult group => group with
            {
                Paths = group.Methods.ToDictionary(
                    method => method,
                    method => (IReadOnlyList<ConstructedType>)[construction, .. group.Paths?.GetValueOrDefault(method) ?? []]),
            },
            _ => found,
        };
    }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        return construction.Definition is IHasTypeParameters generic
            && generic.TypeParametersList.Find(parameter => parameter.Name == request.Name) is { } parameter
            ? new TypeResult(construction.TypeArguments[parameter.Ordinal])
            : new NotFoundResult();
    }
}