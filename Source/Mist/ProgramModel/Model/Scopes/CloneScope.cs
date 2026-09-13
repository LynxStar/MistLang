using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// Stands between a newtype's clone and its source's lexical parent, so the source's declaration binds
/// as the clone: the source's own name answers with the clone, and the source's nested types, which a
/// clone does not copy, still answer by simple name as they do inside the source.
/// </summary>
internal sealed class CloneScope(Scope? parent, ConcreteType source) : Scope(parent)
{
    public ConcreteType? Clone { get; set; }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        if(Clone is not null && request.Name == source.Name)
        {
            return new TypeResult(Clone);
        }

        return source.NestedTypes.Find(nested => nested.Name == request.Name) is { } nested
            ? new TypeResult(nested)
            : new NotFoundResult();
    }
}