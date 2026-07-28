using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// A member declaration's own type parameters, and nothing else nameable -- its locals belong to the
/// <see cref="BodyScope"/> beneath it, its own name to the type above it. Used directly by
/// associated-type slots and derived by <see cref="MethodScope"/>.
/// </summary>
internal class MemberScope(Scope? parent) : Scope(parent), ITypeNameScope
{
    private readonly Dictionary<string, IMistType> typeParameters = [];

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        // A type parameter is neither instance nor static state, so visibility does not apply.
        if(typeParameters.TryGetValue(request.Name, out var typeParameter))
        {
            return new TypeResult(typeParameter);
        }

        return new NotFoundResult();
    }

    /// <inheritdoc />
    public void DeclareType(string name, IMistType type)
    {
        typeParameters[name] = type;
    }
}