using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model.Scopes;

/// <summary>
/// Stands between a newtype's clone and its source's lexical parent, so the source's declaration binds
/// as the clone: the source's own name answers with the clone, each type parameter of a generic source or of
/// the generic types enclosing it with the argument the newtype declaration gives it, and the source's nested
/// types, which a clone does not copy, still answer by simple name as they do inside the source.
/// </summary>
internal sealed class CloneScope(Scope? parent, ConcreteType source, Scope declaration) : Scope(parent)
{
    public ConcreteType Source { get; } = source;

    public ConcreteType? Clone { get; set; }

    public IMistType? Itself { get; set; }

    public Scope Declaration { get; } = declaration;

    public IReadOnlyList<IMistType>? Arguments { get; set; }

    public ConstructedType? Enclosing { get; set; }

    protected override LookupResult LookupSelf(LookupRequest request)
    {
        if(Itself is not null && Source.TypeParametersList.Count == 0 && request.Name == Source.Name)
        {
            return new TypeResult(Itself);
        }

        if(Arguments is not null && Source.TypeParametersList.Find(parameter => parameter.Name == request.Name) is { } parameter)
        {
            return new TypeResult(Arguments[parameter.Ordinal]);
        }

        for(var enclosing = Enclosing; enclosing is not null; enclosing = enclosing.Enclosing)
        {
            if(enclosing.Definition is IHasTypeParameters generic && generic.TypeParametersList.Find(outer => outer.Name == request.Name) is { } outer)
            {
                return new TypeResult(enclosing.TypeArguments[outer.Ordinal]);
            }
        }

        return Source.NestedTypes.Find(nested => nested.Name == request.Name) is { } nested
            ? new TypeResult(nested)
            : new NotFoundResult();
    }
}